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

    

 