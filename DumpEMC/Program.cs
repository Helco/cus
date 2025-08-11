namespace Cus;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        DumpTypeDescriptors(@"C:\dev\cusg\Mapas\GLOBAL.EMC", "types-aventura-german.txt");
        DumpTypeDescriptors(@"C:\dev\cusg-aventura-demo\DATA01\Mapas\GLOBAL.EMC", "types-aventura-german-demo.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\English\Mapas\GLOBAL.EMC", "types-aventura-english.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\Spanish\Mapas\GLOBAL.EMC", "types-aventura-spanish.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Vaqueros\global.emc", "types-vaqueros.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Terror\global.emc", "types-terror.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Operación Moscú\English\Mapas\GLOBAL.EMC", "types-moscu-english.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\SextaSecta\Spanish\Mapas\GLOBAL.EMC", "types-secta.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón La banda de Corvino\Mapas\GLOBAL.EMC", "types-corvino.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón El escarabajo de Cleopatra\Spanish\Mapas\GLOBAL.EMC", "types-escarabajo.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Balones\Archivos\Mapas\GLOBAL.EMC", "types-balones.txt");
        DumpTypeDescriptors(@"E:\SteamLibrary\steamapps\common\Mamelucos\Archivos\Mapas\GLOBAL.EMC", "types-mamelucos.txt");

        DumpAllEMCs(@"C:\dev\cusg\Mapas\", "aventura-german");
        DumpAllEMCs(@"C:\dev\cusg-aventura-demo\DATA01\Mapas\", "aventura-german-demo");
        DumpAllEMCs(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\English\Mapas\", "aventura-english");
        DumpAllEMCs(@"E:\SteamLibrary\steamapps\common\Mortadelo y Filemón Una aventura de cine - Edición especial\Spanish\Mapas\", "aventura-spanish");
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
