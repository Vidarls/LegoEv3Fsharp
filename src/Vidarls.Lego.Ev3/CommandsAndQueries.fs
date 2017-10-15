// Command and query definitions
//
// To add support for more commands and queries
// add to the definitions
namespace Vidarls.Lego.Ev3
open System
open MessageWriter

// Types used in command and query definitions
// These are the types uses will be interacting
// directly with
type Volume = Volume of byte
type Frequency = Frequency of uint16
type Duration =  Duration of uint16

/// Suppported commmands
type Commands = 
| PlayTone of Volume * Frequency * Duration

/// Supported device modes
/// Unification of all XXmode enums in
/// enums.fs
type Modes =
| MotorMode of MotorMode
| UnsupportedMode of string

/// Supported queries
type Queries =
| GetTypeAndMode of InputPort

/// Strongly types responses to queries
type Responses =
| TypeAndMode of (InputPort * DeviceType * Modes) 
| Error of Exception

/// Command defintions
/// Defines how a command given in the 
/// Commands type above maps to the 
/// Lego EV3 binary protocol
///
/// To add support for more commands
/// Check the descriptions in:
/// * Lego mindstorms firmeare development kit (PDF):
///   https://lc-www-live-s.legocdn.com/r/www/r/mindstorms/-/media/franchises/mindstorms%202014/downloads/firmware%20and%20software/advanced/lego%20mindstorms%20ev3%20firmware%20developer%20kit.pdf?l.r2=830923294
/// * The commands implementations of Brian peeks C# implementation:
///   https://github.com/BrianPeek/legoev3/blob/master/Lego.Ev3.Core/Command.cs
/// 
/// The above resources should give good pointers on how to communicate a specific command to the EV3
/// 
/// To add a new command:
///
/// 1. Add the command type to the Commands union above
/// 2. Add any new required argument types above (More specific types gives a better developer experience)
/// 3. Add a new function in the Commands module (in a fitting submodule) that defines the transformation from
///    command to binary data
/// 4. Add the mapping between command and function to the getCommandContent function
module Commands =
    module Audio =  
        let playToneCommandContent (Volume vol) (Frequency freq) (Duration dur) =
            [ addOpode Opcode.SoundTone 
              addByteArg vol 
              addShortArg freq  
              addShortArg dur ]


    /// Function for mapping between command types
    /// and command to binary transformation function
    let getCommandContent = function
        | PlayTone (vol, freq, dur) -> 
                Audio.playToneCommandContent vol freq dur

/// Query defintions
/// Defines how a query given in the 
/// Queries type above maps to the 
/// Lego EV3 binary protocol
///
/// To add support for more queries
/// Check the descriptions in:
/// * Lego mindstorms firmeare development kit (PDF):
///   https://lc-www-live-s.legocdn.com/r/www/r/mindstorms/-/media/franchises/mindstorms%202014/downloads/firmware%20and%20software/advanced/lego%20mindstorms%20ev3%20firmware%20developer%20kit.pdf?l.r2=830923294
/// * The commands implementations of Brian peeks C# implementation:
///   https://github.com/BrianPeek/legoev3/blob/master/Lego.Ev3.Core/Command.cs
/// 
/// The above resources should give good pointers on how to communicate a specific query to the EV3
/// 
/// To add a new query:
///
/// 1. Add the query type to the Queries union above
/// 2. Add the response type (if required) to the Responses union above
/// 3. Add any new required argument types above (More specific types gives a better developer experience)
/// 4. Add a three functions in the Queries module (in a fitting submodule):
///     a. One that defines the transformation from Query type to binary data 
///     b. One that defines how to parse the reponse 
///     c. One that returns a tuple of responseParser * query transformation * total length of response
/// 5. Add the mapping between command and tuple returning function to the prepareQuery function
module Queries =
    /// General queries, not device specific
    module General =
        let typeAndModeResponseParser input initialOffset (bytes:byte[]) =   
            // offset in parser given to byte array is same as set in 
            // query transformer of where in response array the response should be written

            let deviceType = bytes.[initialOffset] |> int |> enum<DeviceType>
            let modeValue = bytes.[initialOffset + 1] |> int
            let mode = 
                match deviceType with
                | DeviceType.LargeMotor | DeviceType.MediumMotor ->
                    MotorMode (modeValue |> enum<MotorMode>)   
                | _ -> UnsupportedMode (sprintf "%A" bytes.[initialOffset + 1])

            TypeAndMode (input, deviceType, mode)

        let typeAndModeRequestContent input (offset:byte) =
            [ addOpode Opcode.InputDeviceGetTypeMode 
              addByteArg 0x00uy  // layer 0
              addByteArg (input |> byte) 
              addGlobalIndex offset // Global var index for device type response, corresponds to index in response byte array in parser
              addGlobalIndex (offset + 1uy) ] // Global var index for mode response, corresponds to index in response byte array in parser

        let prepareTypeAndModeRequest (i:uint16) input =
            (typeAndModeResponseParser input (i |> int)),
            (typeAndModeRequestContent input (i |> byte)),
            2us //Total length of respone is 2 bytes
    
    /// Maps query types to a function that returns
    /// a tuple of responseParser * query transformer * response lenght
    /// 
    /// args: 
    /// * Offset : Current offset to use for parsing / setting index in 
    ///            global variable indes
    /// * Query : query to prepare
    let prepareQuery offset = function
        | GetTypeAndMode input -> General.prepareTypeAndModeRequest offset input
