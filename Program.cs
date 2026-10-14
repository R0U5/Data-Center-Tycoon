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
string view = null;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (args[i] == "--shot") shot = args[i + 1];
    if (args[i] == "--seed" && long.TryParse(args[i + 1], out var s)) seed = s;
    if (args[i] == "--days" && int.TryParse(args[i + 1], out var d)) days = d;
    if (args[i] == "--view") view = args[i + 1];
}

if (view != null)
{
    days = view switch
    {
        "menu" => -100,
        "setup" => -200,
        "help" => -1,
        _ => days,
    };
}

using var game = new Game1(shot, seed, days);
game.Run();
