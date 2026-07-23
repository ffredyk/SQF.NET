using System;
using System.IO;
using SQSharp.Core;
using SQSharp.Language;
using SQSharp.Compiler;
using SQSharp.VM;
using SQSharp.Host;
using SQSharp.Scheduler;

namespace SQSharp.CLI;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("SQ# CLI — SQF Sharp Scripting Engine");
            Console.WriteLine("Usage: sqf <command> [options]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  run <file>      Execute a .sqf script");
            Console.WriteLine("  repl            Start interactive REPL");
            Console.WriteLine("  compile <file>  Compile to bytecode (.sqfc)");
            Console.WriteLine("  lex <file>      Print token stream");
            Console.WriteLine("  parse <file>    Print AST");
            Console.WriteLine("  serialize <file>  Run script, output binary serialized result");
            Console.WriteLine("  deserialize <file> Read binary, print deserialized value");
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "lex" => LexFile(args),
                "parse" => ParseFile(args),
                "run" => RunFile(args),
                "repl" => RunRepl(),
                "compile" => CompileFile(args),
                "serialize" => SerializeFile(args),
                "deserialize" => DeserializeFile(args),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int LexFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf lex <file>"); return 1; }
        string source = File.ReadAllText(args[1]);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        foreach (var t in tokens)
            Console.WriteLine($"[{t.Line}:{t.Column}] {t}");
        return 0;
    }

    static int ParseFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf parse <file>"); return 1; }
        string source = File.ReadAllText(args[1]);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        PrintAst(ast, "");
        return 0;
    }

    static int RunFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf run <file>"); return 1; }
        string source = File.ReadAllText(args[1]);
        var host = new SqHost();
        host.DeclareMultiplayerCommands();
        host.OnPrint += msg => Console.WriteLine(msg);

        // Pre-create benchmark schedulers for spawnOn parallelism tests
        host.CreateScheduler("B_1");
        host.CreateScheduler("B_2");
        host.CreateScheduler("B_3");
        host.CreateScheduler("B_4");

        var fiber = host.ExecuteString(source, Path.GetFileName(args[1]));
        // Pump scheduler until fiber completes
        while (fiber.State != SQSharp.Scheduler.FiberState.Completed
            && fiber.State != SQSharp.Scheduler.FiberState.Terminated)
        {
            host.Tick();
        }
        Console.WriteLine($"Result: {fiber.Result}");
        return 0;
    }

    static int CompileFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf compile <file> [--binary] [-o <output>]"); return 1; }
        string source = File.ReadAllText(args[1]);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var compiler = new SQSharp.Compiler.Compiler();
        var chunk = compiler.Compile(ast, args[1]);

        bool binary = args.Any(a => a == "--binary" || a == "-b");
        string? outFile = null;
        int oIdx = Array.IndexOf(args, "-o");
        if (oIdx >= 0 && oIdx + 1 < args.Length) outFile = args[oIdx + 1];

        if (binary)
        {
            string outputPath = outFile ?? Path.ChangeExtension(args[1], ".sqfc");
            using var stream = File.Create(outputPath);
            var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8);
            SqBinarySerializer.WriteChunk(writer, chunk);
            Console.WriteLine($"Binary compiled: {outputPath} ({chunk.Instructions.Count} instrs, {chunk.Constants.Count} consts, {chunk.LocalCount} locals, {chunk.Children.Count} children)");
        }
        else
        {
            Console.WriteLine($"Compiled {args[1]}: {chunk.Instructions.Count} instructions, {chunk.Constants.Count} constants, {chunk.LocalCount} locals");
            foreach (var inst in chunk.Instructions)
                Console.WriteLine($"  {inst}");
        }
        return 0;
    }

    static int RunRepl()
    {
        Console.WriteLine("SQ# REPL (type 'exit' to quit)");
        var host = new SqHost();
        host.DeclareMultiplayerCommands();
        host.OnPrint += msg => Console.WriteLine($"  | {msg}");
        while (true)
        {
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (line == null || line.Trim().ToLowerInvariant() == "exit") break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var fiber = host.ExecuteString(line, "repl");
                while (fiber.State != SQSharp.Scheduler.FiberState.Completed
                    && fiber.State != SQSharp.Scheduler.FiberState.Terminated)
                {
                    host.Tick();
                }
                if (!fiber.Result?.IsNil ?? false)
                    Console.WriteLine($"  => {fiber.Result}");
            }
            catch (Exception ex) { Console.WriteLine($"  Error: {ex.Message}"); }
        }
        return 0;
    }

    static int UnknownCommand(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); return 1; }

    static void PrintAst(AstNode node, string indent)
    {
        switch (node)
        {
            case SequenceNode seq:
                Console.WriteLine($"{indent}Sequence:");
                foreach (var e in seq.Expressions) PrintAst(e, indent + "  ");
                break;
            case NumberLiteralNode n: Console.WriteLine($"{indent}Number({n.Value})"); break;
            case StringLiteralNode s: Console.WriteLine($"{indent}String(\"{s.Value}\")"); break;
            case BoolLiteralNode b: Console.WriteLine($"{indent}Bool({b.Value.ToString().ToLower()})"); break;
            case NilLiteralNode: Console.WriteLine($"{indent}Nil"); break;
            case VariableNode v: Console.WriteLine($"{indent}Var({v.Name})"); break;
            case ArrayExprNode arr:
                Console.WriteLine($"{indent}Array[{arr.Elements.Count}]:");
                foreach (var e in arr.Elements) PrintAst(e, indent + "  ");
                break;
            case BinaryCallNode bin:
                Console.WriteLine($"{indent}Binary({bin.Operator}, prec={bin.Precedence}):");
                PrintAst(bin.Left, indent + "  L: ");
                PrintAst(bin.Right, indent + "  R: ");
                break;
            case UnaryCallNode un:
                Console.WriteLine($"{indent}Unary({un.Operator}):");
                PrintAst(un.Operand, indent + "  ");
                break;
            case AssignmentNode assign:
                Console.WriteLine($"{indent}Assign({assign.VariableName}):");
                PrintAst(assign.Value, indent + "  ");
                break;
            case IfThenElseNode ifn:
                Console.WriteLine($"{indent}If:");
                PrintAst(ifn.Condition, indent + "  Cond: ");
                PrintAst(ifn.ThenBody, indent + "  Then: ");
                if (ifn.ElseBody != null) PrintAst(ifn.ElseBody, indent + "  Else: ");
                break;
            case WhileDoNode w:
                Console.WriteLine($"{indent}While:");
                PrintAst(w.Condition, indent + "  Cond: ");
                PrintAst(w.Body, indent + "  Do: ");
                break;
            case SwitchDoNode sw:
                Console.WriteLine($"{indent}Switch:");
                PrintAst(sw.Value, indent + "  Val: ");
                foreach (var c in sw.Cases)
                {
                    if (c.CaseValue != null)
                    {
                        Console.WriteLine($"{indent}  Case:");
                        PrintAst(c.CaseValue, indent + "    Val: ");
                    }
                    else
                        Console.WriteLine($"{indent}  Default:");
                    PrintAst(c.Body, indent + "    Body: ");
                }
                break;
            case ForDoNode fd:
                Console.WriteLine($"{indent}ForDo:");
                PrintAst(fd.Init, indent + "  Init: ");
                PrintAst(fd.Condition, indent + "  Cond: ");
                PrintAst(fd.Step, indent + "  Step: ");
                PrintAst(fd.Body, indent + "  Body: ");
                break;
            case ReturnNode r:
                Console.Write($"{indent}Return");
                if (r.Value != null) { Console.WriteLine(":"); PrintAst(r.Value, indent + "  "); }
                else Console.WriteLine();
                break;
            default: Console.WriteLine($"{indent}{node.NodeType}"); break;
        }
    }

    static string DescribeAst(AstNode node) => node switch
    {
        NumberLiteralNode n => n.Value.ToString(),
        StringLiteralNode s => $"\"{s.Value}\"",
        BoolLiteralNode b => b.Value.ToString().ToLower(),
        NilLiteralNode => "nil",
        VariableNode v => v.Name,
        BinaryCallNode b => $"({DescribeAst(b.Left)} {b.Operator} {DescribeAst(b.Right)})",
        UnaryCallNode u => $"{u.Operator}{DescribeAst(u.Operand)}",
        AssignmentNode a => $"{a.VariableName} = {DescribeAst(a.Value)}",
        SequenceNode s => string.Join("; ", s.Expressions.ConvertAll(e => DescribeAst(e))),
        _ => $"<{node.NodeType}>"
    };

    // --- Serialization ---

    static int SerializeFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf serialize <file> [--output <bin>]"); return 1; }
        string file = args[1];
        string? outFile = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
                outFile = args[++i];
        }

        var host = new SqHost();
        var fiber = host.ExecuteString(File.ReadAllText(file));
        while (fiber.State != FiberState.Completed && fiber.State != FiberState.Terminated)
            host.Tick();

        var result = fiber.Result ?? SqValue.Nil;
        byte[] data = SqBinarySerializer.Serialize(result);

        if (outFile != null)
        {
            File.WriteAllBytes(outFile, data);
            Console.WriteLine($"Serialized {data.Length} bytes to {outFile}");
        }
        else
        {
            // Output base64 to stdout
            Console.WriteLine(Convert.ToBase64String(data));
        }
        return 0;
    }

    static int DeserializeFile(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: sqf deserialize <file>"); return 1; }
        byte[] data = File.ReadAllBytes(args[1]);
        var value = SqBinarySerializer.Deserialize(data);
        Console.WriteLine($"Result: {value}");
        return 0;
    }
}
