namespace Vidarls.Lego
open System
open System.IO

type Volume = Volume of byte
type Frequency = Frequency of uint16
type Duration =  Duration of uint16

type Commands = 
| PlayTone of Volume * Frequency * Duration

type Queries =
| GetTypeAndMode of InputPort

[<RequireQualifiedAccess>]
module Protocol =
    type WireMessage =
    | Partial of byte []
    | Complete of byte []

    type ActionResult =
    | Success
    | Failure of Exception
    
    type SendActions = 
    | Send of byte[]
    | Die of AsyncReplyChannel<ActionResult>

    type ReceiveActions =
    | StartReceive
    | ContinueReceive of byte []
    | CheckReceived of byte []
    | StopReceive of AsyncReplyChannel<ActionResult>


    /// Sets two bytes to represent the given 
    /// uint16 value starting att given offsett
    /// using little endian
    let setShortValueInMessage (offset:int) (value:uint16) (b:byte[]) =
        b.[0 + offset] <- value |> byte
        b.[1 + offset] <- (value >>> 8) |> byte
    
    /// Reads two bytes in the message starting
    /// at offset, returning the combines value (little endian)
    /// as a uint16 value
    let getShortValueFromMessage (offset:int) (b:byte[]) =
        // If message is to short to get both bytes
        // we return None
        if (b |> Array.length) < (offset + 2) then
            None 
        else    
            Some(((b.[1 + offset] |> uint16) <<< 8) + (b.[0 + offset] |> uint16))

    let setMessageLength (b:byte []) =
        let size = b |> Array.length |> (fun l -> l - 2 ) |> uint16
        b |> setShortValueInMessage 0 size
    
    /// Try to get messagelength form message
    /// byte array
    let getMessageLenght (b:byte []) =
        b |> getShortValueFromMessage 0

    let setMessageSequenceNumber (messageSequenceNumber:uint16) (b:byte []) =
        b |> setShortValueInMessage 2 messageSequenceNumber

    let getMessageSequenceNumber (b:byte[]) =
        b |> getShortValueFromMessage 2

    let rec evaulateMessageCompleteness (completeMessages:WireMessage list) (b:byte[]) =
       match (b |> getMessageLenght) with
       | None -> completeMessages, Some(Partial b)
       | Some l ->
            match (l + 2us) with
            // Exact match
            | len when len  = (b|> Array.length |> uint16) ->
                Complete(b) :: completeMessages, None
            // remaining bytes are less than expected
            | len when len > (b|> Array.length |> uint16) ->
                completeMessages, Some(Partial b)                
            // Remaining bytes are more than expected
            // (More than one message)
            | len when len < (b|> Array.length |> uint16) ->
                let (c, rest) = b |> Array.splitAt (len |> int) 
                evaulateMessageCompleteness (Complete(c)::completeMessages) rest
            | _ -> completeMessages, None


    type Payload (commandType:CommandType) =
        let stream = (new MemoryStream())
        let writer = (new BinaryWriter(stream))
        do
            let writeMessageSizePlaceholder () =
                writer.Write 0xffffus
            let writeMessageSequencePlaceholder () =
                writer.Write 0xffffus
            let writeCommandType () =
                writer.Write (commandType |> byte)
            let writeVariableBits () =
                writer.Write 0uy
                writer.Write 0uy
            writeMessageSizePlaceholder ()
            writeMessageSequencePlaceholder ()
            writeCommandType ()
            writeVariableBits ()

        interface IDisposable with
            member this.Dispose () =
                writer.Dispose ()
                stream.Dispose ()

        member __.ToBytes (messageNumber:uint16) =
            let buffer = stream.ToArray ()
            buffer |> setMessageLength
            buffer |> setMessageSequenceNumber messageNumber
            buffer

        member __.Opcode (o:Opcode) =
            if o > Opcode.Tst then
                writer.Write ((o >>> 8) |> byte)
            writer.Write (o |> byte)
        member __.ByteArg (a:byte) =
            writer.Write (ArgumentSize.Byte|> byte)
            writer.Write a

        member __.ShortArg (a:uint16) =
            writer.Write (ArgumentSize.Short |> byte)
            writer.Write a

    type CoordinatorActions =
    | Send of byte[]

    let coordinator (send:SendActions->unit) replyReceived = MailboxProcessor.Start(fun inbox -> 
        let bufferSize = 2048
        let rec increaseMessageCount count =
            if count = System.UInt16.MaxValue then 
                increaseMessageCount 0us
            else
                count + 1us

        let rec messageloop count = async {
            let! msg = inbox.Receive ()
            match msg with
            | Send bytes ->
                let newCount = count |> increaseMessageCount
                bytes |> setMessageSequenceNumber newCount
                SendActions.Send bytes |> send
                return! messageloop newCount
        }
        messageloop 0us
    )
    let receiver (s:System.IO.Stream) (receivedHandler:WireMessage->unit) = MailboxProcessor.Start(fun inbox ->
        let rec messageloop () = async {
            let! msg = inbox.Receive ()
            match msg with
            | StartReceive ->
                let buffer = Array.zeroCreate 1024
                let! bytesRead = s.AsyncRead buffer
                inbox.Post (CheckReceived (buffer |> Array.take bytesRead))
                return! messageloop ()
            | ContinueReceive data ->
                let buffer = Array.zeroCreate 1024
                let! bytesRead = s.AsyncRead buffer
                inbox.Post (CheckReceived (Array.append data (buffer |> Array.take bytesRead)))
                return! messageloop ()
            | CheckReceived data ->
                let (complete, partial) =  data |> evaulateMessageCompleteness []
                complete |> List.iter receivedHandler
                match partial with
                | Some(Partial d) ->
                    inbox.Post (ContinueReceive d)
                    return! messageloop ()
                | _ -> 
                    inbox.Post StartReceive
                    return! messageloop ()
            | StopReceive channel ->
                channel.Reply Success
                return ()
        }
        messageloop ()
    )

    let sender (s:System.IO.Stream) = MailboxProcessor.Start(fun inbox ->
        let rec messageloop () = async {
            let! msg = inbox.Receive ()
            match msg with
            | SendActions.Send bytes -> 
                do! s.AsyncWrite bytes
                return! messageloop ()
            | SendActions.Die channel ->
                channel.Reply Success
                return ()
        }
        messageloop ()
    )

    let PlayTone (p:Payload) (PlayTone (Volume (vol), Frequency (freq), Duration (dur))) =
        p.Opcode Opcode.SoundTone
        p.ByteArg vol 
        p.ShortArg freq 
        p.ShortArg dur 
    
[<RequireQualifiedAccess>]
module Bluetooth =

    type Message =
    | Connect of AsyncReplyChannel<Protocol.ActionResult>
    | Disconnect of AsyncReplyChannel<Protocol.ActionResult>
    | Send of byte []


    let connection (comport:string) = MailboxProcessor.Start(fun inbox ->
        
        let rec messageLoop port = async{
            let! msg = inbox.Receive()
            match msg, port with
            | Connect channel, None-> 
                try
                    let p = (new System.IO.Ports.SerialPort(comport, 115200))                    
                    p.Open ()                    
                    channel.Reply Protocol.ActionResult.Success
                    return! messageLoop (Some p)
                with 
                | ex -> 
                    channel.Reply (Protocol.ActionResult.Failure ex)
                    return ()
            | Connect channel, Some connection->
                channel.Reply Protocol.ActionResult.Success
                return! messageLoop (Some connection)
            | Disconnect channel, Some connection -> 
                try
                    connection.Close ()
                    connection.Dispose ()
                    channel.Reply Protocol.ActionResult.Success
                    return! messageLoop None
                with
                | ex -> 
                    channel.Reply (Protocol.ActionResult.Failure ex)
                    return ()
            | Disconnect channel, None -> 
                channel.Reply Protocol.ActionResult.Success
                return! messageLoop None
            | Send bytes, Some connection ->
                connection.Write (bytes,0,(bytes |> Array.length))
                return! messageLoop (Some connection)
            | Send bytes, None -> return! messageLoop None


            return! messageLoop None
        }
        messageLoop None
    ) 

    let x = connection ""
    let xx = x.PostAndReply Connect

module Ev3 =
    let runTest () =
        // use p = (new Protocol.Payload(CommandType.DirectNoReply))
        // PlayTone(Volume 100uy, Frequency 1000us, Duration 1000us)
        // |> Protocol.PlayTone p
        // let btconn = Bluetooth.connection "COM4"
        // let connected = Bluetooth.Message.Connect |> btconn.PostAndReply
        // match connected with
        // | Bluetooth.ConnectionResult.Success -> printfn "YAY connected"
        // | Bluetooth.ConnectionResult.Failure ex -> raise ex
        // (Bluetooth.Message.Send (p.ToBytes(1us))) |> btconn.Post
        // let disconnected = Bluetooth.Message.Disconnect |> btconn.PostAndReply
        // match disconnected with
        // | Bluetooth.ConnectionResult.Success -> printfn "YAY disconnected"
        // | Bluetooth.ConnectionResult.Failure ex -> raise ex

        let com = (new System.IO.Ports.SerialPort("COM1", 115200))
        com.Open ()
        let com2 = (new System.IO.Ports.SerialPort("COM2", 115200))
        com2.Open ()
        let receiver = Protocol.receiver com.BaseStream ignore
        receiver.Post Protocol.ReceiveActions.StartReceive
        com2.Write("Hello world")
        System.Threading.Thread.Sleep 1000
        com2.Write "Hello again"


    
        


