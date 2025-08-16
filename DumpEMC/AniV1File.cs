using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AnimatedGif;

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

    public static SPoint operator +(SPoint a, SPoint b) =>
        checked(new((short)(a.X + b.X), (short)(a.Y + b.Y)));

    public static SPoint operator -(SPoint a, SPoint b) =>
        checked(new((short)(a.X - b.X), (short)(a.Y - b.Y)));

    public static SPoint Min(SPoint a, SPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

    public static SPoint Max(SPoint a, SPoint b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
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
                {
                    segments[j] = new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32());
                }
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

    public byte[] ConvertToRGBA(byte alpha)
    {
        byte[] rgba = new byte[Size.X * Size.Y * 4];
        DrawInto(rgba, Size.X * 4, new(0, 0), alpha);
        return rgba;
    }

    public void DrawInto(byte[] rgba, int pitch, SPoint offset, byte alpha, bool isArgb = false)
    {
        int fullHeight = rgba.Length / pitch;
        int fullWidth = pitch / 4;
        int srcY = 0;
        if (offset.Y < 0)
        {
            srcY = -offset.Y;
            offset = offset with { Y = 0 };
        }
        int srcH = Math.Min(Size.Y, fullHeight - offset.Y);

        for (; srcY < srcH; srcY++)
        {
            int offX = offset.X;
            int srcX = 0;
            if (offX < 0)
            {
                srcX = -offX;
                offX = 0;
            }
            int srcW = Math.Min(Size.X, fullWidth - offX);
            if (srcW <= 0)
                continue;

            Span<byte> outLine = rgba.AsSpan(
                (offset.Y + srcY) * pitch + offX * 4,
                (fullWidth - offX) * 4);
            foreach (var segment in lines[srcY].Segments)
            {
                srcX += segment.X;
                for (int i = 0; i < segment.Width; i++)
                {
                    if (srcX >= srcW)
                        goto nextLine;
                    outLine[srcX * 4 + 3] = alpha;
                    var ri = isArgb ? 2 : 0;
                    var bi = isArgb ? 0 : 2;
                    if (IsPaletted)
                    {
                        var color = pixelData[segment.DataOffset + i];
                        outLine[srcX * 4 + ri] = palette[color * 3 + 2];
                        outLine[srcX * 4 + 1] = palette[color * 3 + 1];
                        outLine[srcX * 4 + bi] = palette[color * 3 + 0];
                    }
                    else
                    {
                        var colorI = segment.DataOffset + i * 3;
                        outLine[srcX * 4 + ri] = pixelData[colorI + 2];
                        outLine[srcX * 4 + 1] = pixelData[colorI + 1];
                        outLine[srcX * 4 + bi] = pixelData[colorI + 0];
                    }
                    srcX++;
                }
            }
nextLine:;
        }
    }
    
    public void ConvertToFile(string targetPath, byte alpha)
    {
        var rgba = ConvertToRGBA(alpha);
        var writer = new StbImageWriteSharp.ImageWriter();
        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
        writer.WritePng(rgba, Size.X, Size.Y, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fileStream);
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
    private readonly int[] spriteOrder;

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

        if (spriteCount > AniFrameIndices.Count)
            throw new InvalidDataException($"Sprite count is greater than maximum {AniFrameIndices.Count}");

        var sprites = new AniSprite[checked((int)spriteCount)];
        Sprites = sprites;
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = new(reader);

        var frames = new AniFrame[checked((int)frameCount)];
        Frames = frames;
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new(reader);

        spriteOrder = [.. Enumerable
            .Range(0, sprites.Length)
            .Reverse()
            .OrderByDescending(i => sprites[i].Order)];
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

    public void ConvertFrameToFile(int frameI, string targetPath)
    {
        var rgba = RenderFrame(frameI);
        var writer = new StbImageWriteSharp.ImageWriter();
        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
        writer.WritePng(rgba, TotalSize.X, TotalSize.Y, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fileStream);
    }

    public byte[] RenderFrame(int frameI)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameI);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameI, Frames.Count);
        var frameSize = GetFrameSize(frameI);
        byte[] rgba = new byte[frameSize.X * frameSize.Y * 4];
        RenderFrameInto(frameI, rgba, frameSize);
        return rgba;        
    }

    private (SPoint min, SPoint max) GetFrameBounds(int frameI)
    {
        var frame = Frames[frameI];
        SPoint frameMin = new(9999, 9999), frameMax = new(-9999, -9999);
        foreach (var (spriteI, sprite) in Sprites.Indexed())
        {
            var imageI = frame.ImageIndices[spriteI];
            if (imageI <= 0)
                continue;
            var image = sprite.Images[imageI - 1];
            frameMin = SPoint.Min(frameMin, image.DrawOffset);
            frameMax = SPoint.Max(frameMax, image.DrawOffset + image.Size);
        }
        return (frameMin, frameMax);
    }

    private SPoint GetFrameSize(int frameI)
    {
        var (frameMin, frameMax) = GetFrameBounds(frameI);
        return frameMax - frameMin + new SPoint(1, 1);
    }

    private void RenderFrameInto(int frameI, byte[] rgba, SPoint outSize, bool isArgb = false)
    {
        var (frameMin, frameMax) = GetFrameBounds(frameI);
        if (frameMin.X > frameMax.X || frameMin.Y > frameMax.Y)
            return;

        var frame = Frames[frameI];
        Array.Fill<byte>(rgba, 0);
        for (int i = 0; i < Sprites.Count; i++)
        {
            var spriteI = spriteOrder[i];
            var sprite = Sprites[spriteI];
            var imageI = frame.ImageIndices[spriteI];
            if (imageI <= 0)
                continue;
            var image = sprite.Images[imageI - 1];
            var offset = image.DrawOffset - frameMin;
            image.DrawInto(rgba, outSize.X * 4, offset, Alpha, isArgb);
        }
    }

    public unsafe void ConvertToFile(string targetPath)
    {
        using var gif = AnimatedGif.AnimatedGif.Create(targetPath, (int)(TotalDuration / Frames.Count));
        var maxFrameSize = Enumerable
            .Range(0, Frames.Count)
            .Select(GetFrameSize)
            .Aggregate(SPoint.Max);
        var argb = new byte[maxFrameSize.X * maxFrameSize.Y * 4];
        fixed (byte* argbPtr = argb)
        {
            using var image = new Bitmap(
                maxFrameSize.X, maxFrameSize.Y, maxFrameSize.X * 4,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                (IntPtr)argbPtr);
            foreach (var (frameI, frame) in Frames.Indexed())
            {
                RenderFrameInto(frameI, argb, maxFrameSize, isArgb: true);
                gif.AddFrame(image, delay: frame.Duration, quality: GifQuality.Bit8);
            }
        }
    }
}
