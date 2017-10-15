# Lego Ev3 communictations library

Dotnet core - Netstandard 2.0 F# API to interact with a Lego mindstorms EV3 brick (https://www.lego.com/en-us/mindstorms)

Started as a port of Brian Peeks C# implementation (https://github.com/BrianPeek/legoev3). But implementation has diverged quite a bit. Still heavily inspired by Brians work.

## Usage

No nuget package is release yet. Needs a bit more features before it will be published.

To use now, build it or just clone it at add a project reference.

For connections, only bluetooth connections is supported for now. [Here are some ok docs for the pairing process](https://se.mathworks.com/help/supportpkg/legomindstormsev3io/ug/connect-to-an-ev3-brick-over-bluetooth-using-windows-1.html)

## Status

Work is still very much in progress. But most hard parts are done.

### Supported functions

Only bluetooth connection is supported.

Only supports one query and one command:

Play tone:

```F#
use brick = Brick.CreateWithBluetoothConnection "COM4"
[PlayTone (Volume 100uy, Frequency 1000us, Duration 300us)]
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

 ## Build and test

 The project is a vanilla dotnet core project. Navigate to `src/Vidarls.Lego.Ev3` and run the usual `dotnet build` command to build the project.

 Tests are written in Xunit. Navigate to `tests/Vidarls.Lego.Ev3.Tests` and run `dotnet test` to execute the tests. The test project also has a `program.fs` that can be modified and exeuted with `dotnet run` to test stuff on a physical EV3

