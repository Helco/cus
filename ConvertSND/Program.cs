namespace ConverSND;

internal class Program
{
    static void ConvertSND(string inputPath, string outputPath)
    {
        var file = File.ReadAllBytes(inputPath);
        if (file.Length == 0)
            return;
        var format = file.AsSpan(4, BitConverter.ToInt32(file));
        var data = file.AsSpan(8 + format.Length);
        using var output = new BinaryWriter(new FileStream(outputPath, FileMode.Create, FileAccess.Write));
        output.Write("RIFF"u8);
        output.Write(4 + 8 + 8 + format.Length + data.Length);
        output.Write("WAVEfmt "u8);
        output.Write(format.Length);
        output.Write(format);
        output.Write("data"u8);
        output.Write(data.Length);
        output.Write(data);
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var allFiles = Directory.GetFiles(@"C:\dev\cusg", "*.SND", SearchOption.AllDirectories);
        foreach (var fullPath in allFiles)
        {
            try
            {
                var relPath = Path.GetRelativePath(@"C:\dev\cusg", fullPath);
                Console.Write(relPath + "...");

                Directory.CreateDirectory(Path.GetDirectoryName(relPath));
                ConvertSND(fullPath, Path.ChangeExtension(relPath, "wav"));

                Console.WriteLine("done");
            }
            catch (Exception e)
            {
#if DEBUG
                throw;
#else
                Console.WriteLine(e.Message);
#endif
            }
        }
    }
}
