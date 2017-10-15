open Xunit
open Xunit.Abstractions
open Vidarls.Lego.Ev3
module Program = 
    let [<EntryPoint>] main _ = 
        use brick = Brick.CreateWithBluetoothConnection "COM4"
        [PlayTone (Volume 100uy, Frequency 1000us, Duration 300us)]
        |> brick.DirectCommand
        [GetTypeAndMode InputPort.A; 
         GetTypeAndMode InputPort.B; 
         GetTypeAndMode InputPort.C; 
         GetTypeAndMode InputPort.D; 
         GetTypeAndMode InputPort.One; 
         GetTypeAndMode InputPort.Two;
         GetTypeAndMode InputPort.Three;
         GetTypeAndMode InputPort.Four]
        |> brick.DirectQuery
        |> printfn "%A"
        [ SetMotorSpeed ([OutputPort.B; OutputPort.C], Speed 100y); StartMotor [OutputPort.B; OutputPort.C]]
        |> brick.DirectCommand
        printfn "Press enter to turn"
        System.Console.ReadLine () |> ignore
        [ SetMotorSpeed ([OutputPort.B], Speed -100y); ]
        |> brick.DirectCommand
        printfn "Press enter to stop"
        System.Console.ReadLine () |> ignore
        [ StopMotor ([OutputPort.B; OutputPort.C], BrakeSetting.Coast) ]
        |> brick.DirectCommand
        0
        



