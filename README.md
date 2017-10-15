# Lego Ev3 communictations library

Dotnet core - Netstandard 2.0 F# API to interact with a Lego mindstorms EV3 brick (https://www.lego.com/en-us/mindstorms)

Started as a port of Brian Peeks C# implementation (https://github.com/BrianPeek/legoev3). But implementation has diverged quite a bit. Still heavily inspired by Brians work.

## Goal

My goal is to make a library that makes it as easy and non-technical as possible to create useful and fun robots with the Lego Mindstorms EV3 kit using a proper programming language. I hope that it can be used to get kids into enjoying programming, by making it easy to *make stuff happen*

I want to avoid leaking unnecesary technical details of the underlaying protocols, keeping the programming model focused on what you want to do. I hope to achieve this by leveraging the rich F# type system to represent concepts that are easy to understand, and easy to get right.

## Usage

No nuget package is release yet. Needs a bit more features before it will be published.

To use now, build it or just clone it at add a project reference.

For connections, only bluetooth connections is supported for now. [Here are some ok docs for the pairing process](https://se.mathworks.com/help/supportpkg/legomindstormsev3io/ug/connect-to-an-ev3-brick-over-bluetooth-using-windows-1.html)

## Status

Work is still very much in progress. But most hard parts are done.

### Supported functions

Only bluetooth connection is supported.

Probably only supports Windows desktop. (`System.IO.Serialport` used for bluetooth communication is in netstandard 2.0, but I am not sure other platforms are abstracting bluetooth with serialport in the same way)

Only supports one query and four commands:

Play tone:

```F#
use brick = Brick.CreateWithBluetoothConnection "COM4"
[PlayTone (Volume 100uy, Frequency 1000us, Duration 300us)]
|> brick.DirectCommand
```

```F#
use brick = Brick.CreateWithBluetoothConnection "COM4"
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
```

Get type and mode:

```F#
use brick = Brick.CreateWithBluetoothConnection "COM4"
 [GetTypeAndMode InputPort.B]
 |> brick.DirectQuery
 |> printfn "%A"

 // prints
 // [TypeAndMode (B, LargeMotor, MotorMode Degrees)]
 ```

 ### Adding support for more commands and queries

 I am planning to implement support for the basic commands and queries 
 requried to make useful robots with the devices included in the basic
 lego mindstorms kit.

 Adding new commands and queries is quite straight forward

 See comments and imlementation example in [CommandsAndQueries.fs](https://github.com/Vidarls/LegoEv3Fsharp/blob/master/src/Vidarls.Lego.Ev3/CommandsAndQueries.fs) for details on what is required to add more.

 ### Programming model

 Exactly how to interact with the EV3 is still not decided. I probably need some time play-testing it to figure out how the best approach is.

 Currently only two API methods are implemented:

 1. `Brick.DirectQuery` for blocking queries to get sensor / output statuses
 2. `Brick.DirectCommand` for sending commands to the EV3

 This is probably enough (and simple enough) for a lot of kid type projects. Where it falls short will be clear once I (or others) starts using it.

 ## Build and test

 The project is a vanilla dotnet core project. Navigate to `src/Vidarls.Lego.Ev3` and run the usual `dotnet build` command to build the project.

 Tests are written in Xunit. Navigate to `tests/Vidarls.Lego.Ev3.Tests` and run `dotnet test` to execute the tests. The test project also has a `program.fs` that can be modified and exeuted with `dotnet run` to test stuff on a physical EV3

