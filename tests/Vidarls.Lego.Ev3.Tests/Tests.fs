module Tests

open System
open System.IO
open FsUnit.Xunit
open Xunit
open Xunit.Abstractions
open Vidarls.Lego

type FakeSerialPort () =
        let stream = (new MemoryStream())
        let writer = (new BinaryWriter(stream))

        member __.Stream = stream

        member __.Write (data:byte []) = 
            writer.Write data
            stream.Position <- stream.Position - (data |> Array.length |> int64)

type ``Protocol tests`` (output:ITestOutputHelper) =
    let toneString = "0F 00 FF FF 80 00 00 94 01 81 4B 82 E8 03 82 2C 01"
    let toneStringNoHeaders = "94 01 81 4B 82 E8 03 82 2C 01"

    let stringToByteArray (s:string) =
        s.Split([|" "|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun hex -> Convert.ToByte(hex, 16))
        
    [<Fact>]
    member __. ``Can convert tone command to byte array`` () =
        let expected = toneString |> stringToByteArray
        
        let result = 
            PlayTone(Volume 75uy, Frequency 1000us, Duration 300us)
            |> Protocol.prepareCommand
            |> (function | Protocol.OutgoingMessage.Prepared (_,bytes) -> bytes | _ -> failwith "unexpected type")

        output.WriteLine (sprintf "%A" expected)
        output.WriteLine (sprintf "%A" result)        
        Assert.Equal<byte []> (expected, result)

    [<Fact>]
    member __.``Can set and retrieve message length`` () = 
        let message = Array.zeroCreate 1024 //expected message length + 2 bytes for message length
        message |> Protocol.MessageWriter.setMessageLength
        let length = message |> Protocol.MessageReader.getMessageLenght
        length |> should equal (Some 1022us)

    [<Fact>]
    member __.``Can set and retrieve message sequence number`` () =
        let message = Array.zeroCreate 1024
        message |> Protocol.MessageWriter.setMessageSequenceNumber 44444us
        let sequenceNumber = message |> Protocol.MessageReader.getMessageSequenceNumber
        sequenceNumber |> should equal (Some(44444us))

    [<Fact>]
    member __.``Can receive complete message smaller than buffer size`` () =
        let message = Array.zeroCreate 500
        message |> Protocol.MessageWriter.setMessageLength

        let fakePort = (new FakeSerialPort ()) 

        let mutable result = Protocol.IncomingWireMessage.Partial([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.IncomingWireMessage.Complete message)

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Incomplete message size)`` () =
        let message = Array.zeroCreate 1
        let (complete, incomplete) = message |> Protocol.MessageReader.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.IncomingWireMessage.Partial message))

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, incomplete message)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.MessageWriter.setMessageLength
        let incompleteMessage = message |> Array.take 3

        let (complete, incomplete) = incompleteMessage |> Protocol.MessageReader.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.IncomingWireMessage.Partial incompleteMessage)) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, exact message size)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.MessageWriter.setMessageLength

        let (complete, incomplete) = message |> Protocol.MessageReader.evaulateMessageCompleteness []

        complete |> should equal [Protocol.IncomingWireMessage.Complete(message)]
        incomplete |> should equal None

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (One complete, one partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.MessageWriter.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.MessageWriter.setMessageLength

        let message = Array.append message1 (message2 |> Array.take 3)

        let (complete, incomplete) = message |> Protocol.MessageReader.evaulateMessageCompleteness []
        complete |> should equal [Protocol.IncomingWireMessage.Complete message1]
        incomplete |> should equal (Some(Protocol.IncomingWireMessage.Partial (message2 |> Array.take 3))) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Two complete, zero partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.MessageWriter.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.MessageWriter.setMessageLength

        let message = Array.append message1 message2

        let (complete, incomplete) = message |> Protocol.MessageReader.evaulateMessageCompleteness []
        complete |> should equal [Protocol.IncomingWireMessage.Complete message1; Protocol.IncomingWireMessage.Complete message2 ]
        incomplete |> should equal None 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Two complete, 1 partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.MessageWriter.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.MessageWriter.setMessageLength
        let message3 = Array.zeroCreate 4
        message3 |> Protocol.MessageWriter.setMessageLength
        let incompleteMessage3 = message3 |> Array.take 3

        let message = Array.append (Array.append message1 message2) incompleteMessage3

        let (complete, incomplete) = message |> Protocol.MessageReader.evaulateMessageCompleteness []

        complete |> should equal [Protocol.IncomingWireMessage.Complete message1; Protocol.IncomingWireMessage.Complete message2 ]
        incomplete |> should equal (Some(Protocol.IncomingWireMessage.Partial incompleteMessage3))
        
    [<Fact>]
    member __.``Can receive complete message larger than buffer size`` () =
        let message = Array.zeroCreate 2100
        message |> Protocol.MessageWriter.setMessageLength

        let fakePort = (new FakeSerialPort ()) 

        let mutable result = Protocol.IncomingWireMessage.Partial([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.IncomingWireMessage.Complete message)

    [<Fact>]
    member __.``Can stop receiving`` () =
        let fakePort = (new FakeSerialPort ())
        let receiver = Protocol.receiver fakePort.Stream ignore
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        let result = Protocol.ReceiveActions.StopReceive |> receiver.PostAndReply
        result |> should equal Protocol.ActionResult.Success

    [<Theory>]
    [<InlineData(1,1us)>]
    [<InlineData(2,2us)>]
    [<InlineData(100,100us)>]
    [<InlineData(65535, 65535us)>]
    [<InlineData(65536, 1us)>]
    [<InlineData(65537, 2us)>]
    [<InlineData(131070,65535us)>]
    [<InlineData(131071, 1us)>]
    member __.``Coordinator increases message count by 1 for each message, retarts 65535`` (numberOfMessagesToSend:int) (expectedCount:uint16) =
        let e = (new System.Threading.AutoResetEvent(false))
        let mutable result = Protocol.SendActions.Send [||]
        
        let mutable counter = 0
        let sendAction = (fun (a:Protocol.SendActions) ->
            result <- a
            counter <- counter + 1
            if counter = (numberOfMessagesToSend) then e.Set () |> ignore)

        let coord = Protocol.coordinator sendAction ignore
        
        for i = 1 to numberOfMessagesToSend do
            Protocol.CoordinatorActions.Send (Array.zeroCreate 8)
            |> coord.Post

        e.WaitOne () |> ignore
       
        match result with
        | Protocol.SendActions.Send bytes ->
            let messageSequence = bytes |> Protocol.MessageReader.getMessageSequenceNumber
            messageSequence |> should equal (Some expectedCount)
        | _ -> failwith "unexpected messagetype"

    //member __.``Can receive response to query`` () =

        

        






   