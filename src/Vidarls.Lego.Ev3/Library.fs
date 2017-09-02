namespace Vidarls.Lego
open System

type Volume = Volume of int
type Frequency = Frequency of int
type Duration = Duration of int

type Action = 
| PlayTone of Volume * Frequency * Duration

type Command =
| DirectCommand of Action

module Ev3 =
    let brick connection = 
        let v = Volume(1)
        connection ()
        printfn "%A" 

