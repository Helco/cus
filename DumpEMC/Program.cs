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
    }

    private static void DumpTypeDescriptors(string source, string target)
    {
        using var fileStream = new FileStream(source, FileMode.Open, FileAccess.Read);
        TypeDescriptorBlock block = new(fileStream);
        using var writer = new CodeWriter(new StreamWriter(target));
        block.WriteTo(writer);
    }
}
