using System.Buffers;

namespace Cus;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        DumpAll(@"C:\dev\cusg\Mapas\", "aventura-german");
        DumpAll(@"C:\dev\cusg-aventura-demo\DATA01\Mapas\", "aventura-german-demo");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\English\Mapas\", "aventura-english");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\Spanish\Mapas\", "aventura-spanish");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Operación Moscú\English\Mapas\", "moscu");
        DumpAll(@"E:\SteamLibrary\steamapps\common\SextaSecta\Spanish\Mapas\", "secta");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón La banda de Corvino\Mapas\", "corvino");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón El escarabajo de Cleopatra\Spanish\Mapas\", "escarabajo");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Balones\Archivos\Mapas\", "balones");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Mamelucos\Archivos\Mapas\", "mamelucos");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Vaqueros\", "vaqueros");
        DumpAll(@"E:\SteamLibrary\steamapps\common\Terror\", "terror");
    }

    private static void DumpAll(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            if (!file.EndsWith(".EMC", StringComparison.InvariantCultureIgnoreCase))
                continue;
            Console.WriteLine(file);
            var targetBase = Path.Combine(target, Path.GetFileNameWithoutExtension(file));
            Directory.CreateDirectory(targetBase);
            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
            EmcFile emc = new EmcFile(fileStream);
            using (var writer = new CodeWriter(new StreamWriter(targetBase + ".types.txt")))
                emc.Types.WriteTo(writer);
            using (var writer = new CodeWriter(new StreamWriter(targetBase + ".emc.txt")))
                emc.Root.WriteTo(writer);

            foreach (var (fileName, offset, size) in emc.EmbeddedFiles)
            {
                var bytes = ArrayPool<byte>.Shared.Rent((int)size);
                fileStream.Position = offset;
                fileStream.ReadExactly(bytes, 0, (int)size);
                var targetFileName = fileName;
                if (targetFileName == "")
                    targetFileName = $"unnamed-{offset}";
                using var targetStream = new FileStream(Path.Combine(targetBase, Path.GetFileName(targetFileName)), FileMode.Create, FileAccess.Write);
                targetStream.Write(bytes, 0, (int)size);
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
    }

    private static void DumpTypeDescriptors(string source, string target)
    {
        using var fileStream = new FileStream(source, FileMode.Open, FileAccess.Read);
        TypeDescriptorBlock block = new(fileStream);
        using var writer = new CodeWriter(new StreamWriter(target));
        block.WriteTo(writer);
    }

    private static void DumpEMC(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var fileStream = new FileStream(source, FileMode.Open, FileAccess.Read);
        EmcFile emc = new(fileStream);
        using var writer = new CodeWriter(new StreamWriter(target));
        emc.Root.WriteTo(writer);
    }

    private static void DumpAllEMCs(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source))
        {
            if (Path.GetExtension(file).Equals(".EMC", StringComparison.InvariantCultureIgnoreCase))
                DumpEMC(file, Path.Combine(target, Path.GetFileName(file) + ".txt"));
        }
    }
}
