namespace Vidarls.Lego.Ev3
open System
open System.IO

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