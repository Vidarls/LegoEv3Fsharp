open Xunit
open Xunit.Abstractions
open Vidarls.Lego.Ev3
module Program = 
    let [<EntryPoint>] main _ = 
        use brick = Brick.CreateWithBluetoothConnection "COM4"
        [PlayTone (Volume 100uy, Frequency 1000us, Duration 300us)
         PlayTone (Volume 100uy, Frequency 2000us, Duration 400us)]
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
        0


