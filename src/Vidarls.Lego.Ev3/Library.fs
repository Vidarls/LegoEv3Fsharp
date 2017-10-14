namespace Vidarls.Lego.Ev3
open System
open System.IO

type Volume = Volume of byte
type Frequency = Frequency of uint16
type Duration =  Duration of uint16

type Commands = 
| PlayTone of Volume * Frequency * Duration

type Responses =
| TypeAndMode of (InputPort * DeviceType * string) 
| Error of Exception

type Queries =
| GetTypeAndMode of InputPort

[<RequireQualifiedAccess>]
module Protocol =
    type Partial = Partial of byte []
    type Complete = Complete of byte []
    type SentSequenceNumber = SentSequenceNumber of uint16
     
    type PreparedCommand = PreparedCommand of byte []
    type PreparedQuery = PreparedQuery of (byte [] -> Responses) list  * AsyncReplyChannel<Responses list> * byte []
    type SentQuery = 
    | QuerySendStarted of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QuerySendConfirmed of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QuerySendFailed of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QueryCompleted

    type ActionResult =
    | Success
    | Failure of Exception
    
    type SendActions = 
    | Send of byte[] * SentSequenceNumber
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

        let rec evaulateMessageCompleteness (completeMessages:Complete list) (b:byte[]) =
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

        let writeGlobalIndex (writer:BinaryWriter) (index:byte) =
            writer.Write 0xe1uy
            writer.Write index

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

        let setGlobalIndexSize (size:uint16) (bytes:byte []) =
            if (bytes |> Array.length) < 7 then failwith "incomplete message"
            if size > 1024us then failwith "Size must be 1024 or less"
            bytes.[5] <- size |> byte //lowe bits of global size
            bytes.[6] <- (0uy ||| ((size >>> 8) |> byte) &&& 0x03uy) // lower bits of global size 
            bytes

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

    module Queries =
        let getTypeAndModeResponse input initialOffset (bytes:byte[]) =            
            (TypeAndMode (
                input, 
                (bytes.[initialOffset] |> int |> enum<DeviceType>), 
                (sprintf "%A" bytes.[initialOffset + 1])))

        let writeTypeAndModeRequest input (offset:byte) (writer:BinaryWriter) =
            Opcode.InputDeviceGetTypeMode |> MessageWriter.writeOpcode writer
            0x00uy |> MessageWriter.writeByteArg writer // layer 0
            input |> byte |> MessageWriter.writeByteArg writer
            offset |> MessageWriter.writeGlobalIndex writer
            (offset + 1uy) |> MessageWriter.writeGlobalIndex writer

        let prepareTypeAndModeRequest (i:uint16) input =
            (getTypeAndModeResponse input (i |> int)),
            (writeTypeAndModeRequest input (i |> byte)),
            2us

    let prepareCommands (commands:Commands list) =
        let writeCommand (writer:BinaryWriter) = function 
            | PlayTone (vol, freq, dur) -> AudioCommands.writePlayTone writer vol freq dur
        
        use stream = (new MemoryStream ())
        use writer = (new BinaryWriter (stream))

        writer |> MessageWriter.initialize CommandType.DirectNoReply

        commands 
        |> List.iter (writeCommand writer)
        PreparedCommand(stream |> MessageWriter.getMessageBytes)

    let prepareCommand (command:Commands) =
        prepareCommands [command]


    let prepareQueries (queries:Queries list) (reply:AsyncReplyChannel<Responses list>) =
        let prepare i = function
        | GetTypeAndMode input -> Queries.prepareTypeAndModeRequest i input

        let write writers =       
            use stream = (new MemoryStream ())
            use writer = (new BinaryWriter (stream))
            writer |> MessageWriter.initialize CommandType.DirectReply
            writers |> List.iter (fun w -> w writer)
            stream |> MessageWriter.getMessageBytes

        let (getResponses, bytes, length) =
            queries
            |> List.mapFold 
                (fun i q ->
                    let getResponses, writeRequest, length = prepare i q
                    ((getResponses, writeRequest), i + length)) 0us
            |> (fun (lists, length) -> 
                    let getResponses, writeRequests = lists |> List.unzip
                    (getResponses, writeRequests |> write, length))
        PreparedQuery(getResponses, reply, (bytes |> MessageWriter.setGlobalIndexSize length))

    let prepareQuery (query:Queries) =
        prepareQueries [query]

    type CoordinatorActions =
    | SendCommands of Commands list
    | SendQueries of Queries list * AsyncReplyChannel<Responses list>
    | ConfirmSentQueries of SentSequenceNumber
    | SendFailed of SentSequenceNumber * Exception
    | Received of Complete

    let coordinator (send:SendActions->unit) = MailboxProcessor.Start(fun inbox -> 
        let bufferSize = System.UInt16.MaxValue
        let buffer = Array.zeroCreate (bufferSize |> int)
        let rec increaseMessageCount count =
            if count = bufferSize then 
                increaseMessageCount 0us
            else
                count + 1us

        let rec messageloop count = async {
            let! msg = inbox.Receive ()
            match msg with
            | SendCommands commands ->
                let (PreparedCommand bytes) = prepareCommands commands
                let newCount = count |> increaseMessageCount
                bytes |> MessageWriter.setMessageSequenceNumber newCount
                SendActions.Send (bytes, SentSequenceNumber newCount) |> send
                return! messageloop newCount
            | SendQueries (queries, reply) ->
                let (PreparedQuery (readers, reply, bytes)) = prepareQueries queries reply
                let newCount = count |> increaseMessageCount
                bytes |> MessageWriter.setMessageSequenceNumber newCount
                buffer.[newCount |> int] <- QuerySendStarted (readers, SentSequenceNumber newCount, reply)  
                SendActions.Send (bytes, SentSequenceNumber newCount) |> send
                return! messageloop newCount
            | ConfirmSentQueries (SentSequenceNumber numb) ->
                buffer.[numb |> int] |> function
                | QuerySendStarted (readers, seq, repl) -> buffer.[numb |> int] <- QuerySendConfirmed (readers, seq, repl)
                | _ -> ()
                return! messageloop count
            | SendFailed (SentSequenceNumber numb, ex) -> 
                buffer.[numb |> int] |> function
                | QuerySendStarted (message, seq, repl) -> 
                    buffer.[numb |> int] <- QuerySendFailed (message, seq, repl)
                    repl.Reply ([Error ex])
                | _ -> ()
                return! messageloop count
            | Received(Complete bytes) ->
                bytes |> MessageReader.getMessageSequenceNumber |> function
                | None -> () // TODO log warning
                | Some (seq) -> 
                    buffer.[seq |> int] |> function
                    | QuerySendStarted (readers, _, repl) | QuerySendConfirmed (readers, _, repl) ->  
                        let bytes = bytes |> Array.skip 5                       
                        readers |> List.map (fun read -> read bytes) |> repl.Reply
                        buffer.[seq |> int] <- QueryCompleted
                    | _ -> () //TODO log warning
                return! messageloop count
        }
        messageloop 0us
    )
    
    let receiver (s:System.IO.Stream) receivedHandler = MailboxProcessor.Start(fun inbox ->
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
            | SendActions.Send (bytes, seq) ->               
                do! s.AsyncWrite bytes
                return! messageloop ()
            | SendActions.Die channel ->
                channel.Reply Success
                return ()
        }
        messageloop ()
    )

type Brick private (stream, disposePort) =
    let sender = Protocol.sender stream
    let coord = Protocol.coordinator sender.Post 
    let receiver = Protocol.receiver stream (Protocol.Received >> coord.Post)
    do Protocol.StartReceive |> receiver.Post
    
    let toQuery queries =
        (fun replyChannel -> Protocol.SendQueries(queries, replyChannel))

    member __.DirectQuery queries =
        coord.PostAndReply ((queries |> toQuery))

    member __.DirectCommand commands =
        commands |> Protocol.SendCommands |> coord.Post

    interface IDisposable with
        member __.Dispose () =
            (receiver :> IDisposable).Dispose ()
            (coord :> IDisposable).Dispose ()
            (sender :> IDisposable).Dispose ()
            disposePort ()
            
    static member CreateWithBluetoothConnection comPort =
        let availableComPports = Ports.SerialPort.GetPortNames ()
        let comPortIsValid =
            availableComPports
            |> Array.exists (fun p -> String.Equals(p, comPort, StringComparison.InvariantCultureIgnoreCase))
        if not comPortIsValid then failwith (sprintf "Selected com port: %s is not valid, must be one of %A" comPort availableComPports)
        let port = (new Ports.SerialPort(comPort, 115200))
        port.DataReceived.Add (fun d -> printfn "DR: %A" d)
        do port.Open ()
        
        new Brick(port.BaseStream, port.Dispose)

    static member CreateFromStream stream =
        new Brick(stream, id)
        
       


    
        


