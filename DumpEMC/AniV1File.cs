using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Cus;

// I will have to probably rename more structures to versioned Ani* types for V2...

public readonly record struct SPoint(short X, short Y)
{
    public void WriteTo(CodeWriter writer, bool fixedWidth = false) =>
        writer.Write(ToString(fixedWidth));

    public string ToString(bool fixedWidth) => fixedWidth
        ? $"({X,5:D}, {Y,5:D})"
        : $"({X}, {Y})";

    public override string ToString() => ToString(false);
}

public readonly record struct AniSegment(ushort X, ushort Width, uint DataOffset);

public readonly record struct AniLine(AniSegment[] Segments);

public class AniImage
{
    public readonly SPoint
        DrawOffset,
        Size,
        P2, P4;
    public readonly bool Flag1;
    private readonly byte[] palette;
    private readonly byte[] pixelData;
    private readonly AniLine[] lines;

    public ReadOnlySpan<byte> Palette => palette;
    public ReadOnlySpan<byte> PixelData => pixelData;
    public ReadOnlySpan<AniLine> Lines => lines;
    public bool IsPaletted => !Palette.IsEmpty;
    public int PixelCount => PixelData.Length / (IsPaletted ? 1 : 3);

    public AniImage(BinaryReader reader)
    {
        DrawOffset = reader.ReadSPoint();
        P2 = reader.ReadSPoint();
        Size = reader.ReadSPoint();
        uint pixelCount = reader.ReadUInt32();
        P4 = reader.ReadSPoint();
        Flag1 = reader.ReadBoolean();
        bool isPaletted = reader.ReadBoolean();

        if (Size.Y <= 0)
            lines = [];
        else
        {
            lines = new AniLine[Size.Y];
            for (int i = 0; i < Size.Y; i++)
            {
                ushort segmentCount = reader.ReadUInt16();
                var segments = new AniSegment[segmentCount];
                for (int j = 0; j < segmentCount; j++)
                    segments[j] = new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32());
                lines[i] = new(segments);
            }
        }

        pixelData = reader.ReadBytes(checked((int)(pixelCount * (isPaletted ? 1 : 3))));
        palette = isPaletted
            ? reader.ReadBytes(256 * 3)
            : [];
    }

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"Size: {Size}");
        writer.WriteLine($"  Draw offset: {DrawOffset}");
        writer.WriteLine($"  P2/P4/Flag: {P2}, {P4}, {Flag1}");
        writer.Write("  Format: ");
        if (IsPaletted)
            writer.Write("paletted ");
        var segmentCount = lines.Sum(l => l.Segments.Length);
        writer.WriteLine($"{PixelCount} pixels in {segmentCount} segments");
    }
}

public class AniSprite
{
    public readonly IReadOnlyList<AniImage> Images;
    public readonly byte Order;
    public readonly bool IsDisabled;
    public readonly uint Arg;

    public AniSprite(BinaryReader reader)
    {
        uint imageCount = reader.ReadUInt32();
        Order = reader.ReadByte();
        IsDisabled = reader.ReadBoolean();
        Arg = reader.ReadUInt32();
        var images = new AniImage[checked((int)imageCount)];
        Images = images;
        for (int i = 0; i < images.Length; i++)
            images[i] = new(reader);
    }

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"{(IsDisabled ? "Disabled " : "")}Order:{(int)Order,2} Arg:{Arg:X8}");
        var indented = writer.Indented;
        foreach (var image in Images)
        {
            indented.Write("- ");
            image.WriteTo(indented);
        }
    }
}

[InlineArray(Count)]
public struct AniFrameIndices
{
    private byte firstIndex;
    public const int Count = 8;

    public AniFrameIndices(BinaryReader reader)
    {
        reader.BaseStream.ReadExactly(this);
    }
}

public readonly record struct AniFrame(
    AniFrameIndices ImageIndices,
    SPoint NegOffset,
    SPoint PosOffset,
    ushort Duration)
{
    public AniFrame(BinaryReader reader)
        : this(new(reader), reader.ReadSPoint(), reader.ReadSPoint(), reader.ReadUInt16()) { }

    public void WriteTo(CodeWriter writer)
    {
        writer.Write($"{Duration,4:D}ms Neg:");
        NegOffset.WriteTo(writer, true);
        writer.Write(" Pos:");
        PosOffset.WriteTo(writer, true);
        writer.Write(" [");
        for (int i = 0; i < AniFrameIndices.Count; i++)
        {
            if (i > 0)
                writer.Write(", ");
            writer.Write($"{ImageIndices[i],2:D}");
        }
        writer.WriteLine("]");
    }
}

public class AniV1File
{
    public readonly string Name;
    public readonly SPoint TotalSize;
    public readonly uint Arg1, TotalDuration, Arg3;
    public readonly byte Alpha;
    public readonly IReadOnlyList<AniSprite> Sprites;
    public readonly IReadOnlyList<AniFrame> Frames;

    public AniV1File(Stream stream, bool leaveOpen = true)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
        var magic = reader.ReadBytes(4);
        if (magic[0] != 'A' || magic[1] != 'N' || magic[2] != 'I' || magic[3] != 0)
            throw new InvalidDataException("Invalid ANI magic");

        Name = reader.ReadVarString();
        var spriteCount = reader.ReadUInt32();
        var frameCount = reader.ReadUInt32();
        TotalSize = reader.ReadSPoint();
        Arg1 = reader.ReadUInt32();
        TotalDuration = reader.ReadUInt32();
        Arg3 = reader.ReadUInt32();
        Alpha = reader.ReadByte();
        reader.BaseStream.Position += 8;

        var sprites = new AniSprite[checked((int)spriteCount)];
        Sprites = sprites;
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = new(reader);

        var frames = new AniFrame[checked((int)frameCount)];
        Frames = frames;
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new(reader);
    }

    public void WriteTo(CodeWriter writer)
    {
        writer.WriteLine($"Name: {Name}");
        writer.WriteLine($"Size: {TotalSize}");
        writer.WriteLine($"Total Duration: {TotalDuration}");
        writer.WriteLine($"Alpha: {(int)Alpha}");
        writer.WriteLine($"Arg1: {Arg1}");
        writer.WriteLine($"Arg3: {Arg3}");

        if (Sprites.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"Sprites: {Sprites.Count}");
            using var indented = writer.Indented;
            foreach (var sprite in Sprites)
            {
                indented.Write("- ");
                sprite.WriteTo(indented);
            }
        }

        if (Frames.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"Frames: {Frames.Count}");
            using var indented = writer.Indented;
            foreach (var frame in Frames)
            {
                indented.Write("- ");
                frame.WriteTo(indented);
            }
        }
    }
}
