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
        DumpAll(@"C:\dev\cusg-aventura-orginal\disk1\Install\", "aventura-orginal-disk1");
        DumpAll(@"C:\dev\cusg-aventura-orginal\disk2\Install\", "aventura-orginal-disk2");
    }

    private static readonly string[] ScriptPaths =
    [
        "../Script/MORTADELO.COD",
        "../Script/FILE.COD",
        "../Script/FILEMON.COD",
        "../Script/SCRIPT.COD"
    ];

    private static void DumpAll(string source, string target)
    {
        TypeDescriptorBlock? globalTypes = null;
        var asyncTasks = new List<Task>();

        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            if (!file.EndsWith(".EMC", StringComparison.InvariantCultureIgnoreCase))
                continue;
            Console.WriteLine(file);
            var targetBase = Path.Combine(target, Path.GetFileNameWithoutExtension(file));
            Directory.CreateDirectory(targetBase);
            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
            EmcFile emc = new(fileStream);
            using (var writer = new CodeWriter(new StreamWriter(targetBase + ".types.txt")))
                emc.Types.WriteTo(writer);
            using (var writer = new CodeWriter(new StreamWriter(targetBase + ".emc.txt")))
                emc.Root.WriteTo(writer);
            if (emc.Cod is not null)
            {
                using (var writer = new CodeWriter(new StreamWriter(targetBase + ".script.txt")))
                    CodDumper.FullDump(emc.Cod, emc.Types, writer, rawOps: false);
            }
            if (file.Contains("global", StringComparison.InvariantCultureIgnoreCase) && globalTypes is null)
                globalTypes = emc.Types;

            var foundFiles = new Dictionary<string, EmcEmbeddedFile>();
            foreach (var t in emc.EmbeddedFiles)
            {
                var (fileName, offset, size) = t;
                if (t.Name == "")
                {
                    if (t.Size == 0)
                        continue;
                    else
                        throw new InvalidDataException("Empty filename with content");
                }
                fileName = Path.GetFileName(fileName);
                if (foundFiles.TryGetValue(fileName, out var prev))
                {
                    if (prev == t)
                    {
                        Console.WriteLine($"  Duplicate file: {t}");
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"  Duplicate filename with diff content: {t.Name}");
                        fileName += $"-{t.Offset}";
                    }
                }
                else
                    foundFiles.Add(fileName, t);

                var bytes = ArrayPool<byte>.Shared.Rent((int)size);
                fileStream.Position = offset;
                fileStream.ReadExactly(bytes, 0, (int)size);
                var targetPath = Path.Combine(targetBase, fileName);
                using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                targetStream.Write(bytes, 0, (int)size);

                if (Path.GetExtension(t.Name)?.Equals(".ANI", StringComparison.InvariantCultureIgnoreCase) is true)
                    AddAsyncTask(() => DumpANI(bytes, targetPath));
                else
                    ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        if (globalTypes is null)
            Console.WriteLine("Cannot decompile external script file without global.emc file");
        else
        {
            foreach (var scriptRelPath in ScriptPaths)
            {
                var scriptSourcePath = Path.Combine(source, scriptRelPath);
                if (!File.Exists(scriptSourcePath)) continue;
                CodFile cod = new(scriptSourcePath);
                using var writer = new CodeWriter(new StreamWriter(Path.Combine(target, Path.GetFileNameWithoutExtension(scriptRelPath) + ".script.txt")));
                CodDumper.FullDump(cod, globalTypes, writer, rawOps: false);
            }
        }

        Task.WaitAll([.. asyncTasks]);
        void AddAsyncTask(Action action) => asyncTasks.Add(Task.Run(action));
    }

    private static void DumpANI(byte[] bytes, string targetPath)
    {
        targetPath += ".txt";
        var aniFile = new AniV1File(new MemoryStream(bytes, writable: false), leaveOpen: false);
        var descrFileStream = new StreamWriter(targetPath);
        using var descrWriter = new CodeWriter(descrFileStream);
        aniFile.WriteTo(descrWriter);
        ArrayPool<byte>.Shared.Return(bytes);
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
