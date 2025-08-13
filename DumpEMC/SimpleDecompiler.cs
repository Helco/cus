using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cus;

internal class SimpleDecompiler
{
    public static void Decompile(CodFile cod, TypeDescriptorBlock types, CodeWriter writer, bool dumpInstructions) =>
        new SimpleDecompiler(cod, types, writer, dumpInstructions).Decompile();

    private readonly CodFile cod;
    private readonly NamedFunctionSignature[] kernelCalls;
    private readonly CodeWriter writer;
    private readonly bool dumpInstructions;
    private readonly string?[] labels;
    private readonly bool[] isStrongLabel;
    private readonly Dictionary<int, string> variableNames;
    private readonly Dictionary<int, string> strings;

    private SimpleDecompiler(CodFile cod, TypeDescriptorBlock types, CodeWriter writer, bool dumpInstructions)
    {
        this.cod = cod;
        kernelCalls = [.. types.Descriptors.OfType<NamedFunctionSignature>()];
        this.writer = writer;
        this.dumpInstructions = dumpInstructions;
        labels = new string?[cod.Ops.Count];
        isStrongLabel = new bool[cod.Ops.Count];
        variableNames = cod.GlobalVariables.ToDictionary(v => v.value, v => v.name);
        strings = cod.Strings.ToDictionary(s => s.offset, s => s.value);
    }

    private string GetLabelName(int offset) =>
        labels[offset] ?? throw new InvalidOperationException($"CreateLabels missed the offset {offset}");

    private void SetLabel(int offset, string name, bool isStrongName)
    {
        if (labels[offset] == null)
        {
            labels[offset] = name;
            isStrongLabel[offset] = isStrongName;
        }
        else if (isStrongName)
        {
            isStrongLabel[offset] = true;
            if (labels[offset]!.StartsWith("SHARED: "))
                labels[offset] += ", " + name;
            else
                labels[offset] = $"SHARED: {labels[offset]}, {name}";
        }
    }

    private void CreateLabels()
    {
        foreach (var proc in cod.GlobalProcedures)
            SetLabel(proc.offset - 1, proc.name, isStrongName: true);
        foreach (var beh in cod.Behaviors)
        {
            foreach (var proc in beh.procedures)
                SetLabel(proc.offset - 1, $"{beh.name}::{proc.name}", isStrongName: true);
        }
        foreach (var (offset, (_, code, value)) in cod.Ops.Indexed())
        {
            if (code is CodOpCode.Jump or CodOpCode.JumpIfTrue or CodOpCode.JumpIfFalse)
                SetLabel(offset + value, $"loc_{offset + value}", isStrongName: false);
            else if (code is CodOpCode.Call)
                SetLabel(value - 1, $"proc_{value - 1}", isStrongName: false);
        }
    }

    private enum ExpressionType
    {
        Number,
        String,
        Address,
        Expression
    }

    private struct Expression
    {
        public ExpressionType type;
        public int value;
        public string text;
        public bool needsBrackets;
        public string BracketedText => needsBrackets ? $"({text})" : text;
    }

    private Expression NumberExpression(int value) => new()
    {
        type = ExpressionType.Number,
        value = value,
        text = value.ToString(),
        needsBrackets = false
    };

    private Expression StringExpression(int stringOffset) => new()
    {
        type = ExpressionType.String,
        value = stringOffset,
        text = strings.TryGetValue(stringOffset, out var value)
            ? $"\"{value}\"" : $"invalid string({stringOffset})",
        needsBrackets = false
    };

    private Expression StringExpression(string value) => new()
    {
        type = ExpressionType.String,
        value = 0,
        text = $"string[ {value} ]",
        needsBrackets = false
    };

    private Expression AddressExpression(int address)
    {
        string text;
        if (variableNames.TryGetValue(address, out var name))
            text = name;
        else if (cod.Generation is Generation.V1 && strings.TryGetValue(address, out text!))
        {
            return new()
            {
                type = ExpressionType.String,
                value = address,
                text = $"\"{text}\"",
                needsBrackets = false
            };
        }
        else
            text = $"invalid-address({address})";

        return new()
        {
            type = ExpressionType.Address,
            value = address,
            text = "&" + text,
            needsBrackets = false
        };
    }

    private Expression DynAddressExpression(Expression address) => address.type is ExpressionType.Number
    ? AddressExpression(address.value) // actually still a static address expression
    : new()
    {
        type = ExpressionType.Expression,
        value = 0,
        text = $"&( {address.text} )",
        needsBrackets = false
    };

    private Expression GeneralExpression(string text, bool needsBrackets = true) => new()
    {
        type = ExpressionType.Expression,
        text = text,
        needsBrackets = needsBrackets
    };

    private Expression DuplicatedExpression(Expression original, string newText) => new()
    {
        type = original.type,
        value = original.value,
        text = newText,
        needsBrackets = false
    };

    private void CreateInstructions()
    {
        Stack<Expression> stack = new();
        for (int offset = 0; offset < cod.Ops.Count; offset++)
        {
            var (_, op, arg) = cod.Ops[offset];

            if (isStrongLabel[offset] && stack.Count > 0)
            {
                writer.WriteLine($"// WARNING: Arrived with {stack.Count} stack entries");
                foreach (var entry in stack.Reverse())
                    writer.WriteLine($"//  - {entry.text}");
                stack.Clear();
            }
            if (isStrongLabel[offset] && offset != 0)
                writer.WriteLine();
            if (labels[offset] != null)
                writer.WriteLine(GetLabelName(offset) + ":");
            using var indented = writer.Indented;
            if (dumpInstructions)
            {
                using var indented2 = indented.Indented;
                indented2.WriteLine($"{offset}: {op} {arg}");
            }

            Expression value;
            switch (op)
            {
                case CodOpCode.Nop: break;
                case CodOpCode.Dup:
                    if (stack.Count == 0)
                    {
                        stack.Push(GeneralExpression("ERROR", false));
                        writer.WriteLine("// ERROR: Arrived without stack entry for dup");
                    }
                    else if (stack.Peek().text.StartsWith("tmp_") ||
                            stack.Peek().type is not ExpressionType.Expression)
                        stack.Push(stack.Peek());
                    else
                    {
                        var tmpName = $"tmp_{offset}";
                        value = stack.Pop();
                        indented.WriteLine($"{tmpName} = {value.text}");
                        stack.Push(DuplicatedExpression(value, tmpName));
                        stack.Push(DuplicatedExpression(value, tmpName));
                    }
                    break;
                case CodOpCode.PushAddr: stack.Push(AddressExpression(arg)); break;
                case CodOpCode.PushValue: stack.Push(NumberExpression(arg)); break;
                case CodOpCode.PushDynAddr:
                    if (stack.Count == 0)
                        writer.WriteLine("// ERROR: Arrived without stack entry for dyn address push");
                    else if (stack.Peek().type is ExpressionType.String)
                        writer.WriteLine($"// ERROR: Arrived at dyn address push with unexpected {stack.Peek().type}");
                    else
                        stack.Push(DynAddressExpression(stack.Pop()));
                    break;
                case CodOpCode.Deref:
                    if (stack.Count == 0)
                        writer.WriteLine("// ERROR: Arrived without stack entry for deref");
                    else if (stack.Peek().type != ExpressionType.Address)
                    {
                        writer.WriteLine("// ERROR: Arrived without address on stack for deref");
                        stack.Push(GeneralExpression($"#deref( {stack.Pop().text} )", false));
                    }
                    else
                        stack.Push(GeneralExpression(stack.Pop().text[1..], false));
                    break;
                case CodOpCode.Pop1:
                    if (stack.Count < 1)
                        writer.WriteLine($"// ERROR: Arrived with {stack.Count} stack entries, attempting to pop 1");
                    else
                        stack.Pop();
                    break;
                case CodOpCode.PopN:
                    if (stack.Count < arg || arg < 0)
                        writer.WriteLine($"// ERROR: Arrived with {stack.Count} stack entries, attempting to pop {arg}");
                    for (int i = 0; i < arg && stack.Count > 0; i++)
                        stack.Pop();
                    break;
                case CodOpCode.Store:
                    int origCount = stack.Count;
                    if (!stack.TryPop(out value) || !stack.TryPop(out var addr))
                        writer.WriteLine($"// ERROR: Arrived with {origCount} stack entries, attempting to store");
                    else if (addr.type != ExpressionType.Address)
                    {
                        writer.WriteLine($"// ERROR: Attempted to store value in non-address");
                        indented.WriteLine($"STORE( {addr.text} ) = {value.text}");
                    }
                    else
                        indented.WriteLine($"{addr.text.Substring(1)} = {value.text}");
                    if (value.text == null)
                        value = GeneralExpression("ERROR", false);
                    stack.Push(value);
                    break;
                case CodOpCode.LoadString:
                    popNumber(out value, "LoadString");
                    if (value.type != ExpressionType.Number)
                        stack.Push(StringExpression(value.text));
                    else
                        stack.Push(StringExpression(value.value));
                    break;
                case CodOpCode.Call:
                case CodOpCode.KernelProc:
                    var isReturnValue = extractArgsAndRets(ref offset, out var args);
                    var callName = op == CodOpCode.Call
                        ? "#" + GetLabelName(arg - 1)
                        : GetKernelProcName(arg);
                    var exprText = args.Length > 0
                        ? $"{callName}( {string.Join(", ", args.Select(a => a.text))} )"
                        : callName + "()";
                    if (isReturnValue)
                        stack.Push(GeneralExpression(exprText, false));
                    else
                        indented.WriteLine(exprText);
                    break;
                case CodOpCode.JumpIfFalse:
                    popNumber(out var cond, "jumpIfFalse");
                    indented.WriteLine($"if not ({cond.text})");
                    indented.WriteLine($"\tgoto {GetLabelName(offset + arg)}");
                    break;
                case CodOpCode.JumpIfTrue:
                    popNumber(out cond, "jumpIfTrue");
                    indented.WriteLine($"if ({cond.text})");
                    indented.WriteLine($"\tgoto {GetLabelName(offset + arg)}");
                    break;
                case CodOpCode.Jump:
                    indented.WriteLine($"goto {GetLabelName(offset + arg)}");
                    break;
                case CodOpCode.Negate:
                    popNumber(out value, "negate");
                    stack.Push(GeneralExpression($"-{value.BracketedText}"));
                    break;
                case CodOpCode.BooleanNot:
                    popNumber(out value, "boolean not");
                    stack.Push(GeneralExpression($"!{value.BracketedText}"));
                    break;
                case CodOpCode.Mul:
                    popNumbers(out var right, out var left, "mul");
                    stack.Push(GeneralExpression($"{left.BracketedText} * {right.BracketedText}"));
                    break;
                case CodOpCode.Add:
                    popNumbers(out right, out left, "add");
                    stack.Push(GeneralExpression($"{left.BracketedText} + {right.BracketedText}"));
                    break;
                case CodOpCode.Sub:
                    popNumbers(out right, out left, "sub");
                    stack.Push(GeneralExpression($"{left.BracketedText} - {right.BracketedText}"));
                    break;
                case CodOpCode.Less:
                    popNumbers(out right, out left, "Less");
                    stack.Push(GeneralExpression($"{left.BracketedText} < {right.BracketedText}"));
                    break;
                case CodOpCode.Greater:
                    popNumbers(out right, out left, "Greater");
                    stack.Push(GeneralExpression($"{left.BracketedText} > {right.BracketedText}"));
                    break;
                case CodOpCode.LessEquals:
                    popNumbers(out right, out left, "LessEquals");
                    stack.Push(GeneralExpression($"{left.BracketedText} <= {right.BracketedText}"));
                    break;
                case CodOpCode.GreaterEquals:
                    popNumbers(out right, out left, "GreaterEquals");
                    stack.Push(GeneralExpression($"{left.BracketedText} >= ({right.BracketedText}"));
                    break;
                case CodOpCode.Equals:
                    popNumbers(out right, out left, "Equals");
                    stack.Push(GeneralExpression($"{left.BracketedText} == {right.BracketedText}"));
                    break;
                case CodOpCode.NotEquals:
                    popNumbers(out right, out left, "NotEquals");
                    stack.Push(GeneralExpression($"{left.BracketedText} != {right.BracketedText}"));
                    break;
                case CodOpCode.BitAnd:
                    popNumbers(out right, out left, "BitAnd");
                    stack.Push(GeneralExpression($"{left.BracketedText} & {right.BracketedText}"));
                    break;
                case CodOpCode.BitOr:
                    popNumbers(out right, out left, "BitOr");
                    stack.Push(GeneralExpression($"{left.BracketedText} | {right.BracketedText}"));
                    break;
                case CodOpCode.ReturnNone:
                    indented.WriteLine($"return");
                    break;
                case CodOpCode.ReturnValue:
                    popNumber(out value, "return");
                    indented.WriteLine($"return {value.text}");
                    break;
                default:
                    if (!dumpInstructions)
                        indented.WriteLine($"ASM {op} {arg}"); break;
            }

        }

        void popNumber(out Expression value, string context)
        {
            value = GeneralExpression("ERROR", false);
            if (stack.Count < 1)
                writer.WriteLine($"// ERROR: Attempted to pop one number for {context}");
            else
                value = stack.Pop();
            if (value.type is ExpressionType.String or ExpressionType.Address)
                writer.WriteLine($"// WARNING: Expected numeric expression for {context}");
        }

        void popNumbers(out Expression value1, out Expression value2, string context)
        {
            value1 = GeneralExpression("ERROR1", false);
            value2 = GeneralExpression("ERROR2", false);
            if (stack.Count < 2)
                writer.WriteLine($"// ERROR: Attempted to pop one number for {context}");
            else
            {
                value1 = stack.Pop();
                value2 = stack.Pop();
            }
            if (value1.type is ExpressionType.String or ExpressionType.Address ||
                value2.type is ExpressionType.String or ExpressionType.Address)
                writer.WriteLine($"// WARNING: Expected numeric expression for {context}");
        }

        bool extractArgsV3(ref int offset, out Expression[] args)
        {
            args = Array.Empty<Expression>();
            if (offset + 1 >= cod.Ops.Count || cod.Ops[offset + 1].code != CodOpCode.PopN)
            {
                writer.WriteLine("// ERROR: Calling somewhere without expected calling convention");
                return false;
            }
            int argCount = cod.Ops[offset + 1].value;
            if (stack.Count < argCount)
            {
                writer.WriteLine($"// ERROR: Calling with {argCount} arguments but only {stack.Count} stack entries");
                argCount = stack.Count;
            }
            args = new Expression[argCount];
            for (int i = 0; i < argCount; i++)
                args[i] = stack.Pop();
            offset++;
            if (dumpInstructions)
                writer.WriteLine($"\t\t{offset}: PopN {cod.Ops[offset].value}");
            return true;
        }

        bool extractRets(ref int offset)
        {
            if (offset + 1 >= cod.Ops.Count)
                return true;
            int popCount = cod.Ops[offset + 1].code switch
            {
                CodOpCode.Pop1 => 1,
                CodOpCode.PopN => cod.Ops[offset + 1].value,
                _ => -1
            };
            if (popCount < 0)
                return true;
            if (popCount != 1)
                writer.WriteLine("// ERROR: Expected at most one return value to pop");
            offset++;
            if (dumpInstructions)
                writer.WriteLine($"\t\t{offset}: {cod.Ops[offset].code} {cod.Ops[offset].value}");
            return false;
        }

        bool extractArgsAndRetsV3(ref int offset, out Expression[] args)
        {
            return extractArgsV3(ref offset, out args) && extractRets(ref offset);
        }

        bool extractArgsAndRetsV1(ref int offset, out Expression[] args)
        {
            args = [];
            bool isCall = cod.Ops[offset].code is CodOpCode.Call;
            if (isCall)
                goto exit;
            var kernelProcI = cod.Ops[offset].value - 1;
            if (kernelProcI < 0 || kernelProcI >= kernelCalls.Length)
            {
                writer.WriteLine("// WARNING: Cannot determine arguments of invalid kernel proc");
                goto exit;
            }
            var kernelProc = kernelCalls[kernelProcI];
            args = [.. Enumerable.Repeat(
                GeneralExpression("ERROR", false), kernelProc.Signature.Parameters.Count)];
            for (int i = 0; i < args.Length; i++)
            {
                if (!stack.TryPop(out args[i]))
                {
                    writer.WriteLine($"// ERROR: Expected {args.Length} arguments, but the stack only had {i}");
                    goto exit;
                }
            }
            
            exit:
            return extractRets(ref offset);
        }

        bool extractArgsAndRets(ref int offset, out Expression[] args) => cod.Generation switch
        {
            Generation.V1 => extractArgsAndRetsV1(ref offset, out args),
            _ => extractArgsAndRetsV3(ref offset, out args)
        };
    }

    private void Decompile()
    {
        CreateLabels();
        CreateInstructions();
    }

    private static readonly string[] KernelProcNames =
    {
        "playVideo",
        "playSound",
        "playMusic",
        "stopMusic",
        "waitForMusicToEnd",
        "showCenterBottomText",
        "stopAndTurn",
        "stopAndTurnMe",
        "changeCharacter",
        "sayText",
        "nop",
        "go",
        "put",
        "changeCharacterRoom",
        "killProcesses",
        "timer",
        "on",
        "off",
        "pickup",
        "characterPickup",
        "drop",
        "characterDrop",
        "delay",
        "hadNoMousePressFor",
        "nop",
        "fork",
        "animate",
        "animateCharacter",
        "animateTalking",
        "changeRoom",
        "toggleRoomFloor",
        "setDialogLineReturn",
        "dialogMenu",
        "clearInventory",
        "nop",
        "fadeType0",
        "fadeType1",
        "setLodBias",
        "fadeType2",
        "setActiveTextureSet",
        "setMaxCamSpeedFactor",
        "waitCamStopping",
        "camFollow",
        "camShake",
        "lerpCamXY",
        "lerpCamZ",
        "lerpCamScale",
        "lerpCamToObjectWithScale",
        "lerpCamToObjectResettingZ",
        "lerpCamRotation",
        "fadeIn",
        "fadeOut",
        "fadeIn2",
        "fadeOut2",
        "lerpCamXYZ",
        "lerpCamToObjectKeepingZ"
    };
    private string GetKernelProcName(int value) => value < 1 || value > kernelCalls.Length
        ? $"invalid kernel({value})"
        : kernelCalls[value - 1].Name;
    /*private string GetKernelProcName(int value) => value < 1 || value > KernelProcNames.Length
        ? $"invalid kernel({value})"
        : KernelProcNames[value - 1] == "nop" ? KernelProcNames[value - 1] + value
        : KernelProcNames[value - 1];*/
}
