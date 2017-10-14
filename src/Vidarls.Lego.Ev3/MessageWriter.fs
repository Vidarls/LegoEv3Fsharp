namespace Vidarls.Lego.Ev3
open System.IO

module MessageWriter =
    open System.IO
    let addOpode (o:Opcode)  (writer:BinaryWriter) =
        if o > Opcode.Tst then
            writer.Write ((o >>> 8) |> byte)
        writer.Write (o |> byte)

    let addGlobalIndex (index:byte) (writer:BinaryWriter) =
        writer.Write 0xe1uy
        writer.Write index

    let addByteArg (a:byte) (writer:BinaryWriter) =
        writer.Write (ArgumentSize.Byte|> byte)
        writer.Write a

    let addShortArg (a:uint16) (writer:BinaryWriter) =
        writer.Write (ArgumentSize.Short |> byte)
        writer.Write a

    

 