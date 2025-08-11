using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Formats.Asn1;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cus;

public enum Generation
{
    V1,
    V2,
    V3,
    Unknown
}

public enum TypeDescriptorKind : byte
{
    End = 0,
    ObjectRelations,
    ExternalType,
    Relation,
    ObjectType,
    EnumType,
    KernelCall,
    Constructor = 17,
    Method = 25,
    Properties = 0xfe,

    // these are mine
    PropertiesRelation = 0xA0,
    CollectionRelation
}

public interface ITypeDescriptor
{
    TypeDescriptorKind DescriptorKind { get; }
    string Name { get; }

    void WriteTo(CodeWriter writer);
}

public interface ITypeRef : ITypeDescriptor { }

public readonly record struct ExternalType(
    string Name,
    string InternalName,
    byte Unk1,
    byte Unk2,
    byte Unk3,
    uint Unk4)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.ExternalType;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"external-type {Name} ({Name}, {Unk1:X2}, {Unk2:X2}, {Unk3:X2}, {Unk4:X8})");
    }
}

public readonly record struct ObjectRelations(
    string Name,
    IReadOnlyList<string> BaseTypes,
    IReadOnlyList<ITypeDescriptor> Relations)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.ObjectRelations;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"relations {Name} {{");
        using (var indented = writer.Indented)
        {
            foreach (var baseType in BaseTypes)
                indented.WriteLine($"base {baseType}");
            foreach (var relation in Relations)
                relation.WriteTo(indented);
        }
        writer.WriteLine('}');
    }
}

public readonly partial record struct Relation(
    string Name,
    byte Unk1,
    uint Unk2,
    byte Unk3)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.Relation;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"relation {Name} ({Unk1:X2}, {Unk2:X8}, {Unk3:X2})");
    }

    public ITypeDescriptor Transform()
    {
        if (Unk1 != 0 || Unk2 != 0 || Unk3 != 0xFD)
            return this;
        if (Name.StartsWith("_T_") && Name.Length > 3)
            return new PropertiesRelation(Name[3..]);
        var match = CollectionPattern().Match(Name);
        if (match.Success)
            return new CollectionRelation(match.Groups[1].Value, match.Groups[2].Value);
        return this;
    }

    [GeneratedRegex(@"^_A_([a-zA-Z]+)_([a-zA-Z]+)$")]
    private static partial Regex CollectionPattern();
}

public readonly record struct PropertiesRelation(string Name)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.PropertiesRelation;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"properties-in {Name}");
    }
}

public readonly record struct CollectionRelation(string Type, string CollectionName)
    : ITypeDescriptor
{
    public string Name => $"{Type}_{CollectionName}";
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.CollectionRelation;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"collection-of {Type} {CollectionName}");
    }
}

public readonly record struct ObjectType(
    string Name,
    byte Unk1,
    uint Unk2,
    byte? Size)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.ObjectType;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"object {Name} {(Size is byte size ? $"{Size}Bytes " : "")}({Unk1:X2}, {Unk2:X8})");
    }
}

public readonly record struct EnumType(
    string Name,
    IReadOnlyList<(string Name, int Value)> Members)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.EnumType;

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"enum {Name} {{");
        using (var indented = writer.Indented)
        {
            foreach (var (name, value) in Members)
                indented.WriteLine($"{name} = {value}");
        }
        writer.WriteLine('}');
    }
}

public enum TypeRefKind : byte
{
    Sint8 = 0x2B, // there surely is the correct value but I do not know it
    Uint8 = 1,
    Sint16 = 2,
    Uint32 = 4,
    StringRef = 9,
    Sint32 = 11,
    Bool = 15,
    Void = 0xFD,
    Named = 0xFF
}
public readonly record struct TypeRef(
    byte Unk1,
    uint Unk2,
    TypeRefKind Kind,
    string? NamedType,
    byte? NamedTypeParam)
{
    public override string ToString()
    {
        string b = Kind switch
        {
            TypeRefKind.Named when NamedTypeParam != 1 => $"{NamedType} ({NamedTypeParam:X2})",
            TypeRefKind.Named => NamedType!,
            _ when Enum.IsDefined(Kind) => Kind.ToString(),
            _ => $"UNKNOWN {(int)Kind}"
        };
        if ((Unk1 == 0 && Unk2 == 0) ||
            (Kind == TypeRefKind.StringRef && Unk1 == 0 && Unk2 == 0x01000000U))
            return b;
        return $"{b} ({Unk1:X2}, {Unk2:X8})";
    }
}

public readonly record struct TypedValue(
    string Name,
    TypeRef Type)
{
    public override string ToString()
    {
        return $"{Name} : {Type}";
    }
}

public readonly record struct FunctionSignature(
    TypeRef ReturnType,
    IReadOnlyList<TypedValue> Parameters)
{
    public override string ToString()
    {
        return $"({string.Join(", ", Parameters)}) -> {ReturnType}";
    }
}

public readonly record struct NamedFunctionSignature(
    TypeDescriptorKind DescriptorKind,
    string Name,
    FunctionSignature Signature)
    : ITypeDescriptor
{
    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"{DescriptorKind.ToString().ToLowerInvariant()} {Name} {Signature}");
    }
}

public readonly record struct Properties(
    string Name,
    IReadOnlyList<TypedValue> Members)
    : ITypeDescriptor
{
    public TypeDescriptorKind DescriptorKind => TypeDescriptorKind.Properties;

    public void WriteTo(CodeWriter writer)
    {
        if (Members.Count == 0)
        {
            writer.WriteLine($"properties {Name}");
            return;
        }
        writer.WriteLine($"properties {Name} {{");
        using (var indented = writer.Indented)
        {
            foreach (var member in Members)
                indented.WriteLine(member.ToString());
        }
        writer.WriteLine('}');
    }
}

public class TypeDescriptorBlock
{
    public uint Timestamp { get; }
    public IReadOnlyList<ITypeDescriptor> Descriptors { get; }

    public DateTime TimestampAsTime => DateTime.UnixEpoch.AddSeconds(Timestamp);
    public Generation Generation => Timestamp switch
    {
        80896606 => Generation.V1,
        6943904 or 1001862970 => Generation.V2,
        1069178032 or 1086694421 => Generation.V3,
        _ => Generation.Unknown
    };

    public TypeDescriptorBlock(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var startPos = stream.Position;
        uint totalSize = reader.ReadUInt32();
        Timestamp = reader.ReadUInt32();
        Descriptors = ReadTaggedList(reader, ReadTypeDescriptor);
        if (stream.Position != startPos + totalSize)
            throw new InvalidDataException("Unexpected end position after TypeDescriptorBlock");
    }

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"// {Timestamp} ({TimestampAsTime.ToString(CultureInfo.InvariantCulture)}) ");
        string previousName = "$";
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.Name != previousName &&
                descriptor.Name[1..] != previousName[1..])
                writer.WriteLine();
            previousName = descriptor.Name;

            descriptor.WriteTo(writer);
        }
    }

    private static ITypeDescriptor ReadTypeDescriptor(byte tag, BinaryReader reader)
    {
        switch ((TypeDescriptorKind)tag)
        {
            case TypeDescriptorKind.ObjectRelations: return ReadObjectRelations(reader);
            case TypeDescriptorKind.ExternalType: return ReadExternalType(reader);
            case TypeDescriptorKind.Relation: return ReadRelation(reader);
            case TypeDescriptorKind.ObjectType: return ReadObjectType(reader);
            case TypeDescriptorKind.EnumType: return ReadEnumType(reader);
            case TypeDescriptorKind.KernelCall: return ReadKernelCall(reader);
            case TypeDescriptorKind.Constructor: return ReadConstructor(reader);
            case TypeDescriptorKind.Method: return ReadMethod(reader);
            case TypeDescriptorKind.Properties: return ReadProperties(reader);
            default: throw new InvalidDataException($"Invalid type descriptor kind: {(int)tag}");
        }
    }

    private static ObjectRelations ReadObjectRelations(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var baseTypes = ReadTaggedList(reader, (tag, _) =>
        {
            if (tag != 1)
                throw new InvalidDataException($"Invalid base type tag: {(int)tag}");
            return reader.ReadVarString();
        });
        var relations = ReadTaggedList(reader, ReadTypeDescriptor);
        return new(name, baseTypes, relations);
    }

    private static ExternalType ReadExternalType(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var unk1 = reader.ReadByte();
        var unk2 = reader.ReadByte();
        var intname = reader.ReadVarString();
        var unk3 = reader.ReadByte();
        var unk4 = reader.ReadUInt32();
        return new(name, intname, unk1, unk2, unk3, unk4);
    }

    private static ITypeDescriptor ReadRelation(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var unk1 = reader.ReadByte();
        var unk2 = reader.ReadUInt32();
        var unk3 = reader.ReadByte();
        return new Relation(name, unk1, unk2, unk3).Transform();
    }

    private static ObjectType ReadObjectType(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var unk1 = reader.ReadByte();
        var unk2 = reader.ReadUInt32();
        byte? size = reader.ReadByte();
        if (size > 127)
        {
            size = null;
            reader.BaseStream.Position--;
        }
        return new(name, unk1, unk2, size);
    }

    private static EnumType ReadEnumType(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var members = ReadTaggedList(reader, (tag, _) =>
        {
            if (tag != 1)
                throw new InvalidDataException($"Invalid enum member tag: {(int)tag}");
            return (reader.ReadVarString(), reader.ReadInt32());
        });
        return new(name, members);
    }

    private static TypeRef ReadTypeRef(BinaryReader reader)
    {
        var unk1 = reader.ReadByte();
        var unk2 = reader.ReadUInt32();
        var kind = (TypeRefKind)reader.ReadByte();
        string? named = null;
        byte? namedParam = null;
        if (kind == TypeRefKind.Named)
        {
            named = reader.ReadVarString();
            namedParam = reader.ReadByte();
        }
        return new(unk1, unk2, kind, named, namedParam);
    }

    private static TypedValue ReadTypedValue(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var type = ReadTypeRef(reader);
        return new(name, type);
    }

    private static List<TypedValue> ReadTypedValueList(BinaryReader reader)
    {
        return ReadTaggedList(reader, (tag, _) =>
        {
            if (tag != 1)
                throw new InvalidDataException($"Invalid typed value list tag: {(int)tag}");
            return ReadTypedValue(reader);
        });
    }

    private static FunctionSignature ReadFunctionSignature(BinaryReader reader)
    {
        var returnType = ReadTypeRef(reader);
        var parameters = ReadTypedValueList(reader);
        return new(returnType, parameters);
    }

    private static NamedFunctionSignature ReadNamedFunctionSignature(BinaryReader reader, TypeDescriptorKind kind)
    {
        var name = reader.ReadVarString();
        var signature = ReadFunctionSignature(reader);
        return new(kind, name, signature);
    }

    private static NamedFunctionSignature ReadKernelCall(BinaryReader reader) =>
        ReadNamedFunctionSignature(reader, TypeDescriptorKind.KernelCall);

    private static NamedFunctionSignature ReadConstructor(BinaryReader reader) =>
        ReadNamedFunctionSignature(reader, TypeDescriptorKind.Constructor);

    private static NamedFunctionSignature ReadMethod(BinaryReader reader) =>
        ReadNamedFunctionSignature(reader, TypeDescriptorKind.Method);

    private static Properties ReadProperties(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        var members = ReadTypedValueList(reader);
        return new(name, members);
    }

    private static List<T> ReadTaggedList<T>(BinaryReader reader, Func<byte, BinaryReader, T> readElement)
    {
        var list = new List<T>();
        while (true)
        {
            var tag = reader.ReadByte();
            if (tag == 0)
                return list;
            list.Add(readElement(tag, reader));
        }
    }

    public IEnumerable<TDescriptor> ByType<TDescriptor>() where TDescriptor : ITypeDescriptor
        => Descriptors.OfType<TDescriptor>();

    public IEnumerable<TDescriptor> AllByName<TDescriptor>(string name) where TDescriptor : ITypeDescriptor
        => Descriptors.Where(d => d is TDescriptor && d.Name == name).Cast<TDescriptor>();

    public IEnumerable<TDescriptor> AllByName<TDescriptor>(TypeDescriptorKind kind, string name) where TDescriptor : ITypeDescriptor
        => Descriptors.Where(d => d is TDescriptor && d.DescriptorKind == kind && d.Name == name).Cast<TDescriptor>();

    public TDescriptor? ByName<TDescriptor>(string name) where TDescriptor : ITypeDescriptor
        => AllByName<TDescriptor>(name).FirstOrDefault();

    private readonly HashSet<(string, string)> baseTypeCache = [];
    public bool IsBaseType(string subType, string baseType)
    {
        if (baseTypeCache.Contains((subType, baseType)))
            return true;
        var source = ByName<ObjectRelations>(subType);
        var target = ByName<ObjectRelations>(baseType);
        if (source.BaseTypes is null || target.BaseTypes is null)
            return false;
        if (source == target)
            return true;
        var visited = new HashSet<string>() { subType };
        var queue = new Queue<string>();
        queue.Enqueue(subType);
        while (queue.TryDequeue(out var currentTypeName))
        {
            var currentType = ByName<ObjectRelations>(currentTypeName);
            if (currentType.BaseTypes is null)
                continue;
            foreach (var curBaseType in currentType.BaseTypes)
            {
                if (curBaseType == baseType)
                {
                    baseTypeCache.Add((subType, baseType));
                    return true;
                }
                else if (visited.Add(curBaseType))
                    queue.Enqueue(curBaseType);
            }
        }
        return false;
    }
}
