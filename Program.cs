using System.Linq;
using DataCenterTycoon;

if (args.Contains("--sim"))
{
    BalanceHarness.Run(args);
    return;
}

string shot = null;
long? seed = null;
int days = 0;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (args[i] == "--shot") shot = args[i + 1];
    if (args[i] == "--seed" && long.TryParse(args[i + 1], out var s)) seed = s;
    if (args[i] == "--days" && int.TryParse(args[i + 1], out var d)) days = d;
}

using var game = new Game1(shot, seed, days);
game.Run();
