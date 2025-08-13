using System.Text;

namespace Cus;

// the semantic, cross-generation op code
public enum CodOpCode
{
    Nop,
    Dup,
    PushAddr,
    PushDynAddr,
    PushValue,
    Deref,
    Crash,
    Pop1,
    PopN,
    Store,
    LoadString,
    Call,
    KernelProc,
    JumpIfFalse,
    JumpIfTrue,
    Jump,
    Negate,
    BooleanNot,
    Mul,
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
    ReturnNone,
    ReturnValue,
    InvalidOp
}

public readonly record struct CodOp(int rawCode, CodOpCode code, int value);
public readonly record struct CodString(int offset, string value);
public readonly record struct CodVariable(string name, int value);
public readonly record struct CodProcedure(string name, int offset, int unknown);
public readonly record struct CodBehavior(
    string name,
    IReadOnlyList<CodVariable> variables,
    IReadOnlyList<CodProcedure> procedures);

public class CodFile
{
    private readonly CodOpCode[] rawOpsToCode;
    public int MemorySize { get; }
    public IReadOnlyList<CodString> Strings { get; }
    public IReadOnlyList<CodVariable> GlobalVariables { get; }
    public IReadOnlyList<CodProcedure> GlobalProcedures { get; }
    public IReadOnlyList<CodBehavior> Behaviors { get; }
    public IReadOnlyList<CodOp> Ops { get; }
    public Generation Generation { get; }

    public CodFile(string path) : this(new FileStream(path, FileMode.Open, FileAccess.Read)) { }
    public CodFile(Stream stream, bool leaveOpen = false) : this(new BinaryReader(stream, Encoding.UTF8, leaveOpen)) { }
    public CodFile(BinaryReader br, Generation generation = Generation.V3)
    {
        Generation = generation;
        rawOpsToCode = generation is Generation.V1 ? V1Ops : V3Ops;

        var nameBlobSize = br.ReadInt32();
        if (generation is not Generation.V1)
            MemorySize = br.ReadInt32();

        ReadOnlySpan<byte> nameBlob = br.ReadBytes(nameBlobSize).AsSpan();
        var strings = new List<CodString>();
        int offset = 0;
        int nextNullI = nameBlob.IndexOf((byte)0);
        while (nextNullI >= 0)
        {
            strings.Add(new(offset, Encoding.Latin1.GetString(nameBlob[..nextNullI])));
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

        if (generation is Generation.V1)
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
        {
            int rawCode = br.ReadInt32();
            var code = rawCode < rawOpsToCode.Length ? rawOpsToCode[rawCode] : CodOpCode.InvalidOp;
            ops[i] = new(rawCode, code, br.ReadInt32());
        }
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

    private static readonly CodOpCode[] V3Ops =
    [
        CodOpCode.Nop,
        CodOpCode.Dup,
        CodOpCode.PushAddr,
        CodOpCode.PushValue,
        CodOpCode.Deref,
        CodOpCode.Crash,
        CodOpCode.PopN,
        CodOpCode.Store,
        CodOpCode.Crash,
        CodOpCode.Crash,
        CodOpCode.LoadString,
        CodOpCode.LoadString,
        CodOpCode.Crash,
        CodOpCode.Call,
        CodOpCode.KernelProc,
        CodOpCode.JumpIfFalse,
        CodOpCode.JumpIfTrue,
        CodOpCode.Jump,
        CodOpCode.Negate,
        CodOpCode.BooleanNot,
        CodOpCode.Mul,
        CodOpCode.Crash,
        CodOpCode.Crash,
        CodOpCode.Add,
        CodOpCode.Sub,
        CodOpCode.Less,
        CodOpCode.Greater,
        CodOpCode.LessEquals,
        CodOpCode.GreaterEquals,
        CodOpCode.Equals,
        CodOpCode.NotEquals,
        CodOpCode.BitAnd,
        CodOpCode.BitOr,
        CodOpCode.Crash,
        CodOpCode.Crash,
        CodOpCode.Crash,
        CodOpCode.Crash,
        CodOpCode.ReturnValue
    ];

    private static readonly CodOpCode[] V1Ops =
    [
        CodOpCode.Nop,
        CodOpCode.Dup,
        CodOpCode.PushAddr,
        CodOpCode.PushValue,
        CodOpCode.Deref,
        CodOpCode.Nop,
        CodOpCode.Pop1,
        CodOpCode.Store,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.PushDynAddr,
        CodOpCode.Nop,
        CodOpCode.Call,
        CodOpCode.KernelProc,
        CodOpCode.JumpIfFalse,
        CodOpCode.JumpIfTrue,
        CodOpCode.Jump,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Add,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Equals,
        CodOpCode.NotEquals,
        CodOpCode.BitAnd,
        CodOpCode.BitOr,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.Nop,
        CodOpCode.ReturnNone
    ];
}

public static class CodDumper
{
    public static void FullDump(CodFile cod, TypeDescriptorBlock types, CodeWriter writer, bool rawOps)
    {
        writer.Write("MemorySize: ");
        writer.Write(cod.MemorySize);
        writer.WriteLine();
        writer.WriteLine();

        writer.WriteLine("Strings: ");
        using (var indented = writer.Indented)
        {
            foreach (var (offset, name) in cod.Strings)
                indented.WriteLine($"{offset:D5}: {name}");
        }
        writer.WriteLine();

        WriteVariables(cod.GlobalVariables, writer);
        writer.WriteLine();

        WriteProcedures(cod.GlobalProcedures, writer);
        writer.WriteLine();

        writer.WriteLine("Behaviors: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, behavior) in cod.Behaviors.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.WriteLine(behavior.name);
                WriteVariables(behavior.variables, indented);
                WriteProcedures(behavior.procedures, indented);
                indented.WriteLine();
            }
        }
        writer.WriteLine();

        writer.WriteLine("Ops: ");
        using (var indented = writer.Indented)
        {
            if (rawOps)
            {
                foreach (var (index, (rawOpCode, opCode, arg)) in cod.Ops.Indexed())
                    indented.WriteLine($"{index:D5}: {rawOpCode:D2} {opCode} {arg}");
            }
            else
                SimpleDecompiler.Decompile(cod, types, indented, false);
        }
    }

    public static void WriteProcedures(IReadOnlyList<CodProcedure> procedures, CodeWriter writer)
    {
        writer.WriteLine("Procedures: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, proc) in procedures.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.Write(proc.name);
                indented.Write(" @ ");
                indented.Write(proc.offset);
                indented.Write(" with ");
                indented.Write(proc.unknown);
                indented.WriteLine();
            }
        }
    }

    public static void WriteVariables(IReadOnlyList<CodVariable> variables, CodeWriter writer)
    {
        writer.WriteLine("Variables: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, variable) in variables.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.Write(variable.name);
                indented.Write(" = ");
                indented.Write(variable.value);
                indented.WriteLine();
            }
        }
    }
}
