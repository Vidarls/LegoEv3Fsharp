namespace Vidarls.Lego
open System
open System.IO

type Volume = Volume of byte
type Frequency = Frequency of uint16
type Duration =  Duration of uint16

type InputPortX =
| Input1
| Input2
| Input3
| Input4
| InputA
| InputB
| InputC
| InputD

type OutputPortX =
| All
| OutputA
| OutputB
| OutputC
| OutputD

/// Devices that can be recognised
/// as input or outpur devices
type DeviceTypeX = 
| NxtTouch
| NxtLight
| NxtSound
| NxtColour
| NxtUltrasonic 
| NxtTempterature 
| LargeMotor
| MediumMotor
| Ev3Touch 
| Ev3Colour
| Ev3Ultrasonic
| Ev3Gyroscope
| Ev3Infrared
| SensorIsInitializing
| NoDeviceConnected
| DeviceConnectedToWrongPort
| UnknownDevice

type Commands = 
| PlayTone of Volume * Frequency * Duration

type Queries =
| GetTypeAndMode of InputPortX list

type Responses =
| TypeAndMode of (InputPortX * DeviceTypeX * string) list



[<RequireQualifiedAccess>]
module Protocol =
    type IncomingWireMessage =
    | Partial of byte []
    | Complete of byte []

    type ResponseOffset = ResponseOffset of uint16
    type ResponseLength = ResponseLength of uint16
    type SentSequenceNumber = SentSequenceNumber of uint16

    type OutgoingMessageData = 
    | Commands of Commands list
    | Queries of (Queries * ResponseOffset * ResponseLength) list
    
    type PreparedMessage = PreparedMessage of OutgoingMessageData * byte []
    type SentMessage = SentMessage of OutgoingMessageData * SentSequenceNumber

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

    module MessageReader =  
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
    
        /// Try to get messagelength form message
        /// byte array
        let getMessageLenght (b:byte []) =
            b |> getShortValueFromMessage 0

        let getMessageSequenceNumber (b:byte[]) =
            b |> getShortValueFromMessage 2    

        let rec evaulateMessageCompleteness (completeMessages:IncomingWireMessage list) (b:byte[]) =
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

    module MessageWriter =
        let initialize (commandType:CommandType) (writer:BinaryWriter) =
             //Write message size placeholder
             writer.Write 0xffffus
             //Write message sequence placeholder
             writer.Write 0xffffus
             //Write command type
             writer.Write (commandType |> byte)
             //Write empty variable bits
             writer.Write 0uy
             writer.Write 0uy

        let writeOpcode (writer:BinaryWriter) (o:Opcode) =
            if o > Opcode.Tst then
                writer.Write ((o >>> 8) |> byte)
            writer.Write (o |> byte)

        let writeByteArg (writer:BinaryWriter) (a:byte) =
            writer.Write (ArgumentSize.Byte|> byte)
            writer.Write a

        let writeShortArg (writer:BinaryWriter) (a:uint16) =
            writer.Write (ArgumentSize.Short |> byte)
            writer.Write a

        /// Sets two bytes to represent the given 
        /// uint16 value starting att given offsett
        /// using little endian
        let setShortValueInMessage (offset:int) (value:uint16) (b:byte[]) =
            b.[0 + offset] <- value |> byte
            b.[1 + offset] <- (value >>> 8) |> byte

        let setMessageLength (b:byte []) =
            let size = b |> Array.length |> (fun l -> l - 2 ) |> uint16
            b |> setShortValueInMessage 0 size

        let setMessageSequenceNumber (messageSequenceNumber:uint16) (b:byte []) =
            b |> setShortValueInMessage 2 messageSequenceNumber

        let getMessageBytes (stream:System.IO.MemoryStream) =
            let buffer = stream.ToArray ()
            buffer |> setMessageLength
            buffer

    module AudioCommands =  
        let writePlayTone (writer:BinaryWriter) (Volume vol) (Frequency freq) (Duration dur) =
            Opcode.SoundTone |> MessageWriter.writeOpcode writer
            vol |> MessageWriter.writeByteArg writer
            freq |> MessageWriter.writeShortArg writer 
            dur |> MessageWriter.writeShortArg writer

    let prepareCommands (commands:Commands list) =
        let writeCommand (writer:BinaryWriter) = function 
            | PlayTone (vol, freq, dur) -> AudioCommands.writePlayTone writer vol freq dur
        
        use stream = (new MemoryStream ())
        use writer = (new BinaryWriter (stream))

        writer |> MessageWriter.initialize CommandType.DirectNoReply

        commands 
        |> List.iter (writeCommand writer)
        PreparedMessage(Commands commands, (stream |> MessageWriter.getMessageBytes))

    let prepareCommand (command:Commands) =
        prepareCommands [command]

    let prepareQueries (queries:Queries list) =
        let writeQuery (writer:BinaryWriter) (oldOffset:ResponseOffset) = function
            | GetTypeAndMode port -> (ResponseOffset 1us, ResponseLength 1us)
        
        use stream = (new MemoryStream ())
        use writer = (new BinaryWriter (stream))

        writer |> MessageWriter.initialize CommandType.DirectReply
        queries
        |> List.mapFold (fun oldOffset q -> 
            let ((ResponseOffset offset), (ResponseLength length)) = q |> writeQuery writer oldOffset
            ((q, ResponseOffset offset, ResponseLength length), ResponseOffset(offset + length))) (ResponseOffset 0us)
        |> (fun (q, _) -> PreparedMessage(Queries(q), (stream |> MessageWriter.getMessageBytes)))

    let prepareQuery (query:Queries) =
        prepareQueries [query]

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
                bytes |> MessageWriter.setMessageSequenceNumber newCount
                SendActions.Send bytes |> send
                return! messageloop newCount
        }
        messageloop 0us
    )
    
    let receiver (s:System.IO.Stream) (receivedHandler:IncomingWireMessage->unit) = MailboxProcessor.Start(fun inbox ->
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
                let (complete, partial) =  data |> MessageReader.evaulateMessageCompleteness []
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


    
        


