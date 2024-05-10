namespace ExtractAn0;

internal class Program
{
    static ReadOnlySpan<byte> EndMarker => "TRUEVISION-XFILE.\0"u8;

    static void ExtractTGAs(string fullPath, string outDir) 
    {
        var allBytes = File.ReadAllBytes(fullPath);
        var bytes = allBytes.AsSpan(8);
        var imageCount = BitConverter.ToInt32(allBytes, 4);
        for (int i = 0; i < imageCount; i++)
        {
            int end = bytes.IndexOf(EndMarker) + EndMarker.Length;
            File.WriteAllBytes(Path.Join(outDir, $"image{i}.tga"), bytes[..end].ToArray());
            bytes = bytes[end..];
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var allFiles = Directory.GetFiles(@"C:\dev\cusg", "*.AN0", SearchOption.AllDirectories);
        foreach (var fullPath in allFiles)
        {
            try
            {
                var relPath = Path.GetRelativePath(@"C:\dev\cusg", fullPath);
                Console.Write(relPath + "...");

                var directory = Path.GetDirectoryName(relPath);
                Directory.CreateDirectory(relPath);
                ExtractTGAs(fullPath, relPath);

                Console.WriteLine("done");
            }
            catch(Exception e)
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
