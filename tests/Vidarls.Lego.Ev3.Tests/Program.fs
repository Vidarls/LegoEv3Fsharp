open Xunit
open Xunit.Abstractions
module Program = 
    let [<EntryPoint>] main _ = 
        let tests = (new Tests.``Protocol tests`` null)
        tests.``Can receive complete message smaller than buffer size`` ()
        System.Console.ReadLine () |> ignore
        0


