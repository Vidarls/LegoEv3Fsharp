namespace Vidarls.Lego.Ev3
open System
open System.IO

/// API entry point for interacting with your EV3
/// Represents a connection to a SINGLE EV3 brick (daisy chaining not implemented)
type Brick private (stream, disposePort) =
    // Set up the agents
    let sender = Protocol.sender stream
    let coord = Protocol.coordinator sender.Post 
    let receiver = Protocol.receiver stream (Protocol.Received >> coord.Post)
    // start the receiver listening to the input stream
    do Protocol.StartReceive |> receiver.Post
    
    /// Helper function to wrap a list of queries
    /// in a Coordinator message with a reply channel
    let toQuery queries =
        (fun replyChannel -> Protocol.SendQueries(queries, replyChannel))

    /// Send a list of queries to your EV3 brick and wait for the responses
    ///
    /// Args:
    /// * queries: List of queries to send
    ///
    /// Returns:
    /// * a list of responses from the EV3
    member __.DirectQuery queries =
        coord.PostAndReply ((queries |> toQuery))

    /// Send a list of commands to your EV3 brick
    ///
    /// Args:
    /// * commands: List of commands to send 
    member __.DirectCommand commands =
        commands |> Protocol.SendCommands |> coord.Post

    /// Ensures proper shutdown of agents and BT serial port (if used)
    interface IDisposable with
        member __.Dispose () =
            (receiver :> IDisposable).Dispose ()
            (coord :> IDisposable).Dispose ()
            (sender :> IDisposable).Dispose ()
            disposePort ()
            
    /// Create a brick connected to the EV3 using bluetooth
    ///
    /// Args:
    /// * comPor: Name of comport created by EV3 pairing
    ///
    /// Returns:
    /// * brick object connected to the EV3 using bluetooth connection
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

    /// Create a brick when connection is handled externally
    ///
    /// Args:
    /// * stream : The stream to write requests and read responses from
    ///
    /// Returns
    /// * brick object connected to EV3 by external means
    static member CreateFromStream stream =
        new Brick(stream, id)