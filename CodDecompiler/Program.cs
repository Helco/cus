using System;

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

        writer.Write("MemorySize: ");
        writer.Write(cod.MemorySize);
        writer.WriteLine();
        writer.WriteLine();

        writer.WriteLine("Strings: ");
        using (var indented = writer.Indented)
        {
            foreach (var (offset, name) in cod.Strings)
                indented.WriteLine($"{offset:D5}: {name}");
        }
        writer.WriteLine();

        WriteVariables(cod.GlobalVariables, writer);
        writer.WriteLine();

        WriteProcedures(cod.GlobalProcedures, writer);
        writer.WriteLine();

        writer.WriteLine("Behaviors: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, behavior) in cod.Behaviors.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.WriteLine(behavior.name);
                WriteVariables(behavior.variables, indented);
                WriteProcedures(behavior.procedures, indented);
                indented.WriteLine();
            }
        }
        writer.WriteLine();

        writer.WriteLine("Ops: ");
        bool rawOps = false;
        using (var indented = writer.Indented)
        {
            if (rawOps)
            {
                foreach (var (index, (opCode, arg)) in cod.Ops.Indexed())
                    indented.WriteLine($"{index:D5}: {opCode} {arg}");
            }
            else
                SimpleDecompiler.Decompile(cod, indented, false);
        }
    }

    private static void WriteProcedures(IReadOnlyList<CodProcedure> procedures, CodeWriter writer)
    {
        writer.WriteLine("Procedures: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, proc) in procedures.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.Write(proc.name);
                indented.Write(" @ ");
                indented.Write(proc.offset);
                indented.Write(" with ");
                indented.Write(proc.unknown);
                indented.WriteLine();
            }
        }
    }

    private static void WriteVariables(IReadOnlyList<CodVariable> variables, CodeWriter writer)
    {
        writer.WriteLine("Variables: ");
        using (var indented = writer.Indented)
        {
            foreach (var (index, variable) in variables.Indexed())
            {
                indented.Write($"{index:D4}: ");
                indented.Write(variable.name);
                indented.Write(" = ");
                indented.Write(variable.value);
                indented.WriteLine();
            }
        }
    }

    public static IEnumerable<(int, T)> Indexed<T>(this IEnumerable<T> set)
    {
        int index = 0;
        foreach (var value in set)
            yield return (index++, value);
    }
}
