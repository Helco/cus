using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cus;

public interface ICodeWritable
{
    void WriteTo(CodeWriter writer);
}

public readonly record struct EmcEmbeddedFile(
    string Name,
    long Offset,
    uint Size);

public record class EmcCollection(
    string Name,
    string BaseType,
    IReadOnlyList<EmcObject> Elements)
    : IReadOnlyList<EmcObject>, ICodeWritable
{
    public EmcObject this[int index] => Elements[index];
    public int Count => Elements.Count;
    public IEnumerator<EmcObject> GetEnumerator() => Elements.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Elements).GetEnumerator();

    public void WriteTo(CodeWriter writer)
    {
        if (Elements.Count == 0)
        {
            writer.WriteLine($"{Name} ({BaseType}) []");
            return;
        }
        writer.WriteLine($"{Name} ({BaseType}) [");
        using (var indented = writer.Indented)
        {
            indented.Write("- ");
            Elements.First().WriteTo(indented);
            foreach (var element in Elements.Skip(1))
            {
                indented.WriteLine();
                indented.Write("- ");
                element.WriteTo(indented);
            }
        }
        writer.WriteLine(']');
    }
}

public readonly record struct EmcProperty(
    string Name,
    TypeRef Type,
    object Value)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        writer.Write($"{Name} : {Type} = ");
        if (Value is ICodeWritable writable)
            writable.WriteTo(writer);
        else if (Value is string str)
            writer.Write($"\"{str}\"");
        else
            writer.Write(Value);
        writer.WriteLine();
    }
}

public readonly record struct EmcStruct(
    string TypeName,
    IReadOnlyList<EmcProperty> Properties)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        if (Properties.Count == 0)
        {
            writer.Write("{}");
            return;
        }
        writer.WriteLine("{");
        using (var indented = writer.Indented)
        {
            foreach (var prop in Properties)
                prop.WriteTo(indented);
        }
        writer.Write('}');
    }
}

public readonly record struct EmcStructArray(
    IReadOnlyList<EmcStruct> Elements)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        WriteArrayTo(writer, Elements.Select(e => e as ICodeWritable));
    }

    public static void WriteArrayTo(CodeWriter writer, IEnumerable<ICodeWritable> elements)
    {
        if (!elements.Any())
        {
            writer.Write("[]");
            return;
        }
        writer.WriteLine("[");
        using (var indented = writer.Indented)
        {
            int i = 0;
            foreach (var element in elements)
            {
                indented.Write($"{i++}: ");
                element.WriteTo(indented);
                indented.WriteLine();
            }
        }
        writer.Write(']');
    }
}

public readonly record struct EmcEnumeration(
    int Value,
    string TypeName,
    string ValueName)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        writer.Write($"\"{ValueName}\" ({Value})");
    }
}

public readonly record struct EmcPoint(int X, int Y) : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        writer.Write(this);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public readonly record struct EmcPolygon(IReadOnlyList<EmcPoint> Points) : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        EmcStructArray.WriteArrayTo(writer, Points.Select(p => p as ICodeWritable));
    }
}

public readonly record struct EmcShape(IReadOnlyList<EmcPolygon> Polygons) : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        EmcStructArray.WriteArrayTo(writer, Polygons.Select(p => p as ICodeWritable));
    }
}

public readonly record struct EmcPathFindingShape(
    IReadOnlyList<EmcPolygon> Polygons,
    IReadOnlyList<byte> PointDepths,
    IReadOnlyList<sbyte> PolygonOrders)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        if (Polygons.Count == 0)
        {
            writer.Write("[]");
            return;
        }
        writer.WriteLine('[');
        int j = 0;
        using (var polygonWriter = writer.Indented)
        {
            for (int i = 0; i < Polygons.Count; i++)
            {
                polygonWriter.Write($"{i}: order={PolygonOrders[i]} [");
                if (Polygons[i].Points.Count == 0)
                {
                    polygonWriter.WriteLine("]");
                    continue;
                }
                polygonWriter.WriteLine();
                using (var pointWriter = polygonWriter.Indented)
                {
                    for (int k = 0; k < Polygons[i].Points.Count; k++)
                    {
                        pointWriter.WriteLine($"{k}: {Polygons[i].Points[k]} depth={PointDepths[j++]}");
                    }
                }
                polygonWriter.WriteLine("]");
            }
        }
        writer.Write(']');
    }
}

public readonly record struct EmcColoredShape(
    IReadOnlyList<EmcPolygon> Polygons,
    IReadOnlyList<byte> PointBrightnesses,
    IReadOnlyList<(byte r, byte g, byte b, byte a)> PointColors,
    IReadOnlyList<byte> PolygonUnknowns)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        if (Polygons.Count == 0)
        {
            writer.Write("[]");
            return;
        }
        writer.WriteLine('[');
        int j = 0;
        using (var polygonWriter = writer.Indented)
        {
            for (int i = 0; i < Polygons.Count; i++)
            {
                polygonWriter.Write($"{i}: unk={PolygonUnknowns[i]} [");
                if (Polygons[i].Points.Count == 0)
                {
                    polygonWriter.WriteLine("]");
                    continue;
                }
                polygonWriter.WriteLine();
                using (var pointWriter = polygonWriter.Indented)
                {
                    for (int k = 0; k < Polygons[i].Points.Count; k++)
                    {
                        pointWriter.Write($"{k}: {Polygons[i].Points[k]} brightness={PointBrightnesses[j]}");
                        var (r, g, b, a) = PointColors[j++];
                        pointWriter.WriteLine($" color={r},{g},{b},{a}");
                    }
                }
                polygonWriter.WriteLine("]");
            }
        }
        writer.Write(']');
    }
}

public record class EmcObject(
    string TypeName,
    string Name,
    IReadOnlyList<EmcProperty> Properties,
    IReadOnlyList<EmcCollection> Collections)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        writer.Write($"{TypeName} {Name} {{");
        if (Properties.Count > 0 || Collections.Count > 0)
        {
            using var indented = writer.Indented;
            writer.WriteLine();
            foreach (var prop in Properties)
                prop.WriteTo(indented);
            if (Properties.Any() && Collections.Any())
                indented.WriteLine();
            foreach (var coll in Collections)
                coll.WriteTo(indented);
        }
        writer.WriteLine("}");
    }
}

public record class EmcArchive(
    string Name,
    uint TotalSize,
    byte Unk1,
    byte Unk2,
    uint FileCount)
    : ICodeWritable
{
    public void WriteTo(CodeWriter writer)
    {
        writer.Write($"\"{Name}\" {FileCount} files in {TotalSize / 1024 / 1024}MiB ({Unk1:X2}, {Unk2:X2})");
    }
}

public class EmcFile
{
    public const int RegularPointCount = 4;
    private readonly List<EmcEmbeddedFile> embeddedFiles = [];

    public TypeDescriptorBlock Types { get; }
    public EmcObject Root { get; }
    public IReadOnlyList<EmcEmbeddedFile> EmbeddedFiles => embeddedFiles;

    public EmcFile(Stream stream)
    {
        Types = new(stream);
        if (Types.Generation is not (Generation.V3 or Generation.V2 or Generation.V1))
            throw new NotSupportedException($"Unsupported EMC timestamp: {Types.Generation} ({Types.Timestamp} - {Types.TimestampAsTime})");

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Root = ReadObject(reader);
    }

    private EmcObject ReadObject(BinaryReader reader)
    {
        var typeName = reader.ReadVarString();
        var type = GetObjectType(typeName);
        return ReadObject(reader, type);
    }

    private EmcObject ReadObject(BinaryReader reader, in ObjectRelations relations)
    {
        string name = reader.ReadVarString();
        List<EmcProperty> properties = [];
        List<EmcCollection> collections = [];
        ReadObjectProperties(reader, relations, properties);
        ReadObjectCollections(reader, relations, collections);
        return new(relations.Name, name, properties, collections);
    }

    private void ReadObjectCollections(BinaryReader reader, in ObjectRelations relations, List<EmcCollection> collections)
    {
        foreach (var baseType in relations.BaseTypes)
            ReadObjectCollections(reader, GetObjectType(baseType), collections);
        foreach (var relation in relations.Relations.OfType<CollectionRelation>())
            collections.Add(ReadObjectCollection(reader, relation));
    }

    private EmcCollection ReadObjectCollection(BinaryReader reader, CollectionRelation collectionRelation)
    {
        List<EmcObject> objects = new List<EmcObject>();
        uint endOfObject = reader.ReadUInt32();
        while (endOfObject > 0)
        {
            objects.Add(ReadObject(reader));
            if (reader.BaseStream.Position != endOfObject)
                throw new InvalidDataException("End of object read did not align with reported offset");
            if (!Types.IsBaseType(objects.Last().TypeName, collectionRelation.Type))
                throw new InvalidDataException($"Collection {collectionRelation.CollectionName} contained a {objects.Last().TypeName} which is not a {collectionRelation.Type}");
            endOfObject = reader.ReadUInt32();
        }
        return new(collectionRelation.CollectionName, collectionRelation.Type, objects);
    }

    private void ReadObjectProperties(BinaryReader reader, in ObjectRelations relations, List<EmcProperty> properties)
    {
        foreach (var baseType in relations.BaseTypes)
            ReadObjectProperties(reader, GetObjectType(baseType), properties);
        foreach (var relation in relations.Relations.OfType<PropertiesRelation>())
        {
            var structProperties = Types.ByName<Properties>(relation.Name);
            if (structProperties.Members is null)
                throw new KeyNotFoundException($"Could not find referenced properties of {relation.Name}");
            ReadProperties(reader, structProperties, properties);
        }
    }

    private void ReadProperties(BinaryReader reader, in Properties structProperties, List<EmcProperty> properties)
    {
        foreach (var member in structProperties.Members)
        {
            var value = ReadValue(reader, member.Type);
            properties.Add(new(member.Name, member.Type, value));
        }
    }

    private object ReadValue(BinaryReader reader, in TypeRef type) => type.Kind switch
    {
        TypeRefKind.Uint8 => reader.ReadByte(),
        TypeRefKind.Sint16 => reader.ReadInt16(),
        TypeRefKind.Sint32 => reader.ReadInt32(),
        TypeRefKind.Uint32 => reader.ReadUInt32(),
        TypeRefKind.StringRef => reader.ReadUInt32(),
        TypeRefKind.Bool => reader.ReadByte() != 0,
        TypeRefKind.Named => ReadNamedTypeValue(reader, type.NamedType!, type.NamedTypeParam ?? 0),
        _ => throw new NotSupportedException($"Unsupported value type: {type.Kind}"),
    };

    private object ReadNamedTypeValue(BinaryReader reader, string typeName, byte param)
    {
        var externalType = Types.ByName<ExternalType>(typeName);
        if (externalType.Name is not null)
            return ReadExternalValue(reader, externalType, param);
        var enumType = Types.ByName<EnumType>(typeName);
        if (enumType.Name is not null)
            return ReadEnumValue(reader, enumType, param);
        var structType = Types.ByName<Properties>(typeName);
        if (structType.Name is not null)
            return ReadStructValue(reader, structType, param);
        throw new NotSupportedException($"Unsupported named value type: {typeName} ({(int)param})");
    }

    private EmcEnumeration ReadEnumValue(BinaryReader reader, in EnumType enumType, byte size)
    {
        if (size != 4)
            throw new NotSupportedException($"Unsupported enumeration size: {size}");
        int value = reader.ReadInt32();
        var (valueName, _) = enumType.Members.FirstOrDefault(m => m.Value == value);
        return new(value, enumType.Name, valueName ?? $"Unknown({value})");
    }

    private object ReadStructValue(BinaryReader reader, Properties properties, byte count)
    {
        if (count == 0)
            throw new NotSupportedException($"Unsupported struct array size: {count}");
        else if (count == 1)
            return ReadStructValue(reader, properties);
        else
            return new EmcStructArray([.. Enumerable.Repeat(0, count).Select(_ => ReadStructValue(reader, properties))]);
    }

    private EmcStruct ReadStructValue(BinaryReader reader, in Properties properties)
    {
        List<EmcProperty> members = [];
        ReadProperties(reader, properties, members);
        return new(properties.Name, members);
    }

    private object ReadExternalValue(BinaryReader reader, in ExternalType type, byte _)
    {
        switch (type.InternalName)
        {
            case "CArchivo" when Types.Generation is Generation.V1: return ReadArchiveV1(reader);
            case "CAnimacion" when Types.Generation is Generation.V1: return ReadAnimacionV1(reader);
            case "CArchivo": return reader.ReadVarString();
            case "CAnimacion": return reader.ReadVarString();
            case "CString": return reader.ReadVarString();
            case "CPunto": return ReadShape(reader).Polygons[0].Points[0];
            case "CRectangulo": return ReadShape(reader).Polygons[0];
            case "CRectangulos": return ReadShape(reader);
            case "CSuelos": return ReadPathFindingShape(reader);
            case "CSuelosConColor": return ReadColoredShape(reader);
            case "CGrafico": return ReadGrafico(reader);
            default: throw new NotSupportedException($"Unsupported external type: {type.InternalName}");
        }
    }

    private EmcPoint ReadPoint(BinaryReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32());

    private EmcPolygon ReadPolygon(BinaryReader reader, int pointCount) =>
        new([.. Enumerable.Repeat(reader, pointCount).Select(ReadPoint)]);

    private EmcShape ReadShape(BinaryReader reader)
    {
        var pointsPerPolygon = reader.ReadByte() switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            3 => 0,
            var c => throw new InvalidDataException($"Invalid shape complexity: {(int)c}")
        };

        var polygonCount = reader.ReadUInt16();
        var polygons = new List<EmcPolygon>(polygonCount);
        for (int i = 0; i < polygons.Capacity; i++)
        {
            var pointCount = pointsPerPolygon == 0 ? reader.ReadByte() : pointsPerPolygon;
            polygons.Add(ReadPolygon(reader, pointCount));
        }
        return new(polygons);
    }

    private EmcPathFindingShape ReadPathFindingShape(BinaryReader reader)
    {
        var polygonCount = reader.ReadUInt16();
        var polygons = new List<EmcPolygon>(polygonCount);
        var polygonOrders = new List<sbyte>(polygonCount);
        var pointDepths = new List<byte>(polygonCount * RegularPointCount);
        for (int i = 0; i < polygonCount; i++)
        {
            polygons.Add(ReadPolygon(reader, RegularPointCount));
            polygonOrders.Add(reader.ReadSByte());
            for (int j = 0; j < RegularPointCount; j++)
                pointDepths.Add(reader.ReadByte());
        }
        return new(polygons, pointDepths, polygonOrders);
    }

    private EmcColoredShape ReadColoredShape(BinaryReader reader)
    {
        var polygonCount = reader.ReadUInt16();
        var polygons = new List<EmcPolygon>(polygonCount);
        var brightnesses = new List<byte>(polygonCount * RegularPointCount);
        var colors = new List<(byte, byte, byte, byte)>(polygonCount * RegularPointCount);
        var unknowns = new List<byte>(polygonCount);
        for (int i = 0; i < polygonCount; i++)
        {
            polygons.Add(ReadPolygon(reader, RegularPointCount));
            for (int j = 0; j < RegularPointCount; j++)
                brightnesses.Add(reader.ReadByte());
            for (int j = 0; j < RegularPointCount; j++)
                colors.Add((reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
            unknowns.Add(reader.ReadByte());
        }
        return new(polygons, brightnesses, colors, unknowns);
    }

    private EmcStruct ReadGrafico(BinaryReader reader)
    {
        short x = reader.ReadInt16();
        short y = reader.ReadInt16();
        short z = reader.ReadInt16();
        sbyte order = reader.ReadSByte();
        var animacion = reader.ReadVarString();
        return new EmcStruct("CGrafico", [
            new("x", new(0, 0, TypeRefKind.Sint16, null, null), x),
            new("y", new(0, 0, TypeRefKind.Sint16, null, null), y),
            new("z", new(0, 0, TypeRefKind.Sint16, null, null), z),
            new("order", new(0, 0, TypeRefKind.Sint8, null, null), order),
            new("animacion", new(0, 0, TypeRefKind.Named, "CAnimacion", 1), animacion)
        ]);
    }

    private EmcArchive ReadArchiveV1(BinaryReader reader)
    {
        var name = reader.ReadVarString();
        uint totalSize = reader.ReadUInt32();
        long endPosition = reader.BaseStream.Position + totalSize;
        var unk1 = reader.ReadByte();
        var unk2 = reader.ReadByte();
        
        uint fileCount = reader.ReadUInt32();

        embeddedFiles.EnsureCapacity(embeddedFiles.Count + (int)fileCount);
        for (uint i = 0; i < fileCount; i++)
        {
            var fileName = reader.ReadVarString() + "." + reader.ReadVarString();
            uint fileSize = reader.ReadUInt32();
            embeddedFiles.Add(new(fileName, reader.BaseStream.Position, fileSize));
            reader.BaseStream.Position += fileSize;
        }
        if (reader.BaseStream.Position > endPosition)
            throw new InvalidDataException("Read more archive files than expected");
        reader.BaseStream.Position = endPosition;

        return new(name, totalSize, unk1, unk2, fileCount);
    }

    private string ReadAnimacionV1(BinaryReader reader)
    {
        var name = reader.ReadString();
        var size = reader.ReadUInt32();
        embeddedFiles.Add(new(name, reader.BaseStream.Position, size));
        reader.BaseStream.Position += size;
        return name;
    }

    private ObjectRelations GetObjectType(string name)
    {
        var result = Types.ByName<ObjectRelations>(name);
        if (result.Relations is null)
            throw new KeyNotFoundException($"Could not find referenced object relations {name}");
        return result;
    }
}
