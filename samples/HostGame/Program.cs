// Simple game loop host demo
// Shows how a .NET host would integrate SQ# into its game loop

using System;
using SQSharp.Host;

var host = new SqHost();

// Register custom game commands
host.RegisterNular("getPlayerHealth", () => new SQSharp.Core.SqValue(75.0));
host.RegisterNular("getFrameCount", () => new SQSharp.Core.SqValue(_frameCount));

// Handle script output
host.OnPrint += msg => Console.WriteLine($"  [SCRIPT] {msg}");

// Spawn initial scripts
host.ExecuteString(@"
    hint 'Game started!';
    sleep 1;
    hint 'One second later...';
");
host.ExecuteString(@"
    _x = 0;
    while { _x < 3 } do {
        hint format ['Tick %1', _x];
        _x = _x + 1;
        sleep 0.5;
    };
    hint 'Done!';
");

// Game loop
int _frameCount = 0;
Console.WriteLine("=== Game Loop Starting ===");
for (int frame = 0; frame < 120; frame++) // 120 frames
{
    _frameCount = frame;
    host.TickMain(); // Pump the main scheduler

    if (host.MainScheduler.ReadyCount == 0 && host.MainScheduler.WaitingCount == 0)
    {
        Console.WriteLine($"Frame {frame}: All scripts completed.");
        break;
    }

    // Simulate frame timing
    System.Threading.Thread.Sleep(16); // ~60 FPS
}
Console.WriteLine("=== Game Loop Ended ===");
