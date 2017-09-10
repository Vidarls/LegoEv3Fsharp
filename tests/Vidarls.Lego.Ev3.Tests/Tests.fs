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
    let toneString = "0F 00 03 00 80 00 00 94 01 81 4B 82 E8 03 82 2C 01"
    let toneStringNoHeaders = "94 01 81 4B 82 E8 03 82 2C 01"

    let stringToByteArray (s:string) =
        s.Split([|" "|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun hex -> Convert.ToByte(hex, 16))
        
    [<Fact>]
    member __. ``Can convert tone command to byte array`` () =
        let expected = toneString |> stringToByteArray
        use p = (new Protocol.Payload (CommandType.DirectNoReply))
        PlayTone(Volume 75uy, Frequency 1000us, Duration 300us)
        |> Protocol.PlayTone p

        let result = p.ToBytes 3us
        output.WriteLine (sprintf "%A" expected)
        output.WriteLine (sprintf "%A" result)        
        Assert.Equal<byte []> (expected, result)

    [<Fact>]
    member __.``Can set and retrieve message length`` () = 
        let message = Array.zeroCreate 1024 //expected message length + 2 bytes for message length
        message |> Protocol.setMessageLength
        let length = message |> Protocol.getMessageLenght
        length |> should equal (Some 1022us)

    [<Fact>]
    member __.``Can set and retrieve message sequence number`` () =
        let message = Array.zeroCreate 1024
        message |> Protocol.setMessageSequenceNumber 44444us
        let sequenceNumber = message |> Protocol.getMessageSequenceNumber
        sequenceNumber |> should equal (Some(44444us))

    


    [<Fact>]
    member __.``Can receive complete message smaller than buffer size`` () =
        let message = Array.zeroCreate 500
        message |> Protocol.setMessageLength

        let fakePort = (new FakeSerialPort ()) 

        let mutable result = Protocol.WireMessage.Partial([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.WireMessage.Complete message)

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Incomplete message size)`` () =
        let message = Array.zeroCreate 1
        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.WireMessage.Partial message))

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, incomplete message)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.setMessageLength
        let incompleteMessage = message |> Array.take 3

        let (complete, incomplete) = incompleteMessage |> Protocol.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.WireMessage.Partial incompleteMessage)) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, exact message size)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.setMessageLength

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []

        complete |> should equal [Protocol.WireMessage.Complete(message)]
        incomplete |> should equal None

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (One complete, one partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.setMessageLength

        let message = Array.append message1 (message2 |> Array.take 3)

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should equal [Protocol.WireMessage.Complete message1]
        incomplete |> should equal (Some(Protocol.WireMessage.Partial (message2 |> Array.take 3))) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Two complete, zero partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.setMessageLength

        let message = Array.append message1 message2

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should equal [Protocol.WireMessage.Complete message1; Protocol.WireMessage.Complete message2 ]
        incomplete |> should equal None 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Two complete, 1 partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.setMessageLength
        let message3 = Array.zeroCreate 4
        message3 |> Protocol.setMessageLength
        let incompleteMessage3 = message3 |> Array.take 3

        let message = Array.append (Array.append message1 message2) incompleteMessage3

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []

        complete |> should equal [Protocol.WireMessage.Complete message1; Protocol.WireMessage.Complete message2 ]
        incomplete |> should equal (Some(Protocol.WireMessage.Partial incompleteMessage3))
        
    [<Fact>]
    member __.``Can receive complete message larger than buffer size`` () =
        let message = Array.zeroCreate 2100
        message |> Protocol.setMessageLength

        let fakePort = (new FakeSerialPort ()) 

        let mutable result = Protocol.WireMessage.Partial([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.WireMessage.Complete message)

    [<Fact>]
    member __.``Can stop receiving`` () =
        let fakePort = (new FakeSerialPort ())
        let receiver = Protocol.receiver fakePort.Stream ignore
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        let result = Protocol.ReceiveActions.StopReceive |> receiver.PostAndReply
        result |> should equal Protocol.ActionResult.Success

    [<Fact>]
    member __.``Coordinator incresases message sequence number by 1 for each message`` () =
        let e = (new System.Threading.AutoResetEvent(false))
        let mutable result = Protocol.SendActions.Send [||]
        let sendAction = (fun (a:Protocol.SendActions) -> 
            result <- a
            e.Set () |> ignore
            )
        let coord = Protocol.coordinator sendAction ignore
        Protocol.CoordinatorActions.Send (Array.zeroCreate 8)
        |> coord.Post
        e.WaitOne () |> ignore
        Protocol.CoordinatorActions.Send (Array.zeroCreate 8)
        |> coord.Post
        e.WaitOne () |> ignore
        match result with
        | Protocol.SendActions.Send bytes ->
            let messageSequence = bytes |> Protocol.getMessageSequenceNumber
            messageSequence |> should equal (Some 2us)
        | _ -> failwith "Wrong type given"



        






   