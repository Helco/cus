using System.Text;

namespace Cus;

public enum CodOpCode
{
    Nop,
    Dup,
    PushAddr,
    PushValue,
    Deref,
    Crash5,
    PopN,
    Store,
    Crash8,
    Crash9,
    LoadString,
    LoadString2,
    Crash12,
    Call,
    KernelProc,
    JumpIfFalse,
    JumpIfTrue,
    Jump,
    Negate,
    BooleanNot,
    Mul,
    Crash21,
    Crash22,
    Add,
    Sub,
    Less,
    Greater,
    LessEquals,
    GreaterEquals,
    Equals,
    NotEquals,
    BitAnd,
    BitOr,
    Crash33,
    Crash34,
    Crash35,
    Crash36,
    Return
}

public readonly record struct CodOp(CodOpCode code, int value);
public readonly record struct CodString(int offset, string value);
public readonly record struct CodVariable(string name, int value);
public readonly record struct CodProcedure(string name, int offset, int unknown);
public readonly record struct CodBehavior(
    string name,
    IReadOnlyList<CodVariable> variables,
    IReadOnlyList<CodProcedure> procedures);

public class CodFile
{
    public int MemorySize { get; }
    public IReadOnlyList<CodString> Strings { get; }
    public IReadOnlyList<CodVariable> GlobalVariables { get; }
    public IReadOnlyList<CodProcedure> GlobalProcedures { get; }
    public IReadOnlyList<CodBehavior> Behaviors { get; }
    public IReadOnlyList<CodOp> Ops { get; }

    public CodFile(string path) : this(new FileStream(path, FileMode.Open, FileAccess.Read)) { }
    public CodFile(Stream stream, bool leaveOpen = false) : this(new BinaryReader(stream, Encoding.UTF8, leaveOpen)) { }
    public CodFile(BinaryReader br, bool newFormat = true)
    {
        var nameBlobSize = br.ReadInt32();
        if (newFormat)
            MemorySize = br.ReadInt32();

        ReadOnlySpan<byte> nameBlob = br.ReadBytes(nameBlobSize).AsSpan();
        var strings = new List<CodString>();
        int offset = 0;
        int nextNullI = nameBlob.IndexOf((byte)0);
        while (nextNullI >= 0)
        {
            strings.Add(new (offset, Encoding.Latin1.GetString(nameBlob[..nextNullI])));
            offset += nextNullI + 1;
            nameBlob = nameBlob[(nextNullI + 1)..];
            nextNullI = nameBlob.IndexOf((byte)0);
        }
        if (nameBlob.Length > 0)
            strings.Add(new(offset, Encoding.Latin1.GetString(nameBlob)));
        Strings = strings;

        GlobalVariables = ReadVariableSet(br);
        GlobalProcedures = ReadProcedureSet(br);
        Behaviors = ReadBehaviorSet(br);
        Ops = ReadOps(br);

        if (!newFormat)
            MemorySize = Math.Max(
                GlobalVariables.MaxOrDefault(v => v.value),
                Behaviors.MaxOrDefault(b => b.variables.MaxOrDefault(v => v.value))) + 4;
    }

    private IReadOnlyList<CodVariable> ReadVariableSet(BinaryReader br)
    {
        int count = br.ReadInt32();
        var variables = new CodVariable[count];
        for (int i = 0; i < count; i++)
            variables[i] = new(ReadVarString(br), br.ReadInt32());
        return variables;
    }

    private IReadOnlyList<CodProcedure> ReadProcedureSet(BinaryReader br)
    {
        int count = br.ReadInt32();
        var procedures = new CodProcedure[count];
        for (int i = 0; i < count; i++)
            procedures[i] = new(ReadVarString(br), br.ReadInt32(), br.ReadInt32());
        return procedures;
    }

    private IReadOnlyList<CodBehavior> ReadBehaviorSet(BinaryReader br)
    {
        int count = br.ReadInt32();
        var behaviors = new CodBehavior[count];
        for (int i = 0; i < count; i++)
            behaviors[i] = new(ReadVarString(br), ReadVariableSet(br), ReadProcedureSet(br));
        return behaviors;
    }

    private IReadOnlyList<CodOp> ReadOps(BinaryReader br)
    {
        int count = br.ReadInt32();
        var ops = new CodOp[count];
        for (int i = 0; i < count; i++)
            ops[i] = new((CodOpCode)br.ReadInt32(), br.ReadInt32());
        return ops;
    }

    private static uint ReadVarInt(BinaryReader br)
    {
        byte b = br.ReadByte();
        if (b != 0xFF) return b;
        ushort s = br.ReadUInt16();
        if (s != 0xFFFF) return s;
        return br.ReadUInt32();
    }

    private static string ReadVarString(BinaryReader br)
    {
        var length = checked((int)ReadVarInt(br));
        return Encoding.Latin1.GetString(br.ReadBytes(length));
    }
}
