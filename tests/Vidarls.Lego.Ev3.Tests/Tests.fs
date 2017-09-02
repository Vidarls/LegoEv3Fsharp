module Tests

open System
open Xunit
open Vidarls.Lego

[<Fact>]
let ``Can play tone on ev3 brick`` () =
    let mutable x = false
    let dostuff v = x <- true
    let brick = Ev3.brick dostuff
    PlayTone |> brick 
    Assert.True x
