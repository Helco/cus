using System;
using static Cus.CodDumper;

namespace Cus;

internal static class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var cod = new CodFile(@"C:\dev\cusg\Script\SCRIPT.COD");
        //using var writer = new CodeWriter(Console.Out, disposeWriter: false);
        var outWriter = new StreamWriter("cus.txt");
        using var writer = new CodeWriter(outWriter);

        FullDump(cod, writer, false);
    }
}
