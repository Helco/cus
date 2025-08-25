
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cus;

public readonly partial record struct DialogLine(string Speaker, string Id, string Text)
{
    public static DialogLine Parse(string full)
    {
        var m = MyRegex().Match(full);
        if (!m.Success)
            throw new ArgumentException($"Invalid line: {full}", nameof(full));
        return new(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
    }

    [GeneratedRegex(@"^\s*(\w+)[\s,]+(\d+)[\s,]*""(.+)""")]
    private static partial Regex MyRegex();

    public override string ToString() => $"{Speaker}, {Id}, \"{Text}\"";
}

public readonly partial record struct ObjectName(string Internal, string External, bool UseAltForm)
{
    public static ObjectName Parse(string full)
    {
        var m = MyRegex().Match(full);
        if (m.Success)
            return new(m.Groups[1].Value, m.Groups[2].Value, false);
        m = MyRegex1().Match(full);
        if (m.Success)
            return new(m.Groups[1].Value, m.Groups[2].Value, true);
        throw new ArgumentException($"Invalid line: {full}", nameof(full));
    }

    [GeneratedRegex(@"""(.+?)""\s+""(.*)""")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"^\s*([^#]+)\s*#\s*([^\r]+)")]
    private static partial Regex MyRegex1();

    public override string ToString() => UseAltForm
        ? $"{Internal}#{External}"
        : $"\"{Internal}\" \"{External}\"";
}

internal interface ITextProvider
{
    DialogLine[] ReadDialogLines();
    ObjectName[] ReadObjectNames();
    void WriteDialogLines(string lang, DialogLine[] lines);
    void WriteObjectNames(string lang, ObjectName[] names);
}

public abstract class DirectTextOutput(string basePath) : ITextProvider
{
    public string BasePath => basePath;
    public abstract DialogLine[] ReadDialogLines();
    public abstract ObjectName[] ReadObjectNames();
    public void WriteDialogLines(string lang, DialogLine[] lines)
    {
        var dirName = BasePath.EndsWith('/') || BasePath.EndsWith('\\') ? BasePath[..^1] : BasePath;
        dirName = Path.GetFileName(dirName);
        Directory.CreateDirectory(dirName);
        File.WriteAllLines(Path.Combine(dirName, $"TEXTOS.{lang}.TXT"),
            [.. lines.Select(l => l.ToString())],
            Program.Encoding);
    }

    public void WriteObjectNames(string lang, ObjectName[] names)
    {
        var dirName = BasePath.EndsWith('/') || BasePath.EndsWith('\\') ? BasePath[..^1] : BasePath;
        dirName = Path.GetFileName(dirName);
        Directory.CreateDirectory(dirName);
        File.WriteAllLines(Path.Combine(BasePath, $"OBJETOS.{lang}.TXT"),
            [.. names.Select(l => l.ToString())],
            Program.Encoding);
    }
}

public sealed class RawTextProvider(string basePath) : DirectTextOutput(basePath)
{
    public override DialogLine[] ReadDialogLines()
    {
        return [.. File
            .ReadAllLines(Path.Combine(BasePath, "TEXTOS.TXT"), Program.Encoding)
            .Select(DialogLine.Parse)];
    }

    public override ObjectName[] ReadObjectNames()
    {
        return [.. File
            .ReadAllLines(Path.Combine(BasePath, "OBJETOS.TXT"), Program.Encoding)
            .Select(ObjectName.Parse)];
    }
}

public sealed class HiddenTextProvider(string basePath, string targetFolder, byte key = 0xA3) : DirectTextOutput(targetFolder)
{
    public override DialogLine[] ReadDialogLines()
    {
        var lines = Program.Encoding.GetString([.. File
            .ReadAllBytes(Path.Combine(basePath, "Fondos", "museo_f.ani"))
            .Select(b => (byte)(b ^ key))])
            .Split("\n")
            .Where(l => l.Trim().Length > 0);
        return [.. lines.Select(DialogLine.Parse)];
    }

    public override ObjectName[] ReadObjectNames()
    {
        var lines = Program.Encoding.GetString([.. File
            .ReadAllBytes(Path.Combine(basePath, "Fondos", "museo_o.ani"))
            .Select(b => (byte)(b ^ key))])
            .Split("\n")
            .Where(l => l.Trim().Length > 0);
        return [.. lines.Select(ObjectName.Parse)];
    }
}

internal static class Program
{
    private const string Endpoint = "http://localhost:5000/translate"; // LibreTranslate
    //internal static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(28591) ??
    internal static readonly Encoding Encoding = Encoding.GetEncoding(28591) ??
        throw new InvalidDataException("Could not get encoding");
    private static readonly ConcurrentQueue<HttpClient> httpClients = new();

    public static async Task Main(string[] args)
    {
        await Task.WhenAll(
            //TranslateAll(new RawTextProvider(@"C:\dev\cus\DumpEMC\bin\Debug\net8.0\vaqueros\GLOBAL\")),
            TranslateAll(new HiddenTextProvider(@"E:\SteamLibrary\steamapps\common\SextaSecta\Spanish\", "SextaSecta")),
            TranslateAll(new HiddenTextProvider(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón La banda de Corvino", "Corvino", 0x60)),
            TranslateAll(new HiddenTextProvider(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón El escarabajo de Cleopatra\Spanish", "Escarabajo")),
            TranslateAll(new HiddenTextProvider(@"E:\SteamLibrary\steamapps\common\Balones\Archivos", "Balones", 0x60)),
            TranslateAll(new HiddenTextProvider(@"E:\SteamLibrary\steamapps\common\Mamelucos\Archivos", "Mamelucos", 0x60))
        );
    }

    public static async Task TranslateAll(ITextProvider provider)
    {
        provider.WriteDialogLines("ES", provider.ReadDialogLines());
        provider.WriteObjectNames("ES", provider.ReadObjectNames());

        var newNames = await provider
            .ReadObjectNames()
            .ToAsyncEnumerable()
            .SelectAwait(async (line) => line.External == "" ? line
                : new ObjectName(line.Internal, await Translate(line.External), line.UseAltForm))
            .ToArrayAsync();
        provider.WriteObjectNames("EN", newNames);

        var newLines = await provider
            .ReadDialogLines()
            .ToAsyncEnumerable()
            .SelectAwait(async (line) => new DialogLine(line.Speaker, line.Id, await Translate(line.Text)))
            .ToArrayAsync();
        provider.WriteDialogLines("EN", newLines);
    }

    public static async Task<string> Translate(string spanish)
    {
        if (!httpClients.TryDequeue(out var client))
            client = new();

        TranslateQuery query = new()
        {
            q = spanish,
            source = "es",
            target = "en"
        };
        var result = await client.PostAsJsonAsync(Endpoint, query);
        if (result.StatusCode != System.Net.HttpStatusCode.OK)
            return spanish;
        var response = await result.Content.ReadFromJsonAsync<TranslateResponse>();
        return response.translatedText ?? spanish;
    }
}

struct TranslateQuery
{
    public string? q { get; set; }
    public string? source { get; set; }
    public string? target { get; set; }
}

struct TranslateResponse
{
    public string translatedText { get; set; }
}

