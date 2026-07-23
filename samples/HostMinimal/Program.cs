// ============================================================
// HostMinimal — Minimal .NET host for SQ# scripts
// Demonstrates: host creation, command registration, execution
// ============================================================

using System;
using SQSharp.Core;
using SQSharp.Host;

// ---- Create the host ----
var host = new SqHost();

// ---- Register custom commands ----

// Nular command (no args):
host.RegisterNular("getServerFps", () => new SqValue(60.0));
host.RegisterNular("getServerTime", () => new SqValue(DateTime.Now.Second));
host.RegisterNular("isProduction", () => SqValue.False);

// Unary commands (one arg):
host.RegisterUnary("doubleIt", arg => new SqValue(arg.AsNumber() * 2));
host.RegisterUnary("shout", arg => new SqValue(arg.AsString().ToUpper()));
host.RegisterUnary("square", arg =>
{
    var n = arg.AsNumber();
    return new SqValue(n * n);
});

// Binary commands (two args, with precedence):
host.RegisterBinary("addBonus", (left, right) =>
    new SqValue(left.AsNumber() + right.AsNumber()), precedence: 6);

host.RegisterBinary("multiplyBy", (left, right) =>
    new SqValue(left.AsNumber() * right.AsNumber()), precedence: 7);

// Nular returning Array:
host.RegisterNular("getPlayers", () =>
{
    var arr = new SqArray();
    arr.PushBack(new SqValue("Alice"));
    arr.PushBack(new SqValue("Bob"));
    arr.PushBack(new SqValue("Charlie"));
    return new SqValue(arr);
});

// ---- Handle script output ----
host.OnPrint += msg => Console.WriteLine($"  [SCRIPT] {msg}");

// ---- Execute scripts ----

Console.WriteLine("=== Example 1: Basic arithmetic ===");
host.ExecuteString(@"
    private _x = 10;
    private _y = 20;
    private _sum = _x + _y;
    print f""Sum: {_sum}"";
");

Console.WriteLine("\n=== Example 2: Custom commands ===");
host.ExecuteString(@"
    private _doubled = doubleIt 21;
    print f""doubleIt 21 = {_doubled}"";

    private _shouted = shout ""hello"";
    print f""shout hello = {_shouted}"";

    private _bonus = 50 addBonus 25;
    print f""50 addBonus 25 = {_bonus}"";
");

Console.WriteLine("\n=== Example 3: Sleep and async ===");
host.ExecuteString(@"
    print ""Starting async demo..."";
    sleep 1;
    print ""1 second passed"";
    sleep 0.5;
    print ""1.5 seconds passed"";
    print ""Async demo done!"";
");

Console.WriteLine("\n=== Example 4: Custom command returning array ===");
host.ExecuteString(@"
    private _players = getPlayers;
    print f""Players: {_players}"";
    {
        print f""  - {_x}"";
    } forEach _players;
");

Console.WriteLine("\n=== Example 5: Spawn and await ===");
host.ExecuteString(@"
    private _task = spawn {
        print ""  Task: working..."";
        sleep 0.5;
        print ""  Task: still working..."";
        sleep 0.5;
        return ""task result"";
    };

    print ""Main: waiting for task..."";
    private _result = await _task;
    print f""Main: got result -> {_result}"";
");

// ---- Game loop simulation ----
Console.WriteLine("\n=== Press Enter to tick the scheduler ===");
Console.WriteLine("(Each Enter = 1 frame, 3ms budget)");

host.ExecuteString(@"
    spawn {
        print ""Frame counter started..."";
        for ""_i"" from 1 to 5 do {
            sleep 1;
            print f""Frame tick {_i}"";
        };
        print ""Frame counter done!"";
    };

    spawn {
        print ""Heavy task started..."";
        sleep 3;
        print ""Heavy task done!"";
    };
");

// Pump the scheduler frame by frame:
int frame = 0;
while (host.MainScheduler.ReadyCount > 0 || host.MainScheduler.WaitingCount > 0)
{
    Console.ReadLine(); // Wait for Enter
    frame++;
    host.TickMain();
    Console.WriteLine($"  [Frame {frame}] Ready: {host.MainScheduler.ReadyCount}, Waiting: {host.MainScheduler.WaitingCount}");
}

Console.WriteLine("\n=== All scripts finished ===");
