// Headless test: renders screenshots and auto-plays the game to catch crashes.
// Build: mcs -langversion:5 -r:System.Windows.Forms -r:System.Drawing src/*.cs tests/Sim.cs
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaskbarTamer;

static class Sim
{
    static void Shot(GameForm g, string name)
    {
        using (Bitmap b = new Bitmap(1333, 178))
        using (Graphics gr = Graphics.FromImage(b)) { g.RenderTo(gr, b.Width, b.Height); b.Save(name); }
    }

    static void Steps(GameForm g, int n) { for (int i = 0; i < n; i++) g.Step(1 / 30f); }

    static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        GameForm.SavePathOverride = Path.Combine(outDir, "save.txt");
        Directory.CreateDirectory(outDir);
        if (File.Exists(GameForm.SavePathOverride)) File.Delete(GameForm.SavePathOverride);
        GameForm g = new GameForm(true);
        Steps(g, 5);
        Shot(g, Path.Combine(outDir, "1_intro.png"));
        for (int i = 0; i < 3; i++) { Steps(g, 10); g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false); }
        Steps(g, 10);
        Shot(g, Path.Combine(outDir, "2_starter.png"));
        g.SimHold(Keys.Right, true); g.SimHold(Keys.Right, false);
        g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false);
        Steps(g, 3);
        for (int i = 0; i < 6; i++) { g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false); Steps(g, 5); }
        Steps(g, 30);
        Shot(g, Path.Combine(outDir, "3_town.png"));
        Console.WriteLine(g.DebugState());

        // auto-play
        int shotsTaken = 0;
        g.SimHold(Keys.Right, true);
        int total = 0, battles = 0; int lastMode = 0;
        for (int step = 0; step < 30 * 60 * 120 && g.ChampWins < 1; step++)
        {
            g.Step(1 / 30f);
            if (g.Mode == 1 && lastMode != 1) battles++;
            lastMode = g.Mode;
            if (g.Mode == 1)
            {
                if (shotsTaken == 0 && g.IsIdle) { Shot(g, Path.Combine(outDir, "4_battle_menu.png")); g.DebugPhase(1); Shot(g, Path.Combine(outDir, "5_battle_fight.png")); g.DebugPhase(0); shotsTaken = 1; }
                if (step % 9 == 0) { g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false); g.SimHold(Keys.Z, false); }
                g.DebugAutoBattle(battles % 3 == 0);
            }
            else if (g.Mode == 0)
            {
                if (!g.IsIdle && step % 9 == 0) { g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false); }
                g.SimHold(Keys.Right, true);
            }
            total = step;
            if (step % (30 * 60 * 5) == 0) Console.WriteLine("t=" + step / 30 + "s battles=" + battles + " " + g.DebugState());
            if (shotsTaken == 1 && g.Mode == 0 && g.IsIdle && step > 30 * 60 * 3) { Shot(g, Path.Combine(outDir, "6_later.png")); shotsTaken = 2; }
        }
        Console.WriteLine("END t=" + total / 30 + "s battles=" + battles + " " + g.DebugState());
        for (int i = 0; i < 30 * 60 && !(g.Mode == 0 && g.IsIdle); i++) { g.Step(1 / 30f); if (i % 9 == 0) { g.SimHold(Keys.Z, true); g.SimHold(Keys.Z, false); } }
        g.SimHold(Keys.Right, false);
        Shot(g, Path.Combine(outDir, "7_end.png"));
        g.SimHold(Keys.Right, false);
        g.SimHold(Keys.Tab, true); g.SimHold(Keys.Tab, false); Steps(g, 3);
        Shot(g, Path.Combine(outDir, "8_team.png"));
        foreach (float x in new float[] { 960, 1740, 2550, 3380, 3700, 4190, 4620 }) { g.SimHold(Keys.Escape, true); g.SimHold(Keys.Escape, false); g.DebugSetX(x); Steps(g, 2); g.DebugClearMsgs(); Shot(g, Path.Combine(outDir, "9_world_" + x + ".png")); }
        Console.WriteLine(File.ReadAllText(GameForm.SavePathOverride));
        return g.ChampWins >= 1 ? 0 : 1;
    }
}
