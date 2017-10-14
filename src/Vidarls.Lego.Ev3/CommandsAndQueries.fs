namespace Vidarls.Lego.Ev3
open System
open MessageWriter

type Volume = Volume of byte
type Frequency = Frequency of uint16
type Duration =  Duration of uint16

type Commands = 
| PlayTone of Volume * Frequency * Duration

type Responses =
| TypeAndMode of (InputPort * DeviceType * string) 
| Error of Exception

type Queries =
| GetTypeAndMode of InputPort

module Commands =
    module Audio =  
        let playToneCommandContent (Volume vol) (Frequency freq) (Duration dur) =
            [ addOpode Opcode.SoundTone 
              addByteArg vol 
              addShortArg freq  
              addShortArg dur ]

    let getCommandContent = function
        | PlayTone (vol, freq, dur) -> 
                Audio.playToneCommandContent vol freq dur

module Queries =
    module General =
        let typeAndModeResponseParser input initialOffset (bytes:byte[]) =            
            (TypeAndMode (
                input, 
                (bytes.[initialOffset] |> int |> enum<DeviceType>), 
                (sprintf "%A" bytes.[initialOffset + 1])))

        let typeAndModeRequestContent input (offset:byte) =
            [ addOpode Opcode.InputDeviceGetTypeMode 
              addByteArg 0x00uy  // layer 0
              addByteArg (input |> byte) 
              addGlobalIndex offset 
              addGlobalIndex (offset + 1uy) ]

        let prepareTypeAndModeRequest (i:uint16) input =
            (typeAndModeResponseParser input (i |> int)),
            (typeAndModeRequestContent input (i |> byte)),
            2us
    let prepareQuery offset = function
        | GetTypeAndMode input -> General.prepareTypeAndModeRequest offset input
