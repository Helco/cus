using System.Text;

namespace Cus;

internal enum CodOpCode
{
    Nop,
    Dup,
    PushAddr,
    PushValue,
    Deref,
    Crash,
    PopN,
    Store,
    Crash8,
    Crash9,
    PushName,
    PushName2,
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

internal readonly record struct CodOp(CodOpCode code, int value);
internal readonly record struct CodVariable(string name, int value);
internal readonly record struct CodProcedure(string name, int offset, int unknown);
internal readonly record struct CodBehavior(
    string name,
    IReadOnlyList<CodVariable> variables,
    IReadOnlyList<CodProcedure> procedures);

internal class CodFile
{
    public int MemorySize { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<CodVariable> GlobalVariables { get; }
    public IReadOnlyList<CodProcedure> GlobalProcedures { get; }
    public IReadOnlyList<CodBehavior> Behaviors { get; }
    public IReadOnlyList<CodOp> Ops { get; }

    public CodFile(string path) : this(new FileStream(path, FileMode.Open, FileAccess.Read)) { }
    public CodFile(Stream stream, bool leaveOpen = false) : this(new BinaryReader(stream, Encoding.UTF8, leaveOpen)) { }
    public CodFile(BinaryReader br)
    {
        var nameBlobSize = br.ReadInt32();
        MemorySize = br.ReadInt32();

        ReadOnlySpan<byte> nameBlob = br.ReadBytes(nameBlobSize).AsSpan();
        var names = new List<string>();
        int nextNullI = nameBlob.IndexOf((byte)0);
        while (nextNullI >= 0)
        {
            names.Add(Encoding.Latin1.GetString(nameBlob[..nextNullI]));
            nameBlob = nameBlob[(nextNullI + 1)..];
            nextNullI = nameBlob.IndexOf((byte)0);
        }
        if (nameBlob.Length > 0)
            names.Add(Encoding.Latin1.GetString(nameBlob));
        Names = names;

        GlobalVariables = ReadVariableSet(br);
        GlobalProcedures = ReadProcedureSet(br);
        Behaviors = ReadBehaviorSet(br);
        Ops = ReadOps(br);
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
