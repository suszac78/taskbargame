<# :
@echo off
rem Taskbar Tamer - double-click to play. Right-click the game for options.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "$f='%~f0'; iex ([IO.File]::ReadAllText($f))"
exit /b
#>
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
try {
$src = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
// Game data: types, moves, critters, trainers and the world layout.
// Written in C# 5 so Windows PowerShell's built-in compiler can build it.

namespace TaskbarTamer
{
    public class MoveDef
    {
        public string Name; public int Type; public int Power; public int Acc;
        public MoveDef(string n, int t, int p, int a) { Name = n; Type = t; Power = p; Acc = a; }
    }

    public class SpeciesDef
    {
        public string Name; public int Type;
        public int Hp, Atk, Def, Spd;
        public int EvoTo, EvoLevel;
        public int[] Learn;      // pairs: level, move id
        public int Sprite;       // index into Data.Sprites
        public bool Big;         // evolved forms are drawn larger
        public int CatchRate;    // percent
        public int[] Pal;        // colours for '1', '2', '3'
    }

    public class TrainerDef
    {
        public string Name; public int X; public int Look;
        public int[] Team;       // pairs: species, level
        public string Intro, Lose;
        public bool Champion;
    }

    public class ZoneDef
    {
        public string Name; public int X0, X1, Kind;
        public int[] Wild;       // pairs: species, weight
        public int MinLv, MaxLv;
    }

    public class BuildingDef
    {
        public int X, Kind; // 0 heal, 1 shop, 2 home
        public BuildingDef(int x, int k) { X = x; Kind = k; }
    }

    public static class Data
    {
        public const int NORMAL = 0, FIRE = 1, WATER = 2, LEAF = 3, VOLT = 4, ROCK = 5;
        public static readonly string[] TypeNames = { "NORMAL", "FIRE", "WATER", "LEAF", "VOLT", "ROCK" };
        public static readonly int[] TypeColors = { 0xB8B8A0, 0xF08030, 0x5890F0, 0x60C048, 0xE8C020, 0xB09848 };

        public static double Eff(int atk, int def)
        {
            switch (atk)
            {
                case FIRE: return def == LEAF ? 2 : (def == WATER || def == ROCK || def == FIRE) ? 0.5 : 1;
                case WATER: return (def == FIRE || def == ROCK) ? 2 : (def == LEAF || def == WATER) ? 0.5 : 1;
                case LEAF: return (def == WATER || def == ROCK) ? 2 : (def == FIRE || def == LEAF) ? 0.5 : 1;
                case VOLT: return def == WATER ? 2 : (def == LEAF || def == ROCK || def == VOLT) ? 0.5 : 1;
                case ROCK: return (def == FIRE || def == VOLT) ? 2 : def == ROCK ? 0.5 : 1;
                default: return def == ROCK ? 0.5 : 1;
            }
        }

        public static readonly MoveDef[] Moves = {
            new MoveDef("Tackle", NORMAL, 35, 95),        // 0
            new MoveDef("Scratch", NORMAL, 40, 100),      // 1
            new MoveDef("Peck", NORMAL, 40, 100),         // 2
            new MoveDef("Gust", NORMAL, 55, 95),          // 3
            new MoveDef("Body Slam", NORMAL, 75, 90),     // 4
            new MoveDef("Ember", FIRE, 40, 100),          // 5
            new MoveDef("Flame Fang", FIRE, 65, 95),      // 6
            new MoveDef("Inferno", FIRE, 90, 80),         // 7
            new MoveDef("Bubble", WATER, 40, 100),        // 8
            new MoveDef("Aqua Jet", WATER, 60, 100),      // 9
            new MoveDef("Torrent", WATER, 90, 85),        // 10
            new MoveDef("Vine Lash", LEAF, 40, 100),      // 11
            new MoveDef("Leaf Blade", LEAF, 65, 95),      // 12
            new MoveDef("Solar Burst", LEAF, 90, 85),     // 13
            new MoveDef("Spark", VOLT, 40, 100),          // 14
            new MoveDef("Volt Bolt", VOLT, 65, 95),       // 15
            new MoveDef("Thunder", VOLT, 95, 75),         // 16
            new MoveDef("Pebble Toss", ROCK, 40, 95),     // 17
            new MoveDef("Rock Slide", ROCK, 65, 90),      // 18
            new MoveDef("Boulder Crush", ROCK, 90, 85),   // 19
            new MoveDef("Quick Jab", NORMAL, 30, 100),    // 20
        };

        static readonly int[] LearnFire = { 1, 1, 4, 5, 9, 20, 13, 6, 19, 4, 24, 7 };
        static readonly int[] LearnWater = { 1, 0, 4, 8, 9, 20, 13, 9, 19, 4, 24, 10 };
        static readonly int[] LearnLeaf = { 1, 0, 4, 11, 9, 20, 13, 12, 19, 4, 24, 13 };
        static readonly int[] LearnVolt = { 1, 20, 3, 14, 8, 1, 12, 15, 18, 4, 23, 16 };
        static readonly int[] LearnRock = { 1, 0, 4, 17, 10, 1, 14, 18, 20, 4, 25, 19 };
        static readonly int[] LearnBird = { 1, 2, 5, 20, 9, 3, 16, 4, 21, 15 };
        static readonly int[] LearnMite = { 1, 1, 3, 5, 8, 20, 12, 6, 18, 18, 23, 7 };
        static readonly int[] LearnFin = { 1, 0, 3, 8, 8, 14, 12, 9, 18, 15, 23, 10 };

        static SpeciesDef S(string name, int type, int hp, int atk, int def, int spd, int evoTo, int evoLv,
                            int[] learn, int sprite, bool big, int cr, int c1, int c2, int c3)
        {
            SpeciesDef s = new SpeciesDef();
            s.Name = name; s.Type = type; s.Hp = hp; s.Atk = atk; s.Def = def; s.Spd = spd;
            s.EvoTo = evoTo; s.EvoLevel = evoLv; s.Learn = learn; s.Sprite = sprite; s.Big = big;
            s.CatchRate = cr; s.Pal = new int[] { c1, c2, c3 };
            return s;
        }

        public static readonly SpeciesDef[] Species = {
            S("Emberpup",   FIRE,  45, 55, 40, 60,   1, 14, LearnFire,  0, false, 45, 0xF08030, 0xC05018, 0xFFD040), // 0
            S("Blazehound", FIRE,  75, 88, 65, 88,  -1,  0, LearnFire,  0, true,  25, 0xD83828, 0x881C14, 0xFFA020), // 1
            S("Driplet",    WATER, 50, 48, 55, 45,   3, 14, LearnWater, 1, false, 45, 0x58A8F0, 0x3070C0, 0xFFFFFF), // 2
            S("Tidalisk",   WATER, 85, 80, 82, 65,  -1,  0, LearnWater, 1, true,  25, 0x3858D0, 0x203090, 0x90E8FF), // 3
            S("Sproutle",   LEAF,  55, 50, 55, 40,   5, 14, LearnLeaf,  2, false, 45, 0x80D0B0, 0x4A9080, 0x38B030), // 4
            S("Thornox",    LEAF,  85, 82, 88, 55,  -1,  0, LearnLeaf,  2, true,  25, 0x4A9070, 0x2F6050, 0xE05090), // 5
            S("Zappit",     VOLT,  40, 52, 35, 78,   7, 16, LearnVolt,  3, false, 50, 0xF8D838, 0xC09820, 0x404040), // 6
            S("Voltail",    VOLT,  65, 88, 55,105,  -1,  0, LearnVolt,  3, true,  25, 0xFFA828, 0xC07010, 0x2040A0), // 7
            S("Pebblin",    ROCK,  50, 55, 72, 25,   9, 16, LearnRock,  4, false, 55, 0xB0A088, 0x786850, 0xE0D8C0), // 8
            S("Bouldon",    ROCK,  88, 92,108, 30,  -1,  0, LearnRock,  4, true,  25, 0x888078, 0x585048, 0xF0A030), // 9
            S("Puffowl",    NORMAL,45, 45, 40, 58,  11, 15, LearnBird,  5, false, 55, 0xC8A070, 0x907048, 0xF0A030), // 10
            S("Gustowl",    NORMAL,75, 78, 65, 88,  -1,  0, LearnBird,  5, true,  25, 0x8898B0, 0x586078, 0xF0C830), // 11
            S("Cindermite", FIRE,  38, 52, 40, 58,  13, 15, LearnMite,  6, false, 50, 0xE04838, 0x902828, 0xFFB020), // 12
            S("Magmantis",  FIRE,  68, 92, 60, 82,  -1,  0, LearnMite,  6, true,  25, 0x505058, 0x28282C, 0xFF6020), // 13
            S("Glimfin",    WATER, 40, 45, 42, 68,  15, 15, LearnFin,   7, false, 50, 0x60D0D0, 0x3090A0, 0xFFF080), // 14
            S("Lumiray",    WATER, 72, 72, 72, 88,  -1,  0, LearnFin,   7, true,  25, 0x9070E0, 0x5840A0, 0x80FFE0), // 15
        };

        public static readonly int[] Starters = { 0, 2, 4 };

        // 16x16 critter sprites. k=outline 1=main 2=shade 3=accent w=white e=eye .=clear. All face right.
        public static readonly string[][] Sprites = {
            new string[] { // 0 pup
                "................",
                "..k.....k.......",
                ".k1k...k1k......",
                ".k11kkk11k......",
                ".k1111111k...k..",
                "k11we111we1k.k3k",
                "k11ee111ee1kk33k",
                "k11111kk111kk3k.",
                ".k1w111111k.k3k.",
                "..kk2211111kk1k.",
                "..k222111111k1k.",
                "..k2221111111k..",
                "...k11111111k...",
                "...k1k.k1k.k1k..",
                "...kk..kk..kk...",
                "................" },
            new string[] { // 1 drop
                ".......kk.......",
                "......k1wk......",
                ".....k11w1k.....",
                ".....k1111k.....",
                "....k111111k....",
                "...k11111111k...",
                "...k1we11we1k...",
                "..k11ee11ee11k..",
                "..k1111111111k..",
                "..k111k33k111k..",
                "..k2111kk11112k.",
                "..k2211111122k..",
                "...k22222222k...",
                "....kkkkkkkk....",
                "................",
                "................" },
            new string[] { // 2 sprout
                ".......k3k......",
                "......k333k.....",
                ".....k33k33k....",
                "......kk3kk.....",
                "....kkkk3kkk....",
                "...k11111111k...",
                "..k111111we11k..",
                "..k1111111ee1k..",
                "..k11111111111k.",
                "..k211111kkk11k.",
                "..k2211111111k..",
                "...k22222222k...",
                "...k1k1kk1k1k...",
                "...kkkk..kkkk...",
                "................",
                "................" },
            new string[] { // 3 bunny
                "..k.....k.......",
                ".k3k...k3k......",
                ".k1k...k1k......",
                ".k1k...k1k......",
                ".k11k.k11k......",
                "k1111k1111k.....",
                "k111111we11k..k.",
                "k111111ee11k.k3k",
                "k1ww1111111kk33k",
                ".k1111111kkk33k.",
                "..k221111111k3k.",
                "..k2221111111k..",
                "...k1k...k1k....",
                "...kk....kk.....",
                "................",
                "................" },
            new string[] { // 4 rock
                "................",
                "................",
                "......kkkk......",
                "....kk3113kk....",
                "...k21111112k...",
                "..k2111111111k..",
                "..k211111we11k..",
                ".k2211111ee112k.",
                ".k2111111111k2k.",
                ".k21111kkkk112k.",
                ".k22111111111k..",
                "..k2221111222k..",
                "...kk222222kk...",
                ".....kkkkkk.....",
                "................",
                "................" },
            new string[] { // 5 owl
                "................",
                "....kkkkkk......",
                "...k111111k.....",
                "..k1ww11ww1k....",
                "..k1we11ew1k....",
                "..k11k33k11k....",
                "..k111kk1111k...",
                ".k2111111111k...",
                "k22211111111k...",
                "k2221111111k1k..",
                ".k22111111k11k..",
                "..k211111k11k...",
                "...kk1111kkk....",
                "....k3kk3k......",
                "....kk..kk......",
                "................" },
            new string[] { // 6 mite
                "...3.......3....",
                "...k3.....3k....",
                "....k.....k.....",
                "....kk...kk.....",
                "...k111111k.....",
                "..k111we1we1k...",
                "..k111ee1ee1k...",
                "..kk11kk11kk....",
                ".k22k2222k22k...",
                "k222k1111k222k..",
                "k22k111111k22k..",
                ".kk1k1111k1kk...",
                "..k.k3333k.k....",
                "....kkkkkk......",
                "....k.k..k.k....",
                "................" },
            new string[] { // 7 fish
                "............33..",
                "...........k33..",
                "..........k.....",
                ".......kkkk.....",
                ".....kk1111kk...",
                "k...k1111111wk..",
                "kk.k111111weekk.",
                "k1kk1111111eek..",
                "k11k22111111kk..",
                "k1kk2221111111k.",
                "kk.k2222111kkk..",
                "k...kk22111k....",
                "......kkkk......",
                "................",
                "................",
                "................" },
        };

        // 8x12 people. 3=cap s=skin b=shirt d=pants
        public static readonly string[] Person1 = {
            "..kkkk..",
            ".k3333k.",
            "k333333k",
            ".kssesk.",
            ".kssssk.",
            "..kbbk..",
            ".kbbbbk.",
            "kskbbksk",
            "..kbbk..",
            "..kddk..",
            ".kd..dk.",
            ".kk..kk." };
        public static readonly string[] Person2 = {
            "..kkkk..",
            ".k3333k.",
            "k333333k",
            ".kssesk.",
            ".kssssk.",
            "..kbbk..",
            ".kbbbbk.",
            "kskbbksk",
            "..kbbk..",
            "..kddk..",
            "..kddk..",
            "..kkkk.." };
        // cap, shirt, pants for: player, bug kid, hiker, ranger, champion, professor
        public static readonly int[][] Looks = {
            new int[] { 0xE03030, 0x3060D0, 0x303848 },
            new int[] { 0x40A040, 0xF0D040, 0x705030 },
            new int[] { 0x8A6A40, 0xC05030, 0x504030 },
            new int[] { 0x2E7D32, 0x80B060, 0x404830 },
            new int[] { 0x7030A0, 0x202030, 0x7030A0 },
            new int[] { 0xE0E0E0, 0xF0F0F0, 0x606070 },
        };

        public static readonly ZoneDef[] Zones = {
            Z("Pixelvale",    0,    300, 0, new int[0], 1, 1),
            Z("Breezy Path",  300,  770, 1, new int[] { 10, 40, 8, 35, 6, 15, 12, 10 }, 2, 5),
            Z("Misty Shore",  770,  1300, 2, new int[] { 14, 40, 2, 12, 10, 20, 6, 28 }, 5, 9),
            Z("Glowwood",     1300, 1870, 3, new int[] { 4, 25, 12, 25, 6, 20, 10, 20, 0, 10 }, 9, 13),
            Z("Ember Cave",   1870, 2460, 4, new int[] { 8, 30, 0, 20, 12, 20, 14, 15, 6, 15 }, 13, 17),
        };
        public const int WorldW = 2460;

        static ZoneDef Z(string n, int x0, int x1, int kind, int[] wild, int a, int b)
        {
            ZoneDef z = new ZoneDef(); z.Name = n; z.X0 = x0; z.X1 = x1; z.Kind = kind; z.Wild = wild; z.MinLv = a; z.MaxLv = b;
            return z;
        }

        public static readonly int[] Grass = {
            330, 420, 480, 600, 640, 735,
            810, 930, 990, 1120, 1170, 1245,
            1420, 1560, 1610, 1800,
            1950, 2120, 2170, 2340 };

        public static readonly BuildingDef[] Buildings = {
            new BuildingDef(40, 2), new BuildingDef(110, 0), new BuildingDef(200, 1),
            new BuildingDef(1325, 0), new BuildingDef(1380, 1), new BuildingDef(1905, 0),
        };

        public static readonly TrainerDef[] Trainers = {
            T("Bug Kid Tam", 750, 1, new int[] { 12, 5, 10, 6 },
              "Hey! My critters have been itching for a fight!", "Aww, my bugs got squished..."),
            T("Hiker Brom", 1270, 2, new int[] { 8, 9, 6, 9, 8, 10 },
              "These rocks are tougher than they look!", "Crumbled like gravel. Nice work!"),
            T("Ranger Ivy", 1850, 3, new int[] { 4, 13, 14, 13, 6, 14 },
              "The cave ahead is dangerous. Prove you're ready!", "You're ready. Mind the heat in there."),
            T("Champion Vex", 2420, 4, new int[] { 13, 18, 15, 18, 5, 19, 7, 20 },
              "So you made it to the end of the taskbar. Let's see what you've got!", "Incredible... you are the new Taskbar Champion!"),
        };

        static TrainerDef T(string n, int x, int look, int[] team, string intro, string lose)
        {
            TrainerDef t = new TrainerDef(); t.Name = n; t.X = x; t.Look = look; t.Team = team; t.Intro = intro; t.Lose = lose;
            t.Champion = look == 4;
            return t;
        }

        public static ZoneDef ZoneAt(float x)
        {
            for (int i = 0; i < Zones.Length; i++) if (x < Zones[i].X1) return Zones[i];
            return Zones[Zones.Length - 1];
        }

        public static bool InGrass(float x)
        {
            for (int i = 0; i < Grass.Length; i += 2) if (x >= Grass[i] && x <= Grass[i + 1]) return true;
            return false;
        }

        public static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static Color C(int rgb, int a) { return Color.FromArgb(a, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // Builds a bitmap from a character grid.
        public static Bitmap MakeSprite(string[] rows, int w, Dictionary<char, Color> pal, bool flip)
        {
            Bitmap b = new Bitmap(w, rows.Length, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = x < rows[y].Length ? rows[y][x] : '.';
                    Color c;
                    if (ch != '.' && pal.TryGetValue(ch, out c)) b.SetPixel(flip ? w - 1 - x : x, y, c);
                }
            return b;
        }
    }

    public class Mon
    {
        public int Sp, Level, Xp, Hp;
        public List<int> Moves = new List<int>();
        public float DispHp;
        public bool PendingEvo;

        public SpeciesDef Def { get { return Data.Species[Sp]; } }
        public string Name { get { return Def.Name; } }
        public int MaxHp { get { return Def.Hp * Level / 25 + Level + 10; } }
        public int Atk { get { return Def.Atk * Level / 40 + 5; } }
        public int DefS { get { return Def.Def * Level / 40 + 5; } }
        public int Spd { get { return Def.Spd * Level / 40 + 5; } }
        public int XpNeed { get { return 10 + Level * Level / 2 + Level * 4; } }

        public static Mon Create(int sp, int level)
        {
            // Wild/trainer critters above their evolve level show up evolved.
            while (Data.Species[sp].EvoTo >= 0 && level >= Data.Species[sp].EvoLevel) sp = Data.Species[sp].EvoTo;
            Mon m = new Mon(); m.Sp = sp; m.Level = level;
            int[] l = Data.Species[sp].Learn;
            for (int i = 0; i < l.Length; i += 2)
                if (l[i] <= level && !m.Moves.Contains(l[i + 1]))
                {
                    m.Moves.Add(l[i + 1]);
                    if (m.Moves.Count > 4) m.Moves.RemoveAt(0);
                }
            m.Hp = m.MaxHp; m.DispHp = m.Hp;
            return m;
        }

        public string Serialize()
        {
            string mv = "";
            for (int i = 0; i < Moves.Count; i++) mv += (i > 0 ? ";" : "") + Moves[i];
            return Sp + "," + Level + "," + Xp + "," + Hp + "," + mv;
        }

        public static Mon Parse(string s)
        {
            string[] p = s.Split(',');
            Mon m = new Mon();
            m.Sp = int.Parse(p[0]); m.Level = int.Parse(p[1]); m.Xp = int.Parse(p[2]); m.Hp = int.Parse(p[3]);
            if (m.Sp < 0 || m.Sp >= Data.Species.Length) throw new Exception("bad species");
            if (p.Length > 4 && p[4].Length > 0)
                foreach (string q in p[4].Split(';')) m.Moves.Add(int.Parse(q));
            if (m.Moves.Count == 0) m.Moves.Add(0);
            m.Hp = Math.Max(0, Math.Min(m.Hp, m.MaxHp));
            m.DispHp = m.Hp;
            return m;
        }
    }
}
// Taskbar Tamer: a tiny creature-catching game that lives in a strip above the Windows taskbar.
// Written in C# 5 so Windows PowerShell's built-in compiler can build it with no installs.

namespace TaskbarTamer
{
    public static class Launcher
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public static void Run()
        {
            try { SetProcessDPIAware(); } catch { }
            try { Application.EnableVisualStyles(); } catch { }
            Application.Run(new GameForm(false));
        }
    }

    public class GameForm : Form
    {
        // ---------- constants ----------
        public const int VW = 480, VH = 64;           // virtual canvas
        const int M_WORLD = 0, M_BATTLE = 1, M_STARTER = 2, M_TEAM = 3, M_SHOP = 4;
        const int PH_MENU = 0, PH_FIGHT = 1, PH_BAG = 2, PH_TEAM = 3;
        const float WalkSpeed = 60f;

        class Ev { public string Msg; public Action Act; }
        class TextCmd { public string S; public float X, Y, Size, W; public int Col; public int Align; }
        class Hit { public RectangleF R; public Action A; }

        // ---------- rendering ----------
        Bitmap low = new Bitmap(VW, VH);
        Bitmap miniBmp = new Bitmap(24, 24);
        Graphics lg;
        float s = 2f;
        List<TextCmd> texts = new List<TextCmd>();
        List<Hit> hits = new List<Hit>();
        Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        Dictionary<int, Font> fonts = new Dictionary<int, Font>();
        Dictionary<string, Bitmap> sprites = new Dictionary<string, Bitmap>();
        StringFormat fmt, fmtWrap;
        string fontName = "Segoe UI";

        // ---------- loop ----------
        Timer timer;
        Stopwatch clock = new Stopwatch();
        double lastT;
        float t;
        Random rng = new Random();
        float topmostT;
        bool topmostFailed;
        bool headless;
        string crashNote;

        // ---------- game state ----------
        int mode = M_WORLD;
        float px = 150; int facing = 1; float walkAnim; bool walking;
        float walkTarget = -1; bool pendingInteract;
        int coins = 30, orbs = 5, potions = 2, beaten, champWins;
        float healX = 110;
        List<Mon> party = new List<Mon>();
        List<Mon> box = new List<Mon>();
        float encCooldown;
        ZoneDef lastZone; float bannerT;
        float hintT = 45;

        List<Ev> evs = new List<Ev>();
        int ins = -1;
        string curMsg; float msgT;

        int cursor;
        int phase;
        bool forcedSwitch;

        // battle
        List<Mon> foes = new List<Mon>();
        int foeIdx, act;
        TrainerDef curTr; int curTrIdx;
        int battleKind;
        float pLunge, fLunge, pFlash, fFlash, pFaint, fFaint, fSlide, introT, orbT, evoFlash;
        bool foeHidden, caught;

        // window
        bool mini;
        float sizeFrac = 0.667f;
        Point? customPos, miniPos;
        bool dragging, dragMoved; Point dragStart, formStart;
        HashSet<Keys> held = new HashSet<Keys>();

        public static string SavePathOverride;

        string SavePath
        {
            get
            {
                if (SavePathOverride != null) return SavePathOverride;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarTamer");
                return Path.Combine(dir, "save.txt");
            }
        }

        Mon P { get { return party[act]; } }
        Mon F { get { return foes[foeIdx]; } }
        bool Idle { get { return curMsg == null && evs.Count == 0; } }

        // =====================================================================
        public GameForm(bool headless)
        {
            this.headless = headless;
            lg = Graphics.FromImage(low);
            lg.InterpolationMode = InterpolationMode.NearestNeighbor;
            lg.PixelOffsetMode = PixelOffsetMode.Half;
            lg.SmoothingMode = SmoothingMode.None;
            fmt = new StringFormat(StringFormat.GenericTypographic);
            fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            fmtWrap = new StringFormat(StringFormat.GenericTypographic);
            using (Font test = new Font(fontName, 10)) { if (test.Name != fontName) fontName = test.Name; }

            Text = "Taskbar Tamer";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            BuildContextMenu();
            Load2();
            ApplyLayout();

            if (party.Count == 0) NewGameIntro();
            lastZone = Data.ZoneAt(px);

            if (!headless)
            {
                clock.Start();
                timer = new Timer();
                timer.Interval = 30;
                timer.Tick += delegate { Tick(); };
                timer.Start();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x8; // tool window (no Alt+Tab, no taskbar button) + topmost
                return cp;
            }
        }

        // ---------- context menu ----------
        void BuildContextMenu()
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Minimise", null, delegate { SetMini(true); });
            ToolStripMenuItem size = new ToolStripMenuItem("Size");
            size.DropDownItems.Add("Small (half screen)", null, delegate { sizeFrac = 0.5f; ApplyLayout(); Save(); });
            size.DropDownItems.Add("Medium (2/3 screen)", null, delegate { sizeFrac = 0.667f; ApplyLayout(); Save(); });
            size.DropDownItems.Add("Large (most of the screen)", null, delegate { sizeFrac = 0.85f; ApplyLayout(); Save(); });
            cm.Items.Add(size);
            cm.Items.Add("Snap back above taskbar", null, delegate { customPos = null; miniPos = null; ApplyLayout(); Save(); });
            cm.Items.Add("How to play", null, delegate { ShowHelp(); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Start a new game...", null, delegate
            {
                if (MessageBox.Show("Erase your save and start over?", "Taskbar Tamer", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    ResetGame();
            });
            cm.Items.Add("Save && quit", null, delegate { Close(); });
            ContextMenuStrip = cm;
        }

        void ShowHelp()
        {
            if (!Idle) return;
            Say("Walk: Left/Right arrows, A/D, or click where to go.");
            Say("Z, Enter or Space: talk / confirm.  X or Esc: back.");
            Say("Tab: your team.  M: minimise.  Drag the dotted edge to move me.");
            Say("Right-click me for size, position and quit.");
        }

        // ---------- window layout ----------
        void ApplyLayout()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (customPos.HasValue)
            {
                Screen sc = Screen.FromPoint(customPos.Value);
                wa = sc.WorkingArea;
            }
            int w = (int)(wa.Width * sizeFrac);
            int h = (int)Math.Round(w * VH / (double)VW);
            s = w / (float)VW;
            int x = wa.Left + (wa.Width - w) / 2, y = wa.Bottom - h - 4;
            if (customPos.HasValue)
            {
                x = Math.Max(wa.Left - w + 60, Math.Min(customPos.Value.X, wa.Right - 60));
                y = Math.Max(wa.Top, Math.Min(customPos.Value.Y, wa.Bottom - 20));
            }
            if (mini)
            {
                int m = Math.Max(40, (int)(24 * s));
                int mx = x + w - m - 8, my = y + h - m;
                if (miniPos.HasValue) { mx = miniPos.Value.X; my = miniPos.Value.Y; }
                Bounds = new Rectangle(mx, my, m, m);
            }
            else Bounds = new Rectangle(x, y, w, h);
            Invalidate();
        }

        void SetMini(bool on)
        {
            mini = on;
            ApplyLayout();
        }

        // ---------- save / load ----------
        public void Save()
        {
            if (party.Count == 0) return;
            try
            {
                List<string> l = new List<string>();
                l.Add("v=1");
                l.Add("x=" + (int)px);
                l.Add("coins=" + coins); l.Add("orbs=" + orbs); l.Add("potions=" + potions);
                l.Add("beaten=" + beaten); l.Add("champ=" + champWins); l.Add("healx=" + (int)healX);
                l.Add("size=" + (int)(sizeFrac * 1000));
                if (customPos.HasValue) l.Add("pos=" + customPos.Value.X + "," + customPos.Value.Y);
                if (miniPos.HasValue) l.Add("minipos=" + miniPos.Value.X + "," + miniPos.Value.Y);
                foreach (Mon m in party) l.Add("mon=" + m.Serialize());
                foreach (Mon m in box) l.Add("box=" + m.Serialize());
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllLines(SavePath, l.ToArray());
            }
            catch { }
        }

        void Load2()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                foreach (string line in File.ReadAllLines(SavePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    switch (k)
                    {
                        case "x": px = int.Parse(v); break;
                        case "coins": coins = int.Parse(v); break;
                        case "orbs": orbs = int.Parse(v); break;
                        case "potions": potions = int.Parse(v); break;
                        case "beaten": beaten = int.Parse(v); break;
                        case "champ": champWins = int.Parse(v); break;
                        case "healx": healX = int.Parse(v); break;
                        case "size": sizeFrac = Math.Max(0.3f, Math.Min(1f, int.Parse(v) / 1000f)); break;
                        case "pos": { string[] p = v.Split(','); customPos = new Point(int.Parse(p[0]), int.Parse(p[1])); } break;
                        case "minipos": { string[] p = v.Split(','); miniPos = new Point(int.Parse(p[0]), int.Parse(p[1])); } break;
                        case "mon": party.Add(Mon.Parse(v)); break;
                        case "box": box.Add(Mon.Parse(v)); break;
                    }
                }
                px = Math.Max(6, Math.Min(Data.WorldW - 6, px));
                if (!AnyAlive()) HealAll();
            }
            catch
            {
                party.Clear(); box.Clear();
                px = 150; coins = 30; orbs = 5; potions = 2; beaten = 0; champWins = 0; healX = 110;
            }
        }

        void ResetGame()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
            party.Clear(); box.Clear(); evs.Clear(); curMsg = null;
            px = 150; coins = 30; orbs = 5; potions = 2; beaten = 0; champWins = 0; healX = 110;
            mode = M_WORLD; walkTarget = -1;
            NewGameIntro();
        }

        // ---------- events / messages ----------
        void Add(Ev e) { if (ins >= 0) evs.Insert(ins++, e); else evs.Add(e); }
        void Say(string m) { Ev e = new Ev(); e.Msg = m; Add(e); }
        void Do(Action a) { Ev e = new Ev(); e.Act = a; Add(e); }
        void SayDo(string m, Action a) { Ev e = new Ev(); e.Msg = m; e.Act = a; Add(e); }

        void Pump(float dt)
        {
            if (curMsg != null)
            {
                msgT += dt;
                if (msgT > 1.6f + curMsg.Length * 0.035f) curMsg = null;
            }
            int guard = 0;
            while (curMsg == null && evs.Count > 0 && guard++ < 200)
            {
                Ev e = evs[0];
                evs.RemoveAt(0);
                if (e.Act != null) { ins = 0; try { e.Act(); } finally { ins = -1; } }
                if (e.Msg != null) { curMsg = e.Msg; msgT = 0; }
            }
        }

        void AdvanceMsg()
        {
            if (curMsg != null && msgT > 0.12f) curMsg = null;
        }

        // ---------- main loop ----------
        void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, now - lastT);
            lastT = now;
            try { Step(dt); }
            catch (Exception ex)
            {
                if (crashNote == null)
                {
                    crashNote = ex.Message;
                    try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(SavePath), "error.log"), DateTime.Now + " " + ex + "\r\n"); } catch { }
                }
                evs.Clear(); curMsg = null; mode = M_WORLD; foeHidden = false;
            }
            topmostT += dt;
            if (topmostT > 3 && !topmostFailed && !headless)
            {
                topmostT = 0;
                try { SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
                catch { topmostFailed = true; }
            }
            Invalidate();
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public void Step(float dt)
        {
            t += dt;
            pLunge = Math.Max(0, pLunge - dt); fLunge = Math.Max(0, fLunge - dt);
            pFlash = Math.Max(0, pFlash - dt); fFlash = Math.Max(0, fFlash - dt);
            evoFlash = Math.Max(0, evoFlash - dt);
            fSlide = Math.Max(0, fSlide - dt * 2.5f);
            introT += dt;
            if (pFaint > 0) pFaint = Math.Min(1, pFaint + dt * 2.5f);
            if (fFaint > 0) fFaint = Math.Min(1, fFaint + dt * 2.5f);
            if (orbT > 0) orbT += dt;
            bannerT = Math.Max(0, bannerT - dt);
            if (hintT > 0 && mode == M_WORLD && party.Count > 0) hintT -= dt;
            foreach (Mon m in party) Drain(m, dt);
            foreach (Mon m in foes) Drain(m, dt);

            Pump(dt);
            if (mode == M_WORLD && Idle) UpdateWorld(dt);
            else walking = false;
        }

        void Drain(Mon m, float dt)
        {
            float rate = Math.Max(20, m.MaxHp * 1.2f) * dt;
            if (Math.Abs(m.DispHp - m.Hp) <= rate) m.DispHp = m.Hp;
            else m.DispHp += m.DispHp < m.Hp ? rate : -rate;
        }

        // =====================================================================
        // WORLD
        // =====================================================================
        void NewGameIntro()
        {
            px = 150; facing = 1; hintT = 45;
            Say("Welcome to Pixelvale, the little town on your taskbar!");
            Say("I'm Professor Byte. Wild critters hide in the tall grass.");
            Say("You'll need a partner. Pick one!");
            Do(delegate { mode = M_STARTER; cursor = 0; });
        }

        void PickStarter(int i)
        {
            Mon m = Mon.Create(Data.Starters[i], 5);
            party.Add(m);
            mode = M_WORLD;
            Say("You chose " + m.Name + "! Take good care of it.");
            Say("Here are 5 Orbs for catching critters and 2 Potions.");
            Say("Walk with the arrow keys (or click). Z talks, Tab shows your team.");
            Say("Beat the trainers to the east, then the Champion. Good luck!");
            Do(Save);
        }

        bool Beaten(int i) { return (beaten & (1 << i)) != 0; }
        bool AnyAlive() { foreach (Mon m in party) if (m.Hp > 0) return true; return false; }
        void HealAll() { foreach (Mon m in party) { m.Hp = m.MaxHp; m.DispHp = m.Hp; } }

        bool Held(params Keys[] ks) { foreach (Keys k in ks) if (held.Contains(k)) return true; return false; }

        void UpdateWorld(float dt)
        {
            if (party.Count == 0) { walking = false; return; }
            int dir = 0;
            if (Held(Keys.Left, Keys.A)) { dir = -1; walkTarget = -1; }
            else if (Held(Keys.Right, Keys.D)) { dir = 1; walkTarget = -1; }
            else if (walkTarget >= 0)
            {
                if (Math.Abs(walkTarget - px) < 1.5f)
                {
                    walkTarget = -1;
                    if (pendingInteract) { pendingInteract = false; Interact(); return; }
                }
                else dir = walkTarget > px ? 1 : -1;
            }
            float old = px;
            if (dir != 0)
            {
                facing = dir;
                px += dir * WalkSpeed * dt;
                if (walkTarget >= 0 && (dir > 0 ? px > walkTarget : px < walkTarget)) px = walkTarget;
            }
            px = Math.Max(6, Math.Min(Data.WorldW - 6, px));

            // trainers block the way until beaten
            for (int i = 0; i < Data.Trainers.Length; i++)
            {
                TrainerDef tr = Data.Trainers[i];
                if (Beaten(i)) continue;
                if (px > tr.X - 14)
                {
                    px = tr.X - 14; walkTarget = -1; pendingInteract = false; walking = false; facing = 1;
                    StartTrainer(i);
                    return;
                }
            }

            walking = Math.Abs(px - old) > 0.001f;
            if (walking) walkAnim += dt;

            ZoneDef z = Data.ZoneAt(px);
            if (z != lastZone) { lastZone = z; bannerT = 2.5f; }

            encCooldown -= dt;
            if (walking && encCooldown <= 0 && z.Wild.Length > 0 && Data.InGrass(px) && AnyAlive())
            {
                if (rng.NextDouble() < Math.Abs(px - old) / 140.0) StartWild(z);
            }
        }

        // Things you can talk to: buildings, the professor, the champion (for rematches).
        int NearBuilding()
        {
            for (int i = 0; i < Data.Buildings.Length; i++)
                if (Math.Abs(px - Data.Buildings[i].X) < 14) return i;
            return -1;
        }

        bool NearChampion()
        {
            int ci = Data.Trainers.Length - 1;
            return Beaten(ci) && Math.Abs(px - (Data.Trainers[ci].X - 14)) < 8;
        }

        void Interact()
        {
            if (party.Count == 0) return;
            if (NearChampion()) { StartTrainer(Data.Trainers.Length - 1); return; }
            int b = NearBuilding();
            if (b < 0) return;
            BuildingDef bd = Data.Buildings[b];
            if (bd.Kind == 0)
            {
                Say("Welcome to the Rest Hut! Let your critters take a nap...");
                SayDo("Your team is fully healed!", delegate { HealAll(); healX = bd.X; });
                Do(Save);
            }
            else if (bd.Kind == 1) { mode = M_SHOP; cursor = 0; }
            else
            {
                string[] notes = {
                    "Your room. A note says: \"Be home before the battery dies!\"",
                    "Your bed looks comfy. No time for naps, there are critters out there!",
                    "Your PC is here, but it's covered in stickers." };
                Say(notes[rng.Next(notes.Length)]);
            }
        }

        void StartWild(ZoneDef z)
        {
            int total = 0;
            for (int i = 1; i < z.Wild.Length; i += 2) total += z.Wild[i];
            int r = rng.Next(total), sp = z.Wild[0];
            for (int i = 0; i < z.Wild.Length; i += 2) { r -= z.Wild[i + 1]; if (r < 0) { sp = z.Wild[i]; break; } }
            int lv = rng.Next(z.MinLv, z.MaxLv + 1);
            foes = new List<Mon>(); foes.Add(Mon.Create(sp, lv));
            curTr = null;
            BeginBattle();
        }

        void StartTrainer(int i)
        {
            TrainerDef tr = Data.Trainers[i];
            int bonus = tr.Champion ? champWins * 5 : 0;
            Say(tr.Name + ": \"" + (tr.Champion && champWins > 0 ? "Back for more? I've been training too!" : tr.Intro) + "\"");
            Do(delegate
            {
                foes = new List<Mon>();
                for (int k = 0; k < tr.Team.Length; k += 2) foes.Add(Mon.Create(tr.Team[k], Math.Min(50, tr.Team[k + 1] + bonus)));
                curTr = tr; curTrIdx = i;
                BeginBattle();
            });
        }

        // =====================================================================
        // BATTLE
        // =====================================================================
        void BeginBattle()
        {
            mode = M_BATTLE;
            battleKind = Data.ZoneAt(px).Kind;
            foeIdx = 0; act = 0;
            for (int i = 0; i < party.Count; i++) if (party[i].Hp > 0) { act = i; break; }
            phase = PH_MENU; cursor = 0; forcedSwitch = false;
            introT = 0; pFaint = 0; fFaint = 0; fSlide = 1; orbT = 0; foeHidden = false; caught = false;
            walkTarget = -1; pendingInteract = false;
            held.Clear();
            if (curTr == null) Say("A wild " + F.Name + " appeared!");
            else Say(curTr.Name + " sent out " + F.Name + "!");
            Say("Go, " + P.Name + "!");
        }

        string FoeName(Mon m) { return (curTr == null ? "Wild " : "Foe ") + m.Name; }

        int FoeChooseMove()
        {
            Mon f = F, p = P;
            int best = 0; double bestScore = -1;
            for (int i = 0; i < f.Moves.Count; i++)
            {
                MoveDef mv = Data.Moves[f.Moves[i]];
                double sc = mv.Power * Data.Eff(mv.Type, p.Def.Type) * (mv.Type == f.Def.Type ? 1.5 : 1) * mv.Acc / 100.0;
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            if (rng.NextDouble() < 0.35) best = rng.Next(f.Moves.Count);
            return f.Moves[best];
        }

        void EndTurn() { phase = PH_MENU; }

        // moveId -1 means "let the foe pick when it's its turn"
        void Attack(bool byPlayer, int moveId, Action next)
        {
            Do(delegate
            {
                if (mode != M_BATTLE) return;
                Mon a = byPlayer ? P : F, d = byPlayer ? F : P;
                if (a.Hp <= 0 || d.Hp <= 0) { if (next != null) next(); return; }
                int mid = moveId < 0 ? FoeChooseMove() : moveId;
                MoveDef mv = Data.Moves[mid];
                string who = byPlayer ? a.Name : FoeName(a);
                SayDo(who + " used " + mv.Name + "!", delegate
                {
                    if (byPlayer) pLunge = 0.35f; else fLunge = 0.35f;
                    if (rng.Next(100) >= mv.Acc)
                    {
                        Say("But it missed!");
                        if (next != null) Do(next);
                        return;
                    }
                    double eff = Data.Eff(mv.Type, d.Def.Type);
                    bool crit = rng.Next(16) == 0;
                    double dmg = ((2.0 * a.Level / 5 + 2) * mv.Power * a.Atk / (double)d.DefS) / 50.0 + 2;
                    if (mv.Type == a.Def.Type) dmg *= 1.5;
                    dmg *= eff;
                    if (crit) dmg *= 1.5;
                    dmg *= 0.85 + rng.NextDouble() * 0.15;
                    d.Hp = Math.Max(0, d.Hp - Math.Max(1, (int)dmg));
                    if (byPlayer) fFlash = 0.5f; else pFlash = 0.5f;
                    if (crit) Say("A critical hit!");
                    if (eff > 1) Say("It's super effective!");
                    else if (eff < 1) Say("It's not very effective...");
                    if (d.Hp <= 0) Do(delegate { Fainted(!byPlayer); });
                    else if (next != null) Do(next);
                });
            });
        }

        void Fainted(bool playerSide)
        {
            if (!playerSide)
            {
                Mon f = F;
                SayDo(FoeName(f) + " fainted!", delegate { fFaint = 0.001f; });
                Do(delegate
                {
                    int gain = 6 + f.Level * 4;
                    if (curTr != null) gain = gain * 3 / 2;
                    for (int i = 0; i < party.Count; i++)
                    {
                        Mon m = party[i];
                        if (m.Hp <= 0) continue;
                        int g = i == act ? gain : Math.Max(1, gain / 3);
                        if (i == act) Say(m.Name + " gained " + g + " XP!");
                        GainXp(m, g);
                    }
                    if (curTr == null)
                    {
                        int c = 2 + f.Level;
                        coins += c;
                        Say("You found " + c + " coins!");
                    }
                    Do(AfterFoeDown);
                });
            }
            else
            {
                SayDo(P.Name + " fainted!", delegate { pFaint = 0.001f; });
                Do(delegate
                {
                    if (AnyAlive())
                    {
                        phase = PH_TEAM; forcedSwitch = true; cursor = 0;
                        for (int i = 0; i < party.Count; i++) if (party[i].Hp > 0) { cursor = i; break; }
                    }
                    else
                    {
                        Say("You have no critters left that can fight!");
                        Say("You hurried back to the Rest Hut and dropped some coins...");
                        Do(Blackout);
                    }
                });
            }
        }

        void AfterFoeDown()
        {
            if (curTr != null && foeIdx + 1 < foes.Count)
            {
                SayDo(curTr.Name + " sent out " + foes[foeIdx + 1].Name + "!", delegate { foeIdx++; fFaint = 0; fSlide = 1; });
                Do(delegate { phase = PH_MENU; cursor = 0; });
            }
            else if (curTr != null)
            {
                TrainerDef tr = curTr;
                int maxLv = 0; foreach (Mon m in foes) maxLv = Math.Max(maxLv, m.Level);
                int prize = maxLv * 15;
                coins += prize;
                bool firstChamp = tr.Champion && !Beaten(curTrIdx);
                beaten |= 1 << curTrIdx;
                if (tr.Champion) champWins++;
                Say("You beat " + tr.Name + "!");
                Say(tr.Name + ": \"" + tr.Lose + "\"");
                Say("You got " + prize + " coins for winning!");
                if (firstChamp)
                {
                    Say("You conquered the whole taskbar! Amazing!");
                    Say("Talk to the Champion any time for a tougher rematch.");
                }
                EndBattle();
            }
            else EndBattle();
        }

        void GainXp(Mon m, int g)
        {
            m.Xp += g;
            while (m.Xp >= m.XpNeed && m.Level < 50)
            {
                m.Xp -= m.XpNeed;
                int oldMax = m.MaxHp;
                m.Level++;
                if (m.Hp > 0) m.Hp += m.MaxHp - oldMax;
                Say(m.Name + " grew to Lv " + m.Level + "!");
                int[] l = m.Def.Learn;
                for (int i = 0; i < l.Length; i += 2) if (l[i] == m.Level) LearnMove(m, l[i + 1]);
                if (m.Def.EvoTo >= 0 && m.Level >= m.Def.EvoLevel) m.PendingEvo = true;
            }
        }

        void LearnMove(Mon m, int mv)
        {
            if (m.Moves.Contains(mv)) return;
            if (m.Moves.Count < 4)
            {
                m.Moves.Add(mv);
                Say(m.Name + " learned " + Data.Moves[mv].Name + "!");
                return;
            }
            int weakest = 0;
            for (int i = 1; i < m.Moves.Count; i++)
                if (Data.Moves[m.Moves[i]].Power < Data.Moves[m.Moves[weakest]].Power) weakest = i;
            string old = Data.Moves[m.Moves[weakest]].Name;
            m.Moves[weakest] = mv;
            Say(m.Name + " forgot " + old + " and learned " + Data.Moves[mv].Name + "!");
        }

        void EndBattle()
        {
            foreach (Mon m0 in party)
            {
                Mon m = m0;
                if (!m.PendingEvo) continue;
                m.PendingEvo = false;
                if (m.Def.EvoTo < 0) continue;
                string oldName = m.Name;
                int to = m.Def.EvoTo;
                Say("What? " + oldName + " is evolving!");
                SayDo(oldName + " evolved into " + Data.Species[to].Name + "!", delegate
                {
                    int oldMax = m.MaxHp;
                    m.Sp = to;
                    if (m.Hp > 0) m.Hp += m.MaxHp - oldMax;
                    m.DispHp = m.Hp;
                    evoFlash = 1;
                });
            }
            Do(delegate
            {
                mode = M_WORLD; encCooldown = 2.5f; foeHidden = false; orbT = 0;
                foreach (Mon m in party) m.DispHp = m.Hp;
                Save();
            });
        }

        void Blackout()
        {
            HealAll();
            px = healX; coins /= 2; mode = M_WORLD; walkTarget = -1; encCooldown = 3;
            foeHidden = false; orbT = 0;
            lastZone = Data.ZoneAt(px);
            Save();
        }

        // ----- battle menu actions -----
        void BattlePick(int i)
        {
            if (!Idle) return;
            if (phase == PH_MENU)
            {
                if (i == 0) { phase = PH_FIGHT; cursor = 0; }
                else if (i == 1) { phase = PH_BAG; cursor = 0; }
                else if (i == 2) { phase = PH_TEAM; cursor = act; forcedSwitch = false; }
                else if (i == 3) Run();
            }
            else if (phase == PH_FIGHT)
            {
                if (i >= P.Moves.Count) return;
                UseMove(i);
            }
            else if (phase == PH_BAG)
            {
                if (i == 0) ThrowOrb();
                else if (i == 1) UsePotion();
                else { phase = PH_MENU; cursor = 1; }
            }
            else if (phase == PH_TEAM)
            {
                if (i >= party.Count) { if (!forcedSwitch) { phase = PH_MENU; cursor = 2; } return; }
                SwitchTo(i);
            }
        }

        void BattleBack()
        {
            if (!Idle) return;
            if (phase == PH_TEAM && forcedSwitch) return;
            if (phase != PH_MENU) { cursor = phase == PH_FIGHT ? 0 : phase == PH_BAG ? 1 : 2; phase = PH_MENU; }
        }

        void UseMove(int i)
        {
            phase = PH_MENU;
            int pm = P.Moves[i];
            bool first = P.Spd > F.Spd || (P.Spd == F.Spd && rng.Next(2) == 0);
            if (first) Attack(true, pm, delegate { Attack(false, -1, EndTurn); });
            else Attack(false, -1, delegate { Attack(true, pm, EndTurn); });
        }

        void ThrowOrb()
        {
            phase = PH_MENU;
            if (curTr != null) { Say("You can't catch another trainer's critter!"); return; }
            if (orbs <= 0) { Say("You're out of Orbs! Buy more at a shop."); return; }
            orbs--;
            Mon f = F;
            double hpf = f.Hp / (double)f.MaxHp;
            double ch = f.Def.CatchRate / 100.0 * (1.6 - 1.2 * hpf);
            if (P.Level > f.Level) ch *= 1.15;
            ch = Math.Max(0.05, Math.Min(0.95, ch));
            bool ok = rng.NextDouble() < ch;
            int shakes = ok ? 3 : rng.Next(3);
            SayDo("You threw an Orb!", delegate { orbT = 0.001f; foeHidden = true; });
            string[] wob = { "...", "Wobble...", "Wobble... wobble...", "Wobble... wobble... wobble..." };
            Say(wob[shakes]);
            if (ok)
            {
                SayDo("Gotcha! " + f.Name + " was caught!", delegate { caught = true; });
                Do(delegate
                {
                    f.PendingEvo = false;
                    if (party.Count < 6) { party.Add(f); Say(f.Name + " joined your team!"); }
                    else { box.Add(f); Say("Your team is full, so " + f.Name + " went to storage."); }
                    EndBattle();
                });
            }
            else
            {
                SayDo("Argh! It broke free!", delegate { foeHidden = false; orbT = 0; });
                Attack(false, -1, EndTurn);
            }
        }

        void UsePotion()
        {
            phase = PH_MENU;
            if (potions <= 0) { Say("You don't have any Potions."); return; }
            Mon m = P;
            if (m.Hp >= m.MaxHp) { Say(m.Name + " already has full HP!"); return; }
            potions--;
            SayDo("You used a Potion on " + m.Name + "!", delegate { m.Hp = Math.Min(m.MaxHp, m.Hp + 30); });
            Attack(false, -1, EndTurn);
        }

        void SwitchTo(int i)
        {
            Mon m = party[i];
            if (m.Hp <= 0) { Say(m.Name + " is too tired to battle!"); return; }
            if (i == act && !forcedSwitch) { Say(m.Name + " is already out there!"); return; }
            bool forced = forcedSwitch;
            forcedSwitch = false;
            phase = PH_MENU; cursor = 0;
            if (!forced) Say("Come back, " + P.Name + "!");
            SayDo("Go, " + m.Name + "!", delegate { act = i; pFaint = 0; });
            if (!forced) Attack(false, -1, EndTurn);
        }

        void Run()
        {
            if (curTr != null) { Say("You can't run from a trainer battle!"); return; }
            if (P.Spd >= F.Spd || rng.NextDouble() < 0.5) { Say("You got away safely!"); Do(EndBattle); }
            else { Say("Couldn't get away!"); Attack(false, -1, EndTurn); }
        }

        // ---------- team & shop ----------
        void OpenTeam() { if (party.Count == 0) return; mode = M_TEAM; cursor = 0; }

        void TeamLead()
        {
            if (cursor <= 0 || cursor >= party.Count) return;
            Mon m = party[cursor]; party.RemoveAt(cursor); party.Insert(0, m); cursor = 0;
            Save();
        }

        void TeamStorage()
        {
            if (box.Count == 0) { Say("Storage is empty. Catch more critters!"); return; }
            if (cursor >= party.Count) return;
            Mon fromBox = box[0];
            box.RemoveAt(0);
            fromBox.Hp = fromBox.MaxHp; fromBox.DispHp = fromBox.Hp;
            box.Add(party[cursor]);
            party[cursor] = fromBox;
            Say(fromBox.Name + " came out of storage!");
            Save();
        }

        void ShopPick(int i)
        {
            if (!Idle) return;
            if (i == 0) { if (coins >= 10) { coins -= 10; orbs++; Say("Bought an Orb!"); } else Say("You need 10 coins for that."); }
            else if (i == 1) { if (coins >= 15) { coins -= 15; potions++; Say("Bought a Potion!"); } else Say("You need 15 coins for that."); }
            else { mode = M_WORLD; Say("Thanks for shopping. Come again!"); Do(Save); }
        }

        // =====================================================================
        // INPUT
        // =====================================================================
        protected override bool IsInputKey(Keys keyData) { return true; }

        static bool IsLeft(Keys k) { return k == Keys.Left || k == Keys.A; }
        static bool IsRight(Keys k) { return k == Keys.Right || k == Keys.D; }
        static bool IsUp(Keys k) { return k == Keys.Up || k == Keys.W; }
        static bool IsDown(Keys k) { return k == Keys.Down || k == Keys.S; }
        static bool IsOk(Keys k) { return k == Keys.Z || k == Keys.Enter || k == Keys.Space || k == Keys.E; }
        static bool IsBack(Keys k) { return k == Keys.X || k == Keys.Escape || k == Keys.Back || k == Keys.Q; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool repeat = held.Contains(e.KeyCode);
            held.Add(e.KeyCode);
            KeyPress2(e.KeyCode, repeat);
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); held.Remove(e.KeyCode); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); held.Clear(); }

        public void SimHold(Keys k, bool down) { if (down) { bool r = held.Contains(k); held.Add(k); KeyPress2(k, r); } else held.Remove(k); }

        public void KeyPress2(Keys k, bool repeat)
        {
            if (k == Keys.M) { SetMini(!mini); return; }
            if (mini) { if (IsOk(k)) SetMini(false); return; }
            if (k == Keys.F1 || k == Keys.H) { ShowHelp(); return; }
            if (curMsg != null || evs.Count > 0)
            {
                if (!repeat && (IsOk(k) || IsBack(k))) AdvanceMsg();
                return;
            }
            switch (mode)
            {
                case M_WORLD:
                    if ((IsOk(k) || IsUp(k)) && !repeat) Interact();
                    else if (k == Keys.Tab || k == Keys.T) OpenTeam();
                    break;
                case M_STARTER:
                    if (IsLeft(k)) cursor = (cursor + 2) % 3;
                    else if (IsRight(k)) cursor = (cursor + 1) % 3;
                    else if (IsOk(k) && !repeat) PickStarter(cursor);
                    break;
                case M_TEAM:
                    if (IsLeft(k)) cursor = Math.Max(0, cursor - 1);
                    else if (IsRight(k)) cursor = Math.Min(party.Count - 1, cursor + 1);
                    else if (IsOk(k) && !repeat) TeamLead();
                    else if (k == Keys.B && !repeat) TeamStorage();
                    else if (IsBack(k) || k == Keys.Tab || k == Keys.T) mode = M_WORLD;
                    break;
                case M_SHOP:
                    if (IsLeft(k) || IsUp(k)) cursor = Math.Max(0, cursor - 1);
                    else if (IsRight(k) || IsDown(k)) cursor = Math.Min(2, cursor + 1);
                    else if (IsOk(k) && !repeat) ShopPick(cursor);
                    else if (IsBack(k)) ShopPick(2);
                    break;
                case M_BATTLE:
                    {
                        int count, cols;
                        BattleGrid(out count, out cols);
                        if (IsLeft(k)) { if (cursor % cols > 0) cursor--; }
                        else if (IsRight(k)) { if (cursor % cols < cols - 1 && cursor + 1 < count) cursor++; }
                        else if (IsUp(k)) { if (cursor - cols >= 0) cursor -= cols; }
                        else if (IsDown(k)) { if (cursor + cols < count) cursor += cols; }
                        else if (IsOk(k) && !repeat) BattlePick(cursor);
                        else if (IsBack(k)) BattleBack();
                    }
                    break;
            }
        }

        void BattleGrid(out int count, out int cols)
        {
            if (phase == PH_MENU) { count = 4; cols = 2; }
            else if (phase == PH_FIGHT) { count = P.Moves.Count; cols = 2; }
            else if (phase == PH_BAG) { count = 3; cols = 3; }
            else { count = party.Count + (forcedSwitch ? 0 : 1); cols = 4; }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            float vx = e.X / s, vy = e.Y / s;
            if (mini || vx < 7 || ModifierKeys == Keys.Alt)
            {
                dragging = true; dragMoved = false;
                dragStart = Cursor.Position; formStart = Location;
                return;
            }
            Click2(vx, vy);
        }

        public void Click2(float vx, float vy)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].R.Contains(vx, vy)) { hits[i].A(); return; }
            if (curMsg != null) { AdvanceMsg(); return; }
            if (mode == M_WORLD && Idle && party.Count > 0)
            {
                float wx = Cam() + vx;
                pendingInteract = false;
                for (int i = 0; i < Data.Buildings.Length; i++)
                    if (Math.Abs(wx - Data.Buildings[i].X) < 18 && vy > 8) { wx = Data.Buildings[i].X; pendingInteract = true; }
                int ci = Data.Trainers.Length - 1;
                if (Beaten(ci) && Math.Abs(wx - Data.Trainers[ci].X) < 10) { wx = Data.Trainers[ci].X - 14; pendingInteract = true; }
                walkTarget = Math.Max(6, Math.Min(Data.WorldW - 6, wx));
                if (pendingInteract && Math.Abs(walkTarget - px) < 1.5f) { walkTarget = -1; pendingInteract = false; Interact(); }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point c = Cursor.Position;
            int dx = c.X - dragStart.X, dy = c.Y - dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) dragMoved = true;
            if (dragMoved) Location = new Point(formStart.X + dx, formStart.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            if (mini)
            {
                if (dragMoved) { miniPos = Location; Save(); }
                else SetMini(false);
            }
            else if (dragMoved) { customPos = Location; Save(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode == M_WORLD || mode == M_TEAM || mode == M_SHOP) Save();
            else if (party.Count > 0)
            {
                // quitting mid-battle: keep progress from before the fight
                foreach (Mon m in party) if (m.Hp <= 0) m.Hp = 1;
                Save();
            }
            base.OnFormClosing(e);
        }

        // =====================================================================
        // DRAWING HELPERS
        // =====================================================================
        SolidBrush Br(int rgb, int a = 255)
        {
            int key = (a << 24) | (rgb & 0xFFFFFF);
            SolidBrush b;
            if (!brushes.TryGetValue(key, out b)) { b = new SolidBrush(Data.C(rgb, a)); brushes[key] = b; }
            return b;
        }

        void R(float x, float y, float w, float h, int rgb, int a = 255) { lg.FillRectangle(Br(rgb, a), x, y, w, h); }

        void Tri(float x1, float y1, float x2, float y2, float x3, float y3, int rgb)
        {
            lg.FillPolygon(Br(rgb), new PointF[] { new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3) });
        }

        void Txt(string str, float x, float y, float size, int col, int align = 0)
        {
            TextCmd c = new TextCmd(); c.S = str; c.X = x; c.Y = y; c.Size = size; c.Col = col; c.Align = align;
            texts.Add(c);
        }

        void TxtWrap(string str, float x, float y, float w, float size, int col)
        {
            TextCmd c = new TextCmd(); c.S = str; c.X = x; c.Y = y; c.W = w; c.Size = size; c.Col = col;
            texts.Add(c);
        }

        // Hides text queued earlier that sits under a panel drawn on top of it.
        void Cover(float x, float y, float w, float h)
        {
            texts.RemoveAll(delegate(TextCmd c) { return c.X >= x - 2 && c.X <= x + w && c.Y >= y - 4 && c.Y <= y + h; });
        }

        void AddHit(RectangleF r, Action a) { Hit h = new Hit(); h.R = r; h.A = a; hits.Add(h); }

        void PanelBox(float x, float y, float w, float h, int fill, int border)
        {
            R(x + 1, y, w - 2, h, border);
            R(x, y + 1, w, h - 2, border);
            R(x + 1, y + 1, w - 2, h - 2, fill);
        }

        void HpBar(float x, float y, float w, float cur, float max)
        {
            float f = max <= 0 ? 0 : Math.Max(0, Math.Min(1, cur / max));
            R(x - 1, y - 1, w + 2, 4, 0x303040);
            R(x, y, w, 2, 0x585868);
            int col = f > 0.5f ? 0x48D058 : f > 0.2f ? 0xF0C030 : 0xE84030;
            R(x, y, w * f, 2, col);
        }

        Bitmap MonSprite(int sp, bool flip)
        {
            string key = "m" + sp + (flip ? "f" : "");
            Bitmap b;
            if (sprites.TryGetValue(key, out b)) return b;
            SpeciesDef d = Data.Species[sp];
            Dictionary<char, Color> pal = new Dictionary<char, Color>();
            pal['k'] = Data.C(0x181820); pal['w'] = Data.C(0xFFFFFF); pal['e'] = Data.C(0x101018);
            pal['1'] = Data.C(d.Pal[0]); pal['2'] = Data.C(d.Pal[1]); pal['3'] = Data.C(d.Pal[2]);
            b = Data.MakeSprite(Data.Sprites[d.Sprite], 16, pal, flip);
            sprites[key] = b;
            return b;
        }

        Bitmap PersonSprite(int look, int frame, bool flip)
        {
            string key = "p" + look + "_" + frame + (flip ? "f" : "");
            Bitmap b;
            if (sprites.TryGetValue(key, out b)) return b;
            int[] lk = Data.Looks[look];
            Dictionary<char, Color> pal = new Dictionary<char, Color>();
            pal['k'] = Data.C(0x181820); pal['e'] = Data.C(0x181820); pal['w'] = Data.C(0xFFFFFF);
            pal['s'] = Data.C(0xF8C8A0); pal['3'] = Data.C(lk[0]); pal['b'] = Data.C(lk[1]); pal['d'] = Data.C(lk[2]);
            b = Data.MakeSprite(frame == 0 ? Data.Person1 : Data.Person2, 8, pal, flip);
            sprites[key] = b;
            return b;
        }

        void Blit(Bitmap b, float x, float y, float w, float h)
        {
            lg.DrawImage(b, new RectangleF((float)Math.Round(x), (float)Math.Round(y), w, h));
        }

        float Cam() { return Math.Max(0, Math.Min(Data.WorldW - VW, px - VW / 2)); }

        // 0 day, 1 sunset/sunrise, 2 night
        int TimeOfDay()
        {
            int h = DateTime.Now.Hour;
            if (h >= 20 || h < 6) return 2;
            if (h >= 17 || h < 8) return 1;
            return 0;
        }

        // =====================================================================
        // RENDER
        // =====================================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            RenderTo(e.Graphics, ClientSize.Width, ClientSize.Height);
        }

        public void RenderTo(Graphics g, int w, int h)
        {
            texts.Clear();
            hits.Clear();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (mini)
            {
                DrawMini();
                g.DrawImage(miniBmp, new Rectangle(0, 0, w, h));
                return;
            }
            float sc = w / (float)VW;
            lg.Clear(Color.Black);
            try
            {
                if (mode == M_BATTLE) DrawBattle();
                else
                {
                    DrawWorld();
                    if (mode == M_STARTER) DrawStarter();
                    else if (mode == M_TEAM) DrawTeam();
                    else if (mode == M_SHOP) DrawShop();
                    if (curMsg != null) DrawMsgBox(110, 15, 260, 29);
                }
                DrawChrome();
            }
            catch (Exception ex) { Txt("Draw error: " + ex.Message, 10, 20, 6, 0xFFFFFF); }

            g.DrawImage(low, new Rectangle(0, 0, w, h));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            foreach (TextCmd c in texts)
            {
                Font f = GetFont(c.Size * sc);
                SolidBrush b = Br(c.Col);
                if (c.W > 0)
                {
                    g.DrawString(c.S, f, b, new RectangleF(c.X * sc, c.Y * sc, c.W * sc, VH * sc), fmtWrap);
                    continue;
                }
                float x = c.X * sc;
                if (c.Align != 0)
                {
                    SizeF sz = g.MeasureString(c.S, f, PointF.Empty, fmt);
                    x -= c.Align == 1 ? sz.Width / 2 : sz.Width;
                }
                g.DrawString(c.S, f, b, x, c.Y * sc, fmt);
            }
        }

        Font GetFont(float px)
        {
            int key = (int)(px * 4);
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(fontName, Math.Max(4f, key / 4f), FontStyle.Bold, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        void DrawChrome()
        {
            // drag grip on the left edge
            R(0, 0, 6, VH, 0x202030, 200);
            for (int y = 8; y < VH - 6; y += 4) { R(2, y, 1, 1, 0xA0A0B8); R(4, y + 2, 1, 1, 0xA0A0B8); }
            // minimise and close buttons
            RectangleF mn = new RectangleF(457, 2, 10, 9), cl = new RectangleF(468, 2, 10, 9);
            PanelBox(mn.X, mn.Y, mn.Width, mn.Height, 0xF0F0F0, 0x303040);
            R(mn.X + 3, mn.Y + 5, 4, 1, 0x303040);
            PanelBox(cl.X, cl.Y, cl.Width, cl.Height, 0xF07060, 0x303040);
            for (int i = 0; i < 4; i++) { R(cl.X + 3 + i, cl.Y + 2.5f + i, 1, 1, 0xFFFFFF); R(cl.X + 6 - i, cl.Y + 2.5f + i, 1, 1, 0xFFFFFF); }
            AddHit(mn, delegate { SetMini(true); });
            AddHit(cl, delegate { Close(); });
        }

        void DrawMini()
        {
            using (Graphics mg = Graphics.FromImage(miniBmp))
            {
                Graphics keep = lg;
                lg = mg;
                mg.InterpolationMode = InterpolationMode.NearestNeighbor;
                mg.PixelOffsetMode = PixelOffsetMode.Half;
                mg.Clear(Color.FromArgb(255, 0x20, 0x20, 0x30));
                PanelBox(0, 0, 24, 24, 0xF8F4E8, 0x303040);
                if (party.Count > 0)
                {
                    Mon m = party[0];
                    float bob = (float)Math.Round(Math.Sin(t * 3) * 0.6);
                    Blit(MonSprite(m.Sp, false), 4, 1 + bob, 16, 16);
                    HpBar(3, 19, 18, m.Hp, m.MaxHp);
                }
                lg = keep;
            }
        }

        // ---------- world ----------
        static float Mod(float a, float m) { float r = a % m; return r < 0 ? r + m : r; }

        static int Hash(int x) { unchecked { x = (x ^ 61) ^ (x >> 16); x *= 9; x ^= x >> 4; x *= 0x27d4eb2d; x ^= x >> 15; return x & 0x7fffffff; } }

        void DrawWorld()
        {
            float cam = Cam();
            foreach (ZoneDef z in Data.Zones)
            {
                float sx0 = z.X0 - cam, sx1 = z.X1 - cam;
                if (sx1 < 0 || sx0 > VW) continue;
                lg.SetClip(new RectangleF(Math.Max(0, sx0), 0, Math.Min(VW, sx1) - Math.Max(0, sx0), VH));
                DrawBackground(z.Kind, cam, false);
                lg.ResetClip();
            }

            // zone signposts
            for (int i = 1; i < Data.Zones.Length; i++)
            {
                float sx = Data.Zones[i].X0 - cam;
                if (sx < -10 || sx > VW + 10) continue;
                R(sx - 1, 38, 2, 10, 0x6A4A30);
                PanelBox(sx - 7, 33, 14, 7, 0xE8D0A0, 0x6A4A30);
                Tri(sx + 1, 34.5f, sx + 1, 38.5f, sx + 4, 36.5f, 0x6A4A30);
                R(sx - 4, 36, 5, 1, 0x6A4A30);
            }

            foreach (BuildingDef b in Data.Buildings) DrawBuilding(b, cam);

            int kind = Data.ZoneAt(px).Kind;
            DrawGrass(cam, false);

            // professor before you have a partner
            if (party.Count == 0) Blit(PersonSprite(5, 0, true), 172 - cam - 4, 45, 8, 12);

            for (int i = 0; i < Data.Trainers.Length; i++)
            {
                TrainerDef tr = Data.Trainers[i];
                float sx = tr.X - cam;
                if (sx < -10 || sx > VW + 10) continue;
                Blit(PersonSprite(tr.Look, 0, true), sx - 4, 45, 8, 12);
                if (tr.Champion) { R(sx - 2, 43, 5, 2, 0xF0C030); R(sx - 2, 42, 1, 1, 0xF0C030); R(sx, 42, 1, 1, 0xF0C030); R(sx + 2, 42, 1, 1, 0xF0C030); }
                if (!Beaten(i) && Math.Abs(px - tr.X) < 80 && mode == M_WORLD)
                {
                    PanelBox(sx - 3, 34, 7, 8, 0xFFFFFF, 0x303040);
                    R(sx, 35.5f, 1, 3, 0xE03030); R(sx, 39.5f, 1, 1, 0xE03030);
                }
            }

            // player
            if (party.Count > 0 || mode == M_STARTER || true)
            {
                int frame = walking && ((int)(walkAnim / 0.14f)) % 2 == 1 ? 1 : 0;
                float bob = walking && frame == 1 ? -1 : 0;
                Blit(PersonSprite(0, frame, facing < 0), px - cam - 4, 45 + bob, 8, 12);
            }

            DrawGrass(cam, true);

            // night tint
            if (TimeOfDay() == 2 && kind != 4) R(0, 0, VW, VH, 0x101040, 60);

            // interact hint
            if (mode == M_WORLD && Idle && party.Count > 0)
            {
                int b = NearBuilding();
                float hx = -100, hy = 1;
                if (b >= 0) hx = Data.Buildings[b].X - cam;
                else if (NearChampion()) { hx = Data.Trainers[Data.Trainers.Length - 1].X - cam; hy = 31; }
                if (hx > -50)
                {
                    float bob = (float)Math.Round(Math.Sin(t * 5));
                    PanelBox(hx - 5, hy + bob, 11, 9, 0xFFFFFF, 0x303040);
                    Txt("Z", hx + 0.5f, hy + 1 + bob, 6, 0x303040, 1);
                }
            }

            if (mode == M_WORLD) DrawHud();
        }

        void DrawBackground(int kind, float cam, bool battle)
        {
            int tod = kind == 4 ? 0 : TimeOfDay();
            int[] sky;
            if (kind == 4) sky = new int[] { 0x1A1210, 0x2A1E1A, 0x3A2A22, 0x4A3428 };
            else if (tod == 2) sky = new int[] { 0x0C1430, 0x142040, 0x1C2C50, 0x283C64 };
            else if (tod == 1) sky = new int[] { 0x4A3C80, 0xA05878, 0xE08860, 0xF8C070 };
            else if (kind == 3) sky = new int[] { 0x5898C8, 0x70B0D0, 0x88C8D0, 0xA0D8C8 };
            else sky = new int[] { 0x68A8E8, 0x80C0F0, 0x98D0F8, 0xB0E0F8 };
            for (int b = 0; b < 4; b++) R(0, b * 11, VW, 11, sky[b]);

            float groundY = battle ? 46 : 44;
            if (kind != 4)
            {
                if (tod == 2)
                {
                    for (int i = 0; i < 24; i++)
                    {
                        float sx = Mod(Hash(i * 7 + 3) % 997 - cam * 0.05f, VW);
                        if ((int)(t * 2 + i) % 7 != 0) R(sx, Hash(i * 13) % 34, 1, 1, 0xFFFFFF);
                    }
                    R(Mod(400 - cam * 0.02f, VW), 5, 6, 6, 0xF0F0D0); R(Mod(402 - cam * 0.02f, VW), 5, 4, 4, sky[0]);
                }
                else
                {
                    float sx = Mod(380 - cam * 0.02f, VW);
                    R(sx, 4, 8, 8, tod == 1 ? 0xF8A060 : 0xFFF0A0); R(sx - 1, 5, 10, 6, tod == 1 ? 0xF8A060 : 0xFFF0A0);
                    for (int i = 0; i < 7; i++)
                    {
                        float cx = Mod(i * 97 + 30 - cam * 0.15f + t * 3, VW + 60) - 30;
                        float cy = 4 + (i * 7) % 16;
                        int cc = tod == 1 ? 0xF8D0C0 : 0xFFFFFF;
                        R(cx, cy, 18, 4, cc); R(cx + 3, cy - 2, 10, 2, cc); R(cx + 12, cy - 1, 4, 1, cc);
                    }
                }
            }

            switch (kind)
            {
                case 0:
                case 1:
                    for (int i = 0; i < 9; i++)
                    {
                        float hx = Mod(i * 70 - cam * 0.35f, VW + 140) - 70;
                        int hh = 10 + Hash(i) % 10;
                        lg.FillEllipse(Br(tod == 2 ? 0x24483A : 0x58A858), hx, groundY - hh, 80, hh * 2);
                    }
                    for (int i = 0; i < 12; i++)
                    {
                        float hx = Mod(i * 50 + 20 - cam * 0.6f, VW + 100) - 50;
                        lg.FillEllipse(Br(tod == 2 ? 0x1E3C30 : 0x48985A), hx, groundY - 6, 34, 12);
                    }
                    R(0, groundY, VW, VH - groundY, 0x78C850);
                    R(0, groundY, VW, 1, 0x58A840);
                    break;
                case 2:
                    R(0, 30, VW, groundY - 30, tod == 2 ? 0x183058 : 0x3878C8);
                    for (int i = 0; i < 30; i++)
                    {
                        float wx = Mod(i * 37 - cam * 0.3f + (float)Math.Sin(t + i) * 3, VW + 20) - 10;
                        R(wx, 32 + (i * 5) % 11, 6, 1, tod == 2 ? 0x3050A0 : 0x88C0F0);
                    }
                    R(0, groundY, VW, VH - groundY, 0xE8D8A0);
                    R(0, groundY, VW, 1, 0xF8F0D0);
                    break;
                case 3:
                    for (int i = 0; i < 14; i++)
                    {
                        float tx = Mod(i * 45 - cam * 0.5f, VW + 90) - 45;
                        R(tx + 14, 18, 5, groundY - 18, 0x4A3020);
                        lg.FillEllipse(Br(tod == 2 ? 0x143820 : 0x2E6B3A), tx, 2 + Hash(i) % 8, 34, 24);
                        lg.FillEllipse(Br(tod == 2 ? 0x1C4828 : 0x3A8048), tx + 6, 6 + Hash(i) % 8, 22, 14);
                    }
                    R(0, groundY, VW, VH - groundY, 0x3E8A40);
                    R(0, groundY, VW, 1, 0x2E6A30);
                    break;
                case 4:
                    for (int i = 0; i < 20; i++)
                    {
                        float sx = Mod(i * 29 - cam * 0.6f, VW + 40) - 20;
                        int len = 6 + Hash(i * 3) % 12;
                        Tri(sx, 0, sx + 8, 0, sx + 4, len, 0x2A1E1A);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        float cx = Mod(i * 71 + 15 - cam * 0.8f, VW + 40) - 20;
                        int gl = (int)(Math.Sin(t * 2 + i) * 40);
                        Tri(cx, groundY, cx + 3, groundY - 9, cx + 6, groundY, 0x40C0D0);
                        R(cx + 2, groundY - 6, 1, 3, 0xB0F8FF, 150 + gl);
                    }
                    for (int i = 0; i < 10; i++)
                    {
                        float ex = Mod(i * 53 + (float)Math.Sin(t + i) * 4, VW);
                        float ey = groundY - Mod(t * 8 + i * 9, 40);
                        R(ex, ey, 1, 1, 0xFF8030);
                    }
                    R(0, groundY, VW, VH - groundY, 0x5A4A42);
                    R(0, groundY, VW, 1, 0x7A6050);
                    break;
            }

            if (!battle)
            {
                int pathA = kind == 2 ? 0xD8C088 : kind == 3 ? 0x9A7A50 : kind == 4 ? 0x403430 : 0xD8C090;
                int pathB = kind == 2 ? 0xC8B078 : kind == 3 ? 0x7A5A38 : kind == 4 ? 0x302420 : 0xB8A070;
                R(0, 52, VW, 7, pathA);
                R(0, 52, VW, 1, pathB); R(0, 58, VW, 1, pathB);
                // little ground details
                int first = (int)(cam / 13);
                for (int i = first; i < first + VW / 13 + 2; i++)
                {
                    float dx = i * 13 - cam + Hash(i) % 7;
                    int hy = Hash(i * 5) % 6;
                    if (kind <= 1 && hy < 3) { R(dx, 47 + hy, 1, 1, Hash(i) % 2 == 0 ? 0xF8F070 : 0xF890B0); }
                    else if (kind == 2 && hy < 2) R(dx, 47 + hy, 2, 1, 0xC8A878);
                    else if (kind == 4 && hy < 2) R(dx, 61, 2, 1, 0x6A5A50);
                    R(dx + 5, 60 + hy % 3, 1, 1, kind == 4 ? 0x4A3A34 : 0x5AA840);
                }
            }
        }

        void DrawGrass(float cam, bool front)
        {
            for (int g = 0; g < Data.Grass.Length; g += 2)
            {
                float sx0 = Data.Grass[g] - cam, sx1 = Data.Grass[g + 1] - cam;
                if (sx1 < 0 || sx0 > VW) continue;
                int kind = Data.ZoneAt(Data.Grass[g]).Kind;
                int dark = kind == 4 ? 0x7A3A2A : kind == 3 ? 0x1E6A2E : kind == 2 ? 0x4A9840 : 0x2E8A30;
                int light = kind == 4 ? 0xB0583A : kind == 3 ? 0x2E8A3E : kind == 2 ? 0x68B858 : 0x48B048;
                if (!front)
                {
                    R(sx0, 49, sx1 - sx0, 10, dark);
                    for (float x = sx0; x < sx1 - 3; x += 4)
                    {
                        float sway = (float)Math.Round(Math.Sin(t * 2 + x * 0.3) * 0.6);
                        Tri(x, 50, x + 2 + sway, 43, x + 4, 50, dark);
                    }
                }
                else
                {
                    for (float x = sx0 + 2; x < sx1 - 3; x += 4)
                    {
                        float sway = (float)Math.Round(Math.Sin(t * 2 + x * 0.3 + 1) * 0.6);
                        bool rustle = walking && Math.Abs(x + cam - px) < 6;
                        Tri(x, 59, x + 2 + sway + (rustle ? (float)Math.Sin(t * 30) : 0), 52, x + 4, 59, light);
                    }
                }
            }
        }

        void DrawBuilding(BuildingDef b, float cam)
        {
            float x = b.X - cam;
            if (x < -40 || x > VW + 40) return;
            if (b.Kind == 0)
            {
                PanelBox(x - 20, 24, 40, 25, 0xF4F0E8, 0x504048);
                Tri(x - 24, 25, x, 11, x + 24, 25, 0xD04040);
                R(x - 22, 24, 44, 2, 0xA02828);
                R(x - 5, 37, 10, 12, 0x6A4A3A); R(x - 4, 38, 8, 11, 0x8A6A50);
                R(x - 16, 30, 7, 2, 0xE03838); R(x - 13.5f, 27.5f, 2, 7, 0xE03838);
                R(x + 10, 29, 6, 5, 0x88C8F0); R(x + 10, 31, 6, 1, 0x504048);
                PanelBox(x - 9, 15, 18, 7, 0xFFFFFF, 0xA02828);
                Txt("REST", x, 15.6f, 5, 0xA02828, 1);
            }
            else if (b.Kind == 1)
            {
                PanelBox(x - 18, 26, 36, 23, 0xE8E8F0, 0x404858);
                R(x - 21, 20, 42, 7, 0x3060C0);
                for (int i = 0; i < 7; i++) R(x - 21 + i * 6, 26, 3, 3, i % 2 == 0 ? 0x3060C0 : 0xFFFFFF);
                R(x - 5, 37, 10, 12, 0x404858); R(x - 4, 38, 8, 11, 0x80A0D0);
                R(x - 15, 32, 7, 6, 0x88C8F0); R(x + 8, 32, 7, 6, 0x88C8F0);
                Txt("SHOP", x, 20.8f, 5, 0xFFFFFF, 1);
            }
            else
            {
                PanelBox(x - 17, 27, 34, 22, 0xF0E0C0, 0x604030);
                Tri(x - 21, 28, x, 14, x + 21, 28, 0x8A4A2A);
                R(x + 8, 17, 4, 8, 0x705048);
                R(x - 4, 38, 8, 11, 0x7A5030);
                R(x - 14, 32, 6, 6, 0x88C8F0); R(x - 11.5f, 32, 1, 6, 0x604030);
                R(x + 8, 32, 6, 6, 0x88C8F0); R(x + 10.5f, 32, 1, 6, 0x604030);
            }
        }

        void DrawHud()
        {
            if (party.Count == 0) return;
            Mon m = party[0];
            PanelBox(9, 2, 118, 12, 0xF8F4E8, 0x303040);
            Txt(m.Name + " Lv" + m.Level, 12, 3.4f, 6, 0x303040);
            HpBar(78, 7, 45, m.DispHp, m.MaxHp);
            AddHit(new RectangleF(9, 2, 118, 12), delegate { if (Idle) OpenTeam(); });

            PanelBox(318, 2, 134, 12, 0xF8F4E8, 0x303040);
            R(322, 5, 5, 5, 0xF0C030); R(323, 6, 3, 3, 0xF8E070);
            Txt(coins.ToString(), 329, 3.4f, 6, 0x303040);
            R(360, 5, 5, 5, 0xE03838); R(360, 7.5f, 5, 1, 0x303040); R(361.5f, 7, 2, 2, 0xFFFFFF);
            Txt("Orbs " + orbs, 367, 3.4f, 6, 0x303040);
            R(403, 5, 5, 5, 0x60B0F0); R(405, 4, 1, 1, 0x303040);
            Txt("Potions " + potions, 410, 3.4f, 6, 0x303040);

            if (bannerT > 0 && lastZone != null)
            {
                int a = (int)(Math.Min(1, bannerT) * 230);
                R(180, 17, 120, 11, 0x202030, a);
                Txt(lastZone.Name, 240, 18.2f, 7, 0xFFFFFF, 1);
            }
            else if (hintT > 0 && curMsg == null)
            {
                R(150, 58, 180, 6, 0x202030, 170);
                Txt("Arrows: walk | Z: talk | Tab: team | H: help", 240, 58.2f, 5, 0xFFFFFF, 1);
            }
            if (crashNote != null) Txt("Oops: " + crashNote, 132, 4, 5, 0xE03030);
        }

        void DrawMsgBox(float x, float y, float w, float h)
        {
            Cover(x, y, w, h);
            PanelBox(x, y, w, h, 0xFFFFFF, 0x303040);
            R(x + 2, y + 2, w - 4, 1, 0xE8E8F0);
            TxtWrap(curMsg, x + 4, y + 3, w - 10, 7, 0x202030);
            if (((int)(t * 3)) % 2 == 0) Tri(x + w - 8, y + h - 6, x + w - 4, y + h - 6, x + w - 6, y + h - 3, 0x303040);
        }

        void Button(RectangleF r, string label, bool on, Action a, float size = 6.5f, int tag = -1)
        {
            PanelBox(r.X, r.Y, r.Width, r.Height, on ? 0xFFF0B8 : 0xF8F8F8, on ? 0xE09020 : 0x8088A0);
            if (tag >= 0) R(r.X + 1, r.Y + 1, 2, r.Height - 2, tag);
            if (on) Tri(r.X + 4, r.Y + r.Height / 2 - 2, r.X + 4, r.Y + r.Height / 2 + 2, r.X + 6, r.Y + r.Height / 2, 0x303040);
            Txt(label, r.X + 8, r.Y + (r.Height - size) / 2 - 0.6f, size, 0x202030);
            AddHit(r, a);
        }

        void DrawStarter()
        {
            Cover(60, 3, 360, 58);
            PanelBox(60, 3, 360, 58, 0xF8F4E8, 0x303040);
            Txt("Choose your partner!", 240, 5, 7, 0x303040, 1);
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                float cx = 120 + i * 120;
                SpeciesDef d = Data.Species[Data.Starters[i]];
                bool on = cursor == i;
                RectangleF r = new RectangleF(cx - 52, 14, 104, 44);
                PanelBox(r.X, r.Y, r.Width, r.Height, on ? 0xFFF0B8 : 0xFFFFFF, on ? 0xE09020 : 0xB0B0C0);
                float bob = on ? (float)Math.Round(Math.Sin(t * 6)) : 0;
                Blit(MonSprite(Data.Starters[i], false), cx - 46, 20 + bob, 32, 32);
                Txt(d.Name, cx - 10, 22, 7, 0x202030);
                R(cx - 10, 32, 34, 8, Data.TypeColors[d.Type]);
                Txt(Data.TypeNames[d.Type], cx + 7, 32.6f, 5, 0xFFFFFF, 1);
                string[] blurb = { "Fiery & fast", "Calm & sturdy", "Tough & leafy" };
                Txt(blurb[i], cx - 10, 43, 5.5f, 0x505060);
                AddHit(r, delegate { if (!Idle) return; if (cursor == ii) PickStarter(ii); else cursor = ii; });
            }
        }

        void DrawTeam()
        {
            Cover(8, 2, 446, 60);
            PanelBox(8, 2, 446, 60, 0xF8F4E8, 0x303040);
            Txt("Your team", 13, 3.5f, 6.5f, 0x303040);
            Txt("Storage: " + box.Count, 70, 4, 5.5f, 0x606070);
            Button(new RectangleF(250, 3, 62, 10), "Make lead", false, delegate { if (Idle) TeamLead(); }, 5.5f);
            Button(new RectangleF(314, 3, 76, 10), "Swap w/ storage", false, delegate { if (Idle) TeamStorage(); }, 5.5f);
            Button(new RectangleF(392, 3, 58, 10), "Close", false, delegate { if (Idle) mode = M_WORLD; }, 5.5f);
            for (int i = 0; i < 6; i++)
            {
                RectangleF r = new RectangleF(12 + i * 73.5f, 15, 71, 44);
                if (i >= party.Count) { PanelBox(r.X, r.Y, r.Width, r.Height, 0xECE8DC, 0xD0CCC0); continue; }
                Mon m = party[i];
                bool on = cursor == i;
                PanelBox(r.X, r.Y, r.Width, r.Height, on ? 0xFFF0B8 : 0xFFFFFF, on ? 0xE09020 : 0xB0B0C0);
                Blit(MonSprite(m.Sp, false), r.X + 2, r.Y + 3, 24, 24);
                if (i == 0) Txt("LEAD", r.X + 4, r.Y + 28, 4.5f, 0xE09020);
                Txt(m.Name, r.X + 27, r.Y + 3, 5.5f, 0x202030);
                Txt("Lv" + m.Level, r.X + 27, r.Y + 10, 5.5f, 0x505060);
                R(r.X + 48, r.Y + 11, 20, 6, Data.TypeColors[m.Def.Type]);
                Txt(Data.TypeNames[m.Def.Type], r.X + 58, r.Y + 11.4f, 3.8f, 0xFFFFFF, 1);
                HpBar(r.X + 27, r.Y + 20, 40, m.Hp, m.MaxHp);
                Txt(m.Hp + "/" + m.MaxHp, r.X + 27, r.Y + 24, 5, 0x505060);
                R(r.X + 4, r.Y + 36, 63, 2, 0xD0D0E0);
                R(r.X + 4, r.Y + 36, 63f * m.Xp / Math.Max(1, m.XpNeed), 2, 0x58A8F0);
                int ii = i;
                AddHit(r, delegate { if (Idle) cursor = ii; });
            }
        }

        void DrawShop()
        {
            Cover(130, 15, 220, 44);
            PanelBox(130, 15, 220, 44, 0xF8F4E8, 0x303040);
            Txt("Shop", 136, 17, 7, 0x303040);
            Txt("You have " + coins + " coins", 344, 18, 6, 0x606070, 2);
            string[] labels = { "Orb  10c", "Potion  15c", "Leave" };
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                Button(new RectangleF(136 + i * 70, 30, 66, 13), labels[i], cursor == i, delegate { cursor = ii; ShopPick(ii); });
            }
            string[] desc = { "Throw at a weak wild critter to catch it.", "Heals 30 HP during a battle.", "See you next time!" };
            Txt(desc[cursor], 240, 47, 5.5f, 0x505060, 1);
        }

        // ---------- battle ----------
        void DrawBattle()
        {
            DrawBackground(battleKind, Cam(), true);

            int plat = battleKind == 2 ? 0xD8C888 : battleKind == 3 ? 0x2E7A34 : battleKind == 4 ? 0x48382F : 0x60B048;
            lg.FillEllipse(Br(plat), 26, 51, 62, 10);
            lg.FillEllipse(Br(plat), 390, 51, 62, 10);

            // foe
            if (foeIdx < foes.Count)
            {
                Mon f = F;
                if (!foeHidden)
                {
                    float off = fLunge > 0 ? -(float)Math.Sin(fLunge / 0.35f * Math.PI) * 10 : 0;
                    off += fSlide * 80;
                    DrawMon(f, 421 + off, 56, true, fFlash, fFaint);
                }
                if (orbT > 0 || caught)
                {
                    float wob = caught ? 0 : (float)Math.Sin(orbT * 9) * (orbT < 0.35f ? 0 : 2);
                    float oy = orbT < 0.35f ? 30 + (orbT / 0.35f) * 20 : 50;
                    DrawOrb(421 + wob, oy);
                    if (caught && ((int)(t * 6)) % 2 == 0) { R(412, 40, 1, 1, 0xFFFFA0); R(430, 42, 1, 1, 0xFFFFA0); R(421, 36, 1, 1, 0xFFFFA0); }
                }
                PanelBox(290, 3, 108, 22, 0xF8F4E8, 0x303040);
                Txt(f.Name, 294, 4.5f, 6.5f, 0x202030);
                Txt("Lv" + f.Level, 394, 5, 5.5f, 0x505060, 2);
                R(294, 14, 20, 6, Data.TypeColors[f.Def.Type]);
                Txt(Data.TypeNames[f.Def.Type], 304, 14.4f, 3.8f, 0xFFFFFF, 1);
                Txt("HP", 318, 14, 5, 0xE09020);
                HpBar(330, 16, 63, f.DispHp, f.MaxHp);
                if (curTr != null)
                    for (int i = 0; i < foes.Count; i++)
                        R(356 + i * 5, 21, 3, 2, foes[i].Hp > 0 ? 0xE03838 : 0x909098);
            }

            // player critter
            Mon p = P;
            {
                float off = pLunge > 0 ? (float)Math.Sin(pLunge / 0.35f * Math.PI) * 10 : 0;
                float intro = Math.Max(0, 1 - introT * 2.5f) * 80;
                DrawMon(p, 57 + off - intro, 56, false, pFlash, pFaint);
                if (evoFlash > 0) R(30, 10, 54, 48, 0xFFFFFF, (int)(evoFlash * 200));
            }
            PanelBox(84, 3, 112, 24, 0xF8F4E8, 0x303040);
            Txt(p.Name, 88, 4.5f, 6.5f, 0x202030);
            Txt("Lv" + p.Level, 192, 5, 5.5f, 0x505060, 2);
            Txt("HP", 88, 13.5f, 5, 0xE09020);
            HpBar(100, 15.5f, 60, p.DispHp, p.MaxHp);
            Txt((int)Math.Ceiling(p.DispHp) + "/" + p.MaxHp, 192, 13, 5, 0x404050, 2);
            R(88, 23, 104, 1.5f, 0xD0D0E0);
            R(88, 23, 104f * p.Xp / Math.Max(1, p.XpNeed), 1.5f, 0x58A8F0);

            // message / menu area
            float bx = 100, by = 29, bw = 280, bh = 33;
            if (curMsg != null) DrawMsgBox(bx, by, bw, bh);
            else if (Idle) DrawBattleMenu(bx, by, bw, bh);
            else PanelBox(bx, by, bw, bh, 0xFFFFFF, 0x303040);

            // intro wipe
            if (introT < 0.4f)
            {
                float k = 1 - introT / 0.4f;
                R(0, 0, VW, VH, 0x000000, (int)(k * 255));
            }
        }

        void DrawOrb(float cx, float cy)
        {
            lg.FillEllipse(Br(0x181820), cx - 4, cy - 4, 8, 8);
            lg.FillEllipse(Br(0xE03838), cx - 3, cy - 3, 6, 6);
            R(cx - 3, cy, 6, 3, 0xF8F8F8);
            R(cx - 3, cy - 0.5f, 6, 1, 0x181820);
            R(cx - 1, cy - 1, 2, 2, 0xFFFFFF);
        }

        void DrawMon(Mon m, float cx, float baseY, bool flip, float flash, float faint)
        {
            if (flash > 0 && ((int)(flash * 16)) % 2 == 0) return;
            float size = m.Def.Big ? 40 : 32;
            float bob = (float)Math.Round(Math.Sin(t * 3 + (flip ? 1 : 0)) * 0.8);
            float y = baseY - size + 1 + bob + faint * size;
            if (faint >= 1) return;
            lg.SetClip(new RectangleF(cx - size, 0, size * 2, baseY + 2));
            Blit(MonSprite(m.Sp, flip), cx - size / 2, y, size, size);
            lg.ResetClip();
        }

        void DrawBattleMenu(float x, float y, float w, float h)
        {
            PanelBox(x, y, w, h, 0xFFFFFF, 0x303040);
            if (phase == PH_MENU)
            {
                TxtWrap("What will " + P.Name + " do?", x + 5, y + 4, 100, 7, 0x202030);
                string[] l = { "FIGHT", "BAG", "TEAM", "RUN" };
                for (int i = 0; i < 4; i++)
                {
                    int ii = i;
                    RectangleF r = new RectangleF(x + 112 + (i % 2) * 83, y + 3 + (i / 2) * 14, 81, 13);
                    Button(r, l[i], cursor == i, delegate { cursor = ii; BattlePick(ii); });
                }
            }
            else if (phase == PH_FIGHT)
            {
                for (int i = 0; i < P.Moves.Count; i++)
                {
                    int ii = i;
                    MoveDef mv = Data.Moves[P.Moves[i]];
                    RectangleF r = new RectangleF(x + 3 + (i % 2) * 92, y + 3 + (i / 2) * 14, 90, 13);
                    Button(r, mv.Name, cursor == i, delegate { cursor = ii; BattlePick(ii); }, 6.5f, Data.TypeColors[mv.Type]);
                }
                if (cursor < P.Moves.Count)
                {
                    MoveDef mv = Data.Moves[P.Moves[cursor]];
                    R(x + 192, y + 4, 40, 8, Data.TypeColors[mv.Type]);
                    Txt(Data.TypeNames[mv.Type], x + 212, y + 4.6f, 5, 0xFFFFFF, 1);
                    Txt("Power " + mv.Power, x + 192, y + 14, 5.5f, 0x404050);
                    Txt("Accuracy " + mv.Acc, x + 192, y + 21, 5.5f, 0x404050);
                    double e = Data.Eff(mv.Type, F.Def.Type);
                    if (e != 1) Txt(e > 1 ? "Super effective!" : "Not very effective", x + 236, y + 4.5f, 5, e > 1 ? 0x30A040 : 0xC05030);
                }
                Button(new RectangleF(x + w - 34, y + h - 13, 31, 10), "Back", false, delegate { BattleBack(); }, 5);
            }
            else if (phase == PH_BAG)
            {
                string[] l = { "Orb x" + orbs, "Potion x" + potions, "Back" };
                for (int i = 0; i < 3; i++)
                {
                    int ii = i;
                    Button(new RectangleF(x + 3 + i * 92, y + 3, 90, 13), l[i], cursor == i, delegate { cursor = ii; BattlePick(ii); });
                }
                string[] d = { curTr != null ? "Can't catch a trainer's critter." : "Catch the wild critter. Works best at low HP.", "Heal 30 HP. Uses your turn.", "" };
                Txt(d[Math.Min(cursor, 2)], x + 6, y + 21, 5.5f, 0x505060);
            }
            else if (phase == PH_TEAM)
            {
                int n = party.Count + (forcedSwitch ? 0 : 1);
                for (int i = 0; i < n; i++)
                {
                    int ii = i;
                    RectangleF r = new RectangleF(x + 3 + (i % 4) * 69, y + 3 + (i / 4) * 14, 67, 13);
                    if (i >= party.Count) { Button(r, "Back", cursor == i, delegate { cursor = ii; BattlePick(ii); }, 5.5f); continue; }
                    Mon m = party[i];
                    Button(r, m.Name, cursor == i, delegate { cursor = ii; BattlePick(ii); }, 5.2f, m.Hp > 0 ? (i == act ? 0xE09020 : 0x48D058) : 0x909098);
                }
                if (forcedSwitch) Txt("Pick who goes next!", x + w - 6, y + h - 9, 5.5f, 0xC05030, 2);
            }
        }

        // ---------- test hooks ----------
        public string DebugState()
        {
            return "mode=" + mode + " phase=" + phase + " px=" + (int)px + " msg=" + (curMsg ?? "-") + " evs=" + evs.Count +
                   " party=" + party.Count + " lead=" + (party.Count > 0 ? party[0].Name + party[0].Level : "-") + " beaten=" + beaten + " champ=" + champWins;
        }
        public int Mode { get { return mode; } }
        public int Phase { get { return phase; } }
        public bool IsIdle { get { return Idle; } }
        public int ChampWins { get { return champWins; } }
        public void DebugGive(int sp, int lv) { party.Add(Mon.Create(sp, lv)); }
        public void DebugSetX(float x) { px = x; }
        public void DebugClearMsgs() { curMsg = null; }
        public void DebugAutoBattle(bool tryCatch)
        {
            if (mode != M_BATTLE || !Idle) return;
            if (phase == PH_TEAM) { for (int i = 0; i < party.Count; i++) if (party[i].Hp > 0 && i != act) { SwitchTo(i); return; } }
            phase = PH_MENU;
            if (tryCatch && curTr == null && orbs > 0 && F.Hp < F.MaxHp / 2) { ThrowOrb(); return; }
            if (potions > 0 && P.Hp < P.MaxHp / 4) { UsePotion(); return; }
            int best = 0; double bs = -1;
            for (int i = 0; i < P.Moves.Count; i++)
            {
                MoveDef mv = Data.Moves[P.Moves[i]];
                double sc = mv.Power * Data.Eff(mv.Type, F.Def.Type) * (mv.Type == P.Def.Type ? 1.5 : 1);
                if (sc > bs) { bs = sc; best = i; }
            }
            UseMove(best);
        }
        public void DebugPhase(int ph) { phase = ph; cursor = 0; }
    }
}
'@
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Windows.Forms, System.Drawing -IgnoreWarnings -ErrorAction Stop
    [TaskbarTamer.Launcher]::Run()
} catch {
    [System.Windows.Forms.MessageBox]::Show("Taskbar Tamer couldn't start:`r`n`r`n" + $_.Exception.Message, "Taskbar Tamer") | Out-Null
}
