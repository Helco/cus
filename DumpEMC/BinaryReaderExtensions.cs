using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cus;

public static class BinaryReaderExtensions
{
    public static uint ReadVarUInt(this BinaryReader reader)
    {
        byte b = reader.ReadByte();
        if (b != 0xFF) return b;
        ushort s = reader.ReadUInt16();
        if (s != 0xFFFF) return s;
        return reader.ReadUInt32();
    }

    public static string ReadVarString(this BinaryReader reader)
    {
        var length = reader.ReadVarUInt();
        var bytes = reader.ReadBytes(checked((int)length));
        return Encoding.Latin1.GetString(bytes);
    }
}
