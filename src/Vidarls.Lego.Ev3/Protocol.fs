// Core implementation of 
// message creating, sending, receiving and parsing
namespace Vidarls.Lego.Ev3
open System
open System.IO 
        
[<RequireQualifiedAccess>]
/// Put these internals behind qualified access
/// to avoid accidental leak of internals
/// Can still be accessed by anyone who wants to (but intentionaly)
/// 
/// EV# communications is handled by agens (mailboxprocessors) to ensure:
/// * Only one thread writes / reads to communications streams
/// * We can keep a mutable buffer of sent queries without having to think about
///   locking and mutexes and such
/// * Allow the user to program async and not having ot wait for communications
module Protocol =
    /// Represents a response where not all bytes
    /// indicated by message length bytes are received yet
    type Partial = Partial of byte []
    /// Represents a received response with exactly all bytes
    /// as indicated by message length bytes.
    type Complete = Complete of byte []

    /// Sequence number of sent message
    /// used to correlate responses with sent queries
    type SentSequenceNumber = SentSequenceNumber of uint16

    /// Represents a command ready to be sent
    type PreparedCommand = PreparedCommand of byte []

    /// Represents a query ready to be sent
    /// Consists of
    /// * a list of response parser functions
    /// * the reply channel where resonses should be sent
    /// * the bytes to send
    type PreparedQuery = PreparedQuery of (byte [] -> Responses) list  * AsyncReplyChannel<Responses list> * byte []

    /// Represents the flow of a query
    /// to be able to handle errors during send
    /// (WIP - not implemented yet, only naive send)
    type SentQuery = 
    | QuerySendStarted of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QuerySendConfirmed of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QuerySendFailed of (byte [] -> Responses) list * SentSequenceNumber * AsyncReplyChannel<Responses list>
    | QueryCompleted

    /// Represents success or failure
    /// of infrastructure 
    /// Not really used, but unsure of whether to implement properly
    /// or delete (possible performance implications)
    type ActionResult =
    | Success
    | Failure of Exception
    
    /// Messages to the send agent
    type SendActions = 
    /// Command to send a byte array to the 
    /// EV3. Includes the sequence number
    /// to give the Sending agent the possibility
    /// report errors back to the coordinator (WIP, not yet implemented)
    | Send of byte[] * SentSequenceNumber
    /// Infrastructure message to stop sending agent during shutdown
    /// Could not get to work, currently relying on Dispose method of 
    /// Mailboxprocessor
    | Die of AsyncReplyChannel<ActionResult>

    /// Messages to the receiver agent
    type ReceiveActions =
    /// Command to start listening to input stream
    | StartReceive
    /// (Used indernally in agent)
    /// Command to continue receiving bytes to
    /// complete a message
    | ContinueReceive of byte []
    /// (Used internally in agent)
    /// Command to check if bytes received includes
    /// any complete responses
    | CheckReceived of byte []
    /// Infrastructure message to stop sending agent during shutdown
    /// Could not get to work, currently relying on Dispose method of 
    /// Mailboxprocessor
    | StopReceive of AsyncReplyChannel<ActionResult>

    /// Messages to the coordinator agent
    type CoordinatorActions =
    /// Command to send a list of commands 
    /// to the EV3
    | SendCommands of Commands list
    /// Command to send a list of queries to the EV3
    /// Includes a response channel to write responses to
    | SendQueries of Queries list * AsyncReplyChannel<Responses list>
    /// Message intended for sending agent
    /// to inform coordinator agent that message was
    /// send successfully (WIP - not implemented yet)
    | ConfirmSentQueries of SentSequenceNumber
    /// Message intended for sending agent
    /// to inform coordinator agent that message send failed
    /// (WIP - not implemented yet)
    | SendFailed of SentSequenceNumber * Exception
    /// Message for receiving agent to inform coordinator agent
    /// that a new reponse message has been received
    | Received of Complete

    /// Initialize a new binary message
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

    /// Sets two bytes to represent the given 
    /// uint16 value starting att given offsett
    /// using little endian
    let setShortValueInMessage (offset:int) (value:uint16) (b:byte[]) =
        b.[0 + offset] <- value |> byte
        b.[1 + offset] <- (value >>> 8) |> byte

    /// Set the message lenght bytes of 
    /// the provided message byte array
    let setMessageLength (b:byte []) =
        let size = b |> Array.length |> (fun l -> l - 2 ) |> uint16
        b |> setShortValueInMessage 0 size

    /// Set the sequence number bytes in the
    /// provided message byte array
    let setMessageSequenceNumber (messageSequenceNumber:uint16) (b:byte []) =
        b |> setShortValueInMessage 2 messageSequenceNumber

    /// Sets the global variable index size bytes
    /// of the provided message byte array
    /// Ported from Bryan Peeks implementation:
    /// https://github.com/BrianPeek/legoev3/blob/master/Lego.Ev3.Core/Command.cs
    let setGlobalIndexSize (size:uint16) (bytes:byte []) =
        if (bytes |> Array.length) < 7 then failwith "incomplete message"
        if size > 1024us then failwith "Size must be 1024 or less"
        bytes.[5] <- size |> byte //lowe bits of global size
        bytes.[6] <- (0uy ||| ((size >>> 8) |> byte) &&& 0x03uy) // lower bits of global size 
        bytes

    /// Gets the message bytes from the memory stream
    /// used to build the message
    /// NOTE: Also sets the message length
    let getMessageBytes (stream:System.IO.MemoryStream) =
        let buffer = stream.ToArray ()
        buffer |> setMessageLength
        buffer

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

    /// Try to get messagelength from message
    /// byte array
    /// Returns None if byte array too short
    let getMessageLenght (b:byte []) =
        b |> getShortValueFromMessage 0

    /// Try to get message sequence number from
    /// byte array
    /// returns None if byte array is too short
    let getMessageSequenceNumber (b:byte[]) =
        b |> getShortValueFromMessage 2    

    /// Given a received byte array
    /// this function will attempt to read the 
    /// message length bytes of the message
    /// and check if the byte array contains the complete message
    /// returns a list of complete messages (supports multiple)
    /// and an option of bytes of any remaining incpomlete message
    ///
    /// Used to ensure receiving agent only forwards complete messages
    /// to the coordinator agent
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
    
    /// Tranforms a list of commands from user to
    /// binary command for the EV3
    let prepareCommand (commands:Commands list) =
        use stream = (new MemoryStream ())
        use writer = (new BinaryWriter (stream))

        // Step 1: Initialize message
        initialize CommandType.DirectNoReply writer

        commands 
        // Step2: Get the binary mappings
        |> List.map Commands.getCommandContent
        // and flatten
        |> List.collect id
        /// Step 3: Write the binary mappings
        |> List.iter (fun commandContent -> (commandContent writer))

        PreparedCommand(stream |> getMessageBytes)

    /// Transforms a list of queries from user
    /// to binary query commands for EV3
    let prepareQuery (queries:Queries list) (reply:AsyncReplyChannel<Responses list>) =
        /// internal function to
        /// write the binary query command
        let writeRequestContent requestContent =       
            use stream = (new MemoryStream ())
            use writer = (new BinaryWriter (stream))
            initialize CommandType.DirectReply writer
            requestContent |> List.iter (fun w -> w writer)
            getMessageBytes stream

        // transform the list of queries to 
        // the list of repsonse parsers, the bytes to send
        // and the complete length of responses
        let (getResponses, bytes, length) =
            queries
            // using mapfold to keep an aggregate of 
            // total response length so each
            // query mapping / response parser can be created
            // with correct index of global variable index
            |> List.mapFold 
                (fun i q ->
                    // calls out to query defintions to get the mappings
                    let getResponses, requestContent, length = Queries.prepareQuery i q
                    // returns results and aggregates length if response
                    ((getResponses, requestContent), i + length)) 0us
            |> (fun (lists, length) -> 
                    // gets response parsers and binary content as 
                    // separate lists
                    let getResponses, requestContent = lists |> List.unzip
                    // returns response parsers, and writes the byte array ready to send
                    (getResponses, requestContent |> List.collect id |> writeRequestContent, length))
        PreparedQuery(getResponses, reply, (bytes |> setGlobalIndexSize length))

    /// Main agent running the show
    ///
    /// The coordinator is responsible for keeping track of sent messages
    /// so responses are sent back using the correct response channel
    /// This is the agent the user is interacting with when programming the 
    /// EV3.
    /// Args:
    /// * send: Function to send the message to EV3 (post function of send agent)
    let coordinator (send:SendActions->unit) = MailboxProcessor.Start(fun inbox -> 
        // EV3 message sequence numbering are represented by 
        // an unsigned short value, hence max number of messages
        // to keep track of is Uint116.MaxValue
        let bufferSize = System.UInt16.MaxValue
        // buffer to keep sent messages
        let buffer = Array.zeroCreate (bufferSize |> int)

        /// helper function to ensure we don't use message 0
        /// and don't exceed Uint16.MaxValue (seems to be handled
        /// by the clr in an ok way, but if any issues should occur
        /// can be handled here)
        let rec increaseMessageCount count =
            if count = bufferSize then 
                increaseMessageCount 0us
            else
                count + 1us

        /// Main agent control logic and message handling
        /// Message sequence number is kept by
        // the coutn argument
        let rec messageloop count = async {
            let! msg = inbox.Receive ()
            match msg with
            // Sending a command
            // We do not store sent commands, as we do not require 
            // to track responses to them
            | SendCommands commands ->
                // Prepare command
                let (PreparedCommand bytes) = prepareCommand commands
                // ensure sequnece number is increased and set
                let newCount = count |> increaseMessageCount
                bytes |> setMessageSequenceNumber newCount                
                SendActions.Send (bytes, SentSequenceNumber newCount) |> send
                return! messageloop newCount
            // Sending aa query 
            // sent queries are saved in the buffer, so response can be sent back to the 
            // provided reply channel
            | SendQueries (queries, reply) ->
                // preapre query (get hte bytes and reponse parsers)
                let (PreparedQuery (parsers, reply, bytes)) = prepareQuery queries reply
                // ensure sequnece number is increased and set
                let newCount = count |> increaseMessageCount
                bytes |> setMessageSequenceNumber newCount
                // Save the query to the buffer
                buffer.[newCount |> int] <- QuerySendStarted (parsers, SentSequenceNumber newCount, reply)  
                // send
                SendActions.Send (bytes, SentSequenceNumber newCount) |> send
                return! messageloop newCount
            // WIP acc from sending agent. not implemented
            | ConfirmSentQueries (SentSequenceNumber numb) ->
                buffer.[numb |> int] |> function
                | QuerySendStarted (readers, seq, repl) -> buffer.[numb |> int] <- QuerySendConfirmed (readers, seq, repl)
                | _ -> ()
                return! messageloop count
            // WIP NAK from sending agent, not implemented
            | SendFailed (SentSequenceNumber numb, ex) -> 
                buffer.[numb |> int] |> function
                | QuerySendStarted (message, seq, repl) -> 
                    buffer.[numb |> int] <- QuerySendFailed (message, seq, repl)
                    repl.Reply ([Error ex])
                | _ -> ()
                return! messageloop count
            // Handling a received response message
            | Received(Complete bytes) ->
                // to locate the source query, get the sequence number
                // from received message
                bytes |> getMessageSequenceNumber |> function
                // unable to find sequence number
                // happens only if the EV3 should send a 0 length response message
                | None -> () // TODO log warning
                // Sequence number found
                | Some (seq) -> 
                    buffer.[seq |> int] |> function
                    // buffer had a sent query at the sequence position
                    | QuerySendStarted (parsers, _, repl) | QuerySendConfirmed (parsers, _, repl) ->  
                        // get the response data byte (skip header bytes)
                        // TODO: Handle success / error respone codes
                        let bytes = bytes |> Array.skip 5  
                        // Use parsers from source query reqyest and reply using source 
                        // query reply channel                     
                        parsers |> List.map (fun parse -> parse bytes) |> repl.Reply
                        // mark query as completed
                        buffer.[seq |> int] <- QueryCompleted
                    // TODO handle missing query at buffer position
                    | _ -> () //TODO log warning
                return! messageloop count
        }
        messageloop 0us
    )
    
    /// Receiving agent
    /// Ensures only one thread attempts to read from
    /// input stream.
    /// Heavily inspired by "proper serial port reading" from Ben Voigt:
    /// http://www.sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
    ///
    /// Implemented using stream as abstraction to be able to easily implement
    /// netword and USB communications later
    ///
    /// Args:
    /// * s: input data stream to read responses from
    /// * receive handler: function to pass complete messages to (The coordinator post function)
    let receiver (s:System.IO.Stream) receivedHandler = MailboxProcessor.Start(fun inbox ->
        let rec messageloop () = async {
            let! msg = inbox.Receive ()
            match msg with
            // Start receiving with no incomplete messages to complete
            | StartReceive ->                
                let buffer = Array.zeroCreate 1024
                let! bytesRead = s.AsyncRead buffer    
                // once data received post back a check received command
                // to make agent check for complete messages before 
                // continuing receiving
                // this enables passing on complete messages and then 
                // continue receiving incomplete messages           
                inbox.Post (CheckReceived (buffer |> Array.take bytesRead))
                return! messageloop ()
            // Continue receiving bytes adding to an incomplete message
            | ContinueReceive data ->
                let buffer = Array.zeroCreate 1024
                let! bytesRead = s.AsyncRead buffer
                // once data received post back a check received command
                // to make agent check for complete messages before 
                // continuing receiving
                // this enables passing on complete messages and then 
                // continue receiving incomplete messages    
                inbox.Post (CheckReceived (Array.append data (buffer |> Array.take bytesRead)))
                return! messageloop ()
            // Check receeived data for complete messages
            | CheckReceived data ->
                // Split received data in complete and incomplete messages
                let (complete, partial) =  data |> evaulateMessageCompleteness []
                // Pass complete messages to coordinator
                complete |> List.iter receivedHandler
                match partial with
                // If a partial message is present
                // Post back a continue receuve message
                // so we ensure we keep building on the same 
                // message
                | Some(Partial d) ->
                    inbox.Post (ContinueReceive d)
                    return! messageloop ()
                // No incpomlete messagem, we start over again
                // posting a start receive command
                | _ -> 
                    inbox.Post StartReceive
                    return! messageloop ()
            // internal command to stop agent
            // WIP could not get this to work
            // currently relying on mailboxprocessor dispose for
            // a clean shutdown
            | StopReceive channel ->
                channel.Reply Success
                return ()
        }
        messageloop ()
    )

    /// Sending agent
    /// ensures only one thread tries to write to output stream
    /// Quite straight forward
    /// NOTE: No error handling closed streams or connections will
    /// result in hang / hidden exceptions
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
       


    
        


