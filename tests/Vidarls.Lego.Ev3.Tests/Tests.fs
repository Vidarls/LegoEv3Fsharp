module Tests

open System
open System.IO
open FsUnit.Xunit
open Xunit
open Xunit.Abstractions
open Vidarls.Lego.Ev3
open Nerdbank


type FakeSerialPort () =
        let stream = (new MemoryStream())
        let writer = (new BinaryWriter(stream))

        member __.Stream = stream

        member __.Write (data:byte []) = 
            writer.Write data
            stream.Position <- stream.Position - (data |> Array.length |> int64)
            
type Fakebrick (response:byte[]) =
    let (stream1:System.IO.Stream, stream2:System.IO.Stream) = FullDuplexStream.CreateStreams()
    let reader = MailboxProcessor.Start (fun inbox ->
        let rec messageLoop () = async {
            let! msg = inbox.Receive ()
            match msg with
            | "read" ->
                let buffer = Array.zeroCreate 1024
                let! bytesRead = stream2.AsyncRead buffer
                if bytesRead > 0 then
                    do! stream2.AsyncWrite response
                do! Async.Sleep 100
                inbox.Post "read"
                return! messageLoop ()
            | "stop" -> return ()
            | _ -> return! messageLoop ()
        }
        inbox.Post "read"
        messageLoop ()
        )

    interface IDisposable 
        with 
            member __.Dispose () =
                reader.Post "stop"
                System.Threading.Thread.Sleep 200
                stream1.Dispose ()
                stream2.Dispose ()

    member __.Stream = stream1



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
            [PlayTone(Volume 75uy, Frequency 1000us, Duration 300us)]
            |> Protocol.prepareCommand
            |> (function | (Protocol.PreparedCommand bytes) -> bytes | _ -> failwith "unexpected type")

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

        let fakePort = (FakeSerialPort ()) 

        let mutable result = Protocol.Complete([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.Complete message)

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Incomplete message size)`` () =
        let message = Array.zeroCreate 1
        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.Partial message))

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, incomplete message)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.setMessageLength
        let incompleteMessage = message |> Array.take 3

        let (complete, incomplete) = incompleteMessage |> Protocol.evaulateMessageCompleteness []
        complete |> should be Empty
        incomplete |> should equal (Some(Protocol.Partial incompleteMessage)) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Message size ok, exact message size)`` () =
        let message = Array.zeroCreate 4
        message |> Protocol.setMessageLength

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []

        complete |> should equal [Protocol.Complete(message)]
        incomplete |> should equal None

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (One complete, one partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.setMessageLength

        let message = Array.append message1 (message2 |> Array.take 3)

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should equal [Protocol.Complete message1]
        incomplete |> should equal (Some(Protocol.Partial (message2 |> Array.take 3))) 

    [<Fact>]
    member __.``Can correctly split a byte array into completed and incomplete parts (Two complete, zero partial)`` () =
        let message1 = Array.zeroCreate 4
        message1 |> Protocol.setMessageLength
        let message2 = Array.zeroCreate 4
        message2 |> Protocol.setMessageLength

        let message = Array.append message1 message2

        let (complete, incomplete) = message |> Protocol.evaulateMessageCompleteness []
        complete |> should equal [Protocol.Complete message1; Protocol.Complete message2 ]
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

        complete |> should equal [Protocol.Complete message1; Protocol.Complete message2 ]
        incomplete |> should equal (Some(Protocol.Partial incompleteMessage3))
        
    [<Fact>]
    member __.``Can receive complete message larger than buffer size`` () =
        let message = Array.zeroCreate 2100
        message |> Protocol.setMessageLength

        let fakePort = (new FakeSerialPort ()) 

        let mutable result = Protocol.Complete([||])
        let e = (new System.Threading.AutoResetEvent(false))
        let receiveHandler = 
            (fun msg -> 
                result <- msg
                e.Set () |> ignore )

        let receiver = Protocol.receiver fakePort.Stream receiveHandler

        fakePort.Write message        
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        e.WaitOne () |> ignore
        
        result |> should equal (Protocol.Complete message)

    [<Fact>]
    member __.``Can stop receiving`` () =
        let fakePort = (new FakeSerialPort ())
        let receiver = Protocol.receiver fakePort.Stream ignore
        Protocol.ReceiveActions.StartReceive |> receiver.Post
        let result = Protocol.ReceiveActions.StopReceive |> receiver.PostAndReply
        result |> should equal Protocol.ActionResult.Success

   
    [<Fact>]
    member __.``Can parse single response`` () =
        let response =  Array.zeroCreate 100
        response |> Protocol.setMessageSequenceNumber 1us
        response |> Protocol.setMessageLength
        response.[5] <- DeviceType.Ev3Colour |> byte
        response.[6] <- Ev3ColourMode.Ambient |> byte
        use fakeBrick = new Fakebrick (response)
        use brick = Brick.CreateFromStream fakeBrick.Stream
       
        let response = brick.DirectQuery [GetTypeAndMode InputPort.A]
        
        match response with
        | [(Responses.TypeAndMode(port,t,mode))] ->
           port |> should equal InputPort.A    
           t |> should equal DeviceType.Ev3Colour
           mode |> should equal (sprintf "%A" (Ev3ColourMode.Ambient |> byte))
        | _ -> failwith "Wroyng resonse"

    [<Fact>]
    member __.``Can parse two responses`` () =
        let response =  Array.zeroCreate 100
        response |> Protocol.setMessageSequenceNumber 1us
        response |> Protocol.setMessageLength
        response.[5] <- DeviceType.Ev3Colour |> byte
        response.[6] <- Ev3ColourMode.Ambient |> byte
        response.[7] <- DeviceType.Ev3Touch |> byte
        response.[8] <- TouchMode.Bumps |> byte
        use fakeBrick = new Fakebrick (response)
        use brick = Brick.CreateFromStream fakeBrick.Stream
       
        let response = brick.DirectQuery [GetTypeAndMode InputPort.A; GetTypeAndMode InputPort.B]
        
        match response with
        | [(Responses.TypeAndMode(port1,t1,mode1)); (Responses.TypeAndMode(port2,t2,mode2))] ->
           port1 |> should equal InputPort.A    
           t1 |> should equal DeviceType.Ev3Colour
           mode1 |> should equal (sprintf "%A" (Ev3ColourMode.Ambient |> byte))
           port2 |> should equal InputPort.B
           t2 |> should equal DeviceType.Ev3Touch
           mode2 |> should equal (sprintf "%A" (TouchMode.Bumps |> byte))
        | _ -> failwith "Wrong resonse"

   


 


   