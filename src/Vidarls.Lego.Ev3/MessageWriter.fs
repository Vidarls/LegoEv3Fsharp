namespace Vidarls.Lego.Ev3
open System.IO

/// Helper functions to create terse
/// Command to binary mappings
/// Put in separate file to keep the 
/// commands and queries file focused 
module MessageWriter =
    open System.IO
    /// Add an opcode
    /// Ported from Brian Peeks implementation
    /// https://github.com/BrianPeek/legoev3/blob/master/Lego.Ev3.Core/Command.cs
    ///
    /// Opccodes as given in Lego documentation are 
    /// composed by a main opcode and a sub opcode
    /// In the enums, Brian has combined them but when 
    /// writing the bytes they need to be uncombined.
    let addOpode (o:Opcode)  (writer:BinaryWriter) =
        if o > Opcode.Tst then
            writer.Write ((o >>> 8) |> byte)
        writer.Write (o |> byte)

    /// Adds a global index pointer / reference value
    /// This is a two byte value, where the first byte is 
    /// a flag indicating that we are passing a global
    /// index reference
    let addGlobalIndex (index:byte) (writer:BinaryWriter) =
        writer.Write 0xe1uy
        writer.Write index

    let addByteArg (a:byte) (writer:BinaryWriter) =
        writer.Write (ArgumentSize.Byte|> byte)
        writer.Write a

    let addShortArg (a:uint16) (writer:BinaryWriter) =
        writer.Write (ArgumentSize.Short |> byte)
        writer.Write a

    /// Writes constant 0 layer value
    /// meaning directly connected brick
    /// (Daisychaning not supported)
    let layer0 (writer:BinaryWriter) =
        writer.Write 0x00uy

    /// Writes the output port argument from
    /// the given output port list
    ///
    /// Most motor control commands supports
    /// sending same command to multiple outputs
    /// in the binary protocol this i enabled by combining
    /// outputport flags setting the bits in the output port byte
    /// corresponding to the output port
    /// This requires a bit of binary knowledge to understand
    /// it is easier to pass a list of output ports and combine them during mapping
    let addOutputPorts (outputPorts:OutputPort list) (writer:BinaryWriter) =
        let outputPortvalue =
            outputPorts |> List.fold (fun value outputPort -> (outputPort |> byte) ||| value) 0uy
        writer.Write outputPortvalue

    /// Add brake setting to a motor command
    let addBrakeSetting (brakeSetting:BrakeSetting) (writer:BinaryWriter) =
        writer.Write (brakeSetting |> byte)

    /// Adds a percentage value like speed of power
    /// Can be negative, so can not be represented by a literal byte value
    let addSignedByteArg (value:sbyte)  =
        addByteArg (value |> byte) 

    

 