using System;
using System.IO;
using SQSharp.Language;

var source = File.ReadAllText(@"d:\dev\C#\SQF.NET\samples\bench-math.sqf");
var lexer = new Lexer(source);
var tokens = lexer.Tokenize();

Console.WriteLine("=== TOP LEVEL TOKENS ===");
int idx = 0;
foreach (var t in tokens)
{
    Console.WriteLine($"[{idx}] [{t.Line}:{t.Column}] {t}");
    if (t.Type == TokenType.CodeBlock && t.NestedTokens != null)
    {
        Console.WriteLine($"  Nested ({t.NestedTokens.Count} tokens):");
        foreach (var nt in t.NestedTokens)
            Console.WriteLine($"    [{nt.Line}:{nt.Column}] {nt}");
    }
    idx++;
}
