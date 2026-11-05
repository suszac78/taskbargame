// Game data: types, moves, critters, trainers and the world layout.
// Written in C# 5 so Windows PowerShell's built-in compiler can build it.
using System;
using System.Collections.Generic;
using System.Drawing;

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
        public int Gym = -1;     // badge index for gym leaders
    }

    public class ZoneDef
    {
        public string Name, Region; public int X0, X1, Kind;
        public int[] Wild;       // pairs: species, weight
        public int MinLv, MaxLv;
    }

    public class BuildingDef
    {
        public int X, Kind, Type; // kind: 0 heal, 1 shop, 2 home, 3 gym (Type = gym type)
        public BuildingDef(int x, int k) { X = x; Kind = k; }
        public BuildingDef(int x, int k, int type) { X = x; Kind = k; Type = type; }
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
        static readonly int[] LearnStorm = { 1, 2, 4, 14, 9, 3, 15, 15, 21, 4, 25, 16 };
        static readonly int[] LearnMoss = { 1, 1, 3, 11, 8, 20, 13, 12, 18, 18, 23, 13 };

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
            S("Blazehound", FIRE,  75, 88, 65, 88,  -1,  0, LearnFire,  1, true,  25, 0xD83828, 0x881C14, 0xFFA020), // 1
            S("Driplet",    WATER, 50, 48, 55, 45,   3, 14, LearnWater, 2, false, 45, 0x58A8F0, 0x3070C0, 0xFFFFFF), // 2
            S("Tidalisk",   WATER, 85, 80, 82, 65,  -1,  0, LearnWater, 3, true,  25, 0x3858D0, 0x203090, 0x90E8FF), // 3
            S("Sproutle",   LEAF,  55, 50, 55, 40,   5, 14, LearnLeaf,  4, false, 45, 0x80D0B0, 0x4A9080, 0x38B030), // 4
            S("Thornox",    LEAF,  85, 82, 88, 55,  -1,  0, LearnLeaf,  5, true,  25, 0x4A9070, 0x2F6050, 0x2F9A40), // 5
            S("Zappit",     VOLT,  40, 52, 35, 78,   7, 16, LearnVolt,  6, false, 50, 0x50B8C8, 0x2E7E90, 0xFFE040), // 6
            S("Voltail",    VOLT,  65, 88, 55,105,  -1,  0, LearnVolt,  7, true,  25, 0x3858C8, 0x203888, 0xFFC020), // 7
            S("Pebblin",    ROCK,  50, 55, 72, 25,   9, 16, LearnRock,  8, false, 55, 0xB0A088, 0x786850, 0x9FE0F0), // 8
            S("Bouldon",    ROCK,  88, 92,108, 30,  -1,  0, LearnRock,  9, true,  25, 0x888078, 0x585048, 0xF0A030), // 9
            S("Puffowl",    NORMAL,45, 45, 40, 58,  11, 15, LearnBird,  10, false, 55, 0xC8A070, 0x907048, 0xF0A030), // 10
            S("Gustowl",    NORMAL,75, 78, 65, 88,  -1,  0, LearnBird,  11, true,  25, 0x8898B0, 0x586078, 0xF0C830), // 11
            S("Cindermite", FIRE,  38, 52, 40, 58,  13, 15, LearnMite,  12, false, 50, 0xE04838, 0x902828, 0xFFB020), // 12
            S("Magmantis",  FIRE,  68, 92, 60, 82,  -1,  0, LearnMite,  13, true,  25, 0x505058, 0x28282C, 0xFF6020), // 13
            S("Glimfin",    WATER, 40, 45, 42, 68,  15, 15, LearnFin,   14, false, 50, 0x60D0D0, 0x3090A0, 0xFFF080), // 14
            S("Lumiray",    WATER, 72, 72, 72, 88,  -1,  0, LearnFin,   15, true,  25, 0x9070E0, 0x5840A0, 0x80FFE0), // 15
            S("Stormowl",   VOLT,  42, 50, 38, 66,  17, 16, LearnStorm, 10, false, 50, 0x8090A8, 0x505C78, 0xFFE040), // 16
            S("Tempestowl", VOLT,  70, 84, 60, 96,  -1,  0, LearnStorm, 11, true,  25, 0x404C70, 0x262E48, 0xFFD020), // 17
            S("Mossmite",   LEAF,  44, 50, 46, 52,  19, 15, LearnMoss,  12, false, 50, 0x78C050, 0x3E8030, 0xF0E060), // 18
            S("Verdantis",  LEAF,  74, 86, 70, 76,  -1,  0, LearnMoss,  13, true,  25, 0x3E7A48, 0x24502C, 0xA0F060), // 19
        };

        public static readonly int[] Starters = { 0, 2, 4 };

        // 24x24 critter sprites, one per species (made by a shape script, then outlined and shaded).
        // k=outline 1=main 2=shade h=highlight 3=accent 4=dark accent c=cream w=white e=eye p=pink. All face right.
        public static readonly string[][] Sprites = {
            new string[] { // 0 emberpup
                ".....k..................",
                "....k3k......k....k.....",
                "..kk3kk.....k1k..k1k....",
                ".k33333k....k1k.k11k....",
                ".k33h33k...k131kk13k....",
                ".k3hhh3k...k13hhhh1kk...",
                ".k3hhh3k...khhhh11111k..",
                ".k3hhh31k.khhh11111111k.",
                ".k11h111k.khh1111we111k.",
                ".k111112k.k111111ee111k.",
                ".k111122k.k11111111ccckk",
                "..k1112kkkk1111111ccccce",
                "..k1122khhh1111111ccccck",
                "...kkkhhhh11111122cpceek",
                "....khhh11111112222kkkk.",
                "...khh111111111111k.....",
                "...k111111cccccc12k.....",
                "...k11111cccccccc2k.....",
                "....k1111cccccccck......",
                ".....k1221cccc11k.......",
                "......k222222211k.......",
                "......k22kk22k11k.......",
                ".......kk..kk.kk........",
                "........................" },
            new string[] { // 1 emberpup_evo
                "..kkkk3k......k3k.......",
                ".k3333k......kk33kk.....",
                ".k3333k.....k1k3hk1k....",
                "k33hh33k....k133h11k....",
                "k33hh33k...k1333313k....",
                "k3hhhh3k...k13hhhh1kk...",
                ".k3hh33k...khhhh11111k..",
                ".k3hh331k.khhh11111111k.",
                ".k11h111k.khh1111we111k.",
                ".k111112k.k111111ee111k.",
                ".k111122k.k11111111ccckk",
                "..k1112kkkk1111111ccccce",
                "..k1122khhh1111111ccccck",
                "...kkkhhh333111122cpceek",
                "....khhh11111112222kkkk.",
                "...khh111111111111k.....",
                "...k111111cccccc12k.....",
                "...k11111cccccccc2k.....",
                "....k1111cccccccck......",
                ".....k1221cccc11k.......",
                "......k222222211k.......",
                "......k22kk22k11k.......",
                ".......kk..kk.kk........",
                "........................" },
            new string[] { // 2 driplet
                "........................",
                "...........kk...........",
                "..........k11k..........",
                "..........k11k..........",
                ".........k1111k.........",
                ".........k1111k.........",
                "........k111111k........",
                ".......k11111111k.......",
                ".......k1whhhh11k.......",
                "......k1whhh11111k......",
                ".....khhwhh1111111k.....",
                ".....khwhh11111111k.....",
                "....khhw11111111111k....",
                "....khh11we111we111k....",
                "....k1111ee111ee111k....",
                "....k1111ee111ee112k....",
                "....k11111111111122k....",
                "....k11p11e11e12p22k....",
                ".....k11111pp12222k.....",
                ".....k111111222222k.....",
                "......kk11122222kk......",
                "........k122222k........",
                ".........kkkkkk.........",
                "........................" },
            new string[] { // 3 driplet_evo
                "...........kk...........",
                "..........k33k..........",
                ".........k333k..........",
                ".........k3333k.........",
                "........k333333k........",
                ".........k1111k.........",
                "........k111111k........",
                ".......k11111111k.......",
                ".......k1whhhh11k.......",
                "......k1whhh11111k......",
                ".....khhwhh1111111k.....",
                "k....khwhh11111111k....k",
                "3k..khhw11111111111k..k3",
                "33k.khh11we111we111k.k33",
                "k33kk1111ee111ee111kk33k",
                "k333k1111ee111ee112k333k",
                "k33kk11111111111122kk33k",
                ".k3kk11p11e11e12p22kk3k.",
                "..k..k11111pp12222k..k..",
                ".....k111111222222k.....",
                "......kk11122222kk......",
                "........k122222k........",
                ".........kkkkkk.........",
                "........................" },
            new string[] { // 4 sproutle
                "........................",
                "........................",
                "........................",
                "...........k............",
                "........k.k3k...........",
                ".......k3kk33k.k........",
                "....k.k33k334kk3k.......",
                "...k3kk343334k33k.......",
                "...k33k343333343k.......",
                "..k3343333333333k.......",
                "..k33433khhhhk33k.......",
                "..k333hhhhhh1111kk......",
                "..k3hhhhhh11111111kk....",
                "..khhhhh111111111weckk..",
                "..khh1111111111cceeccck.",
                ".kh11111111111ccccccccck",
                ".k111111111111ccccccccek",
                "..k11111111111ccccccccck",
                "..k111111111112ccccpcck.",
                "...k1111111122222ccckk..",
                "....kk2211222211kkkk....",
                ".....k22k2222k11k.......",
                ".....k22kkkkkk11k.......",
                "......kk......kk........" },
            new string[] { // 5 sproutle_evo
                "........................",
                "........................",
                "........................",
                "..........kk............",
                "........kkppk...........",
                ".......k3ppppk.k........",
                "....kkk33ppwpkk3k.......",
                "...k3pp343pp4k33k.......",
                "...kpppp43333343k.......",
                "..k3pppp33333333k.......",
                ".k333pp3khhhhk33k.......",
                ".k3333hhhhhh1111kk......",
                "k333hhhhhh11111111kk....",
                "k3333hhh111111111weckk..",
                "k33hh1111111111cceeccck.",
                ".kh11111111111ccccccccck",
                ".k111111111111ccccccccek",
                "..k11111111111ccccccccck",
                "..k111111111112ccccpcck.",
                "...k1111111122222ccckk..",
                "....kk2211222211kkkk....",
                ".....k22k2222k11k.......",
                ".....k22kkkkkk11k.......",
                "......kk......kk........" },
            new string[] { // 6 zappit
                "........................",
                "........................",
                "........................",
                "...k.............k......",
                ".kk3k.k.........k3k.....",
                "k3333k3k.......k33k.....",
                "k3ww33k........k33kk....",
                "33ww333k......kkhhh1kk..",
                "33ww333k.....khhhh1111k.",
                "3333333k.....khh11we11k.",
                "k33333k.....khh111eek11k",
                ".k333k...kkkk11111ee111k",
                "kh111k.kkhhhh1111111122k",
                "k1111kkhhh3131111p1122k.",
                "k1112khhh11111111122eeek",
                ".k12h1h1111111111222kkk.",
                ".kh111111111111112kk....",
                ".k1111111111111222k.....",
                "..k11121111112222k......",
                "...kkkkk222222211k......",
                ".......k22kkkkk11k......",
                ".......k22k...k11k......",
                "........kk.....kk.......",
                "........................" },
            new string[] { // 7 zappit_evo
                "........................",
                "........................",
                "........................",
                "...k.............k..k...",
                ".kk3k.k.........k3kk3k..",
                "k3333k3k.......k33kk3k..",
                "k3ww33k........k33k33k..",
                "33ww333k......kkhhh33k..",
                "33ww333k..k..khhhh1111k.",
                "3333333k.k3k.khh11we11k.",
                "k33333k..k3kk3h111eek11k",
                ".k333k..k333331111ee111k",
                "kh111k.kkhhhh1111111122k",
                "k1111kkhhh3131111p1122k.",
                "k1112khhh11111111122eeek",
                ".k12h1h1111111111222kkk.",
                ".kh111111111111112kk....",
                ".k1111111111111222k.....",
                "..k11121111112222k......",
                "...kkkkk222222211k......",
                ".......k22kkkkk11k......",
                ".......k22k...k11k......",
                "........kk.....kk.......",
                "........................" },
            new string[] { // 8 pebblin
                "........................",
                "........................",
                "............k...........",
                "...........k3k..........",
                "..........k3wk..k.......",
                "..........k3wk.k3k......",
                "..........k333k3wk......",
                ".........k3333k33k......",
                ".......kkk3333333k......",
                "......khhhhhh1111k......",
                "....kkhhhhhh111111kk....",
                "...khhhhhh1111111111k...",
                "...khhhh11111k1111k1k...",
                "..khh2111111k111111k1k..",
                "..khh12111111we11we11k..",
                "..k1112111111ee11ee12k..",
                "..k111111111111111122k..",
                "..k111111111111112222k..",
                "...k11111111111kk222k...",
                "...k1111111112222222k...",
                "....kk111111222222kk....",
                "......k1112222222k......",
                "......k222kkkkk222k.....",
                ".......kkk.....kkk......" },
            new string[] { // 9 pebblin_evo
                "........................",
                "........................",
                "............k...........",
                "...........k3k..........",
                "..........k3wk..k.......",
                "..........k3wk.k3k......",
                "....k.....k333k3wk..k...",
                "...k3k...k3333k33k.k3k..",
                "...kw3kkkk3333333k.k3k..",
                "...k33khhhhhh1111k.k3k..",
                "...k333hhhhh111111k33k..",
                "...k33hhhh11111111113k..",
                "...khhhh1111kkk11kkkk...",
                "..khh2111111k111111k1k..",
                "..khh12111111we11we11k..",
                "..k1112111111ee11ee12k..",
                "..k111111111111111122k..",
                "..k111111111111112222k..",
                "...k11111111111kk222k...",
                "...k1111111112222222k...",
                "....kk111111222222kk....",
                "......k1112222222k......",
                "......k222kkkkk222k.....",
                ".......kkk.....kkk......" },
            new string[] { // 10 puffowl
                "......k..........k......",
                ".....k1k........k1k.....",
                ".....k11k......k11k.....",
                "....k1111kkkkkk1111k....",
                "....k11kkhhhhh1kk11k....",
                ".....kkhhhhhh1111kk.....",
                ".....khhcccccccc11k.....",
                "....khhcccccccccc11k....",
                "...kkhcwwwwccwwwwc1kk...",
                "..k222cwwwwwwwwwwc222k..",
                "..k22ccwwwewwwwewcc22k..",
                ".k2222cwweeccweewc2222k.",
                ".k2222ccccc33ccccc2222k.",
                ".k22222cccc33cccc22222k.",
                ".k222221cccccccc122222k.",
                ".k22222111111111122222k.",
                ".k22222111211122222222k.",
                "..k222111111112222222k..",
                "..k222111211122222222k..",
                "...kkk111112222222kkk...",
                "......k1112222222k......",
                ".......kk222222kk.......",
                ".......k33kkkk33k.......",
                ".......k33k..k33k......." },
            new string[] { // 11 puffowl_evo
                "......k...k33k...k......",
                ".....k1k..k33k..k1k.....",
                ".....k11kk3333kk11k.....",
                "....k1111kkkkkk1111k....",
                "....k11kkhhhhh1kk11k....",
                ".....kkhhhhhh1111kk.....",
                ".....khhcccccccc11k.....",
                "....khhcccccccccc11k....",
                "...kkhcwwwwccwwwwc1kk...",
                "..k322cwwwwwwwwwwc222k..",
                ".k332ccwwwewwwwewcc23k..",
                "k33322cwweeccweewc2233k.",
                "k33322ccccc33ccccc22333k",
                ".k33222cccc33cccc22233k.",
                ".k332221cccccccc122233k.",
                ".k23222111111111122232k.",
                ".k23222111211122222232k.",
                "..k222111111112222222k..",
                "..k222111211122222222k..",
                "...kkk111112222222kkk...",
                "......k1112222222k......",
                ".......kk222222kk.......",
                ".......k33kkkk33k.......",
                ".......k33k..k33k......." },
            new string[] { // 12 cindermite
                ".................k3k....",
                "................k333kkk.",
                "................k333k33k",
                "................k3w33333",
                "................k33333w3",
                ".................k3kk33k",
                "..................k..kk.",
                "..................k..k..",
                ".........kkk.......kk...",
                "......kkkhhhkkk.........",
                ".....khhhhh1111kk.kkk...",
                "...kkkkkh11111111k222k..",
                "..khhhh1kkkkkkkkk22222k.",
                "..khh133111111112222we2k",
                ".kh11133111111112222ee2k",
                ".k111111111331112222222k",
                "..k1133111133112222222k.",
                "..k113311111122222222k..",
                "...k1111111222222kkkk...",
                "....kk111222222kkk......",
                "....k.kkk222kkk...k.....",
                "...k....kkkk.k..........",
                "........k.....k.........",
                "........................" },
            new string[] { // 13 cindermite_evo
                "..............kk.k3k....",
                ".............k33k333kkk.",
                "............k3333333k33k",
                ".............k33k3w33333",
                "..............kkk33333w3",
                "................kk3kk33k",
                ".................kk..kk.",
                ".................kk..kk.",
                ".........kkk.......kkk3k",
                "......kkkhhhkkk.....k3k.",
                ".....khhhhh1111kk.kk33k.",
                "...kkkkkh11111111k2333k.",
                "..khhhh1kkkkkkkkk22233k.",
                "..khh133111111112222we2k",
                ".kh11133111111112222ee2k",
                ".k111111111331112222222k",
                "..k1133111133112222222k.",
                "..k113311111122222222k..",
                "...k1111111222222kkkk...",
                "....kk111222222kkk......",
                "....k.kkk222kkk...k.....",
                "...k....kkkk.k..........",
                "........k.....k.........",
                "........................" },
            new string[] { // 14 glimfin
                "........................",
                "....................kk..",
                "...................k33k.",
                "..........kk......k3333k",
                ".........k22k.....k3w33k",
                ".........k22k.....k3333k",
                "........k2222k...k.k33k.",
                "........k2222kkk....kk..",
                ".k.....k2hhhhh11kkk.....",
                "k2k...khhhhh1111111k....",
                "k22k.khhhhh111111111k...",
                "k222kkhhh1111111we11k...",
                "k2222hh111111111ee111k..",
                "k2222h111111111111111k..",
                "k22221111111111111112k..",
                "k22221112222111111eewk..",
                "k222kk11222211112eweek..",
                "k22k.k1111111112222wek..",
                "k2k...k111111222222kk...",
                ".k.....kk11222222kk.....",
                ".........kkkkkkkk.......",
                "........................",
                "........................",
                "........................" },
            new string[] { // 15 glimfin_evo
                ".........k33k...........",
                "........k3333k......kk..",
                ".........k33k......k33k.",
                "..........kk.k....k3333k",
                ".........k22k.....k3w33k",
                ".....kk..k22k.k...k3333k",
                "....k22kk2222k...k.k33k.",
                "....k22kk2222kkk....kk..",
                ".k.k22222hhhhh11kkk.....",
                "k2k.kkkhhhhh1111111k....",
                "k22k.khhhhh111111111k...",
                "k222kkhhh1111111we11k...",
                "k2222hh111111111ee111k..",
                "k2222h111111111111111k..",
                "k22221111111111111112k..",
                "k22221112222111111eewk..",
                "k222kk11222211112eweek..",
                "k22k.k1111111112222wek..",
                "k2k...k111111222222kk...",
                ".k.....kk11222222kk.....",
                ".........kkk2222k.......",
                "............k22k........",
                ".............k2k........",
                "..............k........." },
        };

        // 10x16 people. 3=hat h=hat shine a=hair s=skin b=top c=top light d=trousers g=bag o=shoes
        public static readonly string[] Person1 = {
            "...kkkk...",
            "..k3h33k..",
            ".k333333k.",
            ".k3333333k",
            ".kaakkkkkk",
            ".kaasssek.",
            ".kaassssk.",
            "..kassssk.",
            "..kkkkkk..",
            ".kgkbcbbk.",
            ".kgkbbbsk.",
            ".kgkbcbbk.",
            "..kkddddk.",
            "..kddkddk.",
            "..kdk.kdk.",
            "..kok.kok.",
        };
        public static readonly string[] Person2 = {
            "...kkkk...",
            "..k3h33k..",
            ".k333333k.",
            ".k3333333k",
            ".kaakkkkkk",
            ".kaasssek.",
            ".kaassssk.",
            "..kassssk.",
            "..kkkkkk..",
            ".kgkbcbbk.",
            ".kgkbbbsk.",
            ".kgkbcbbk.",
            "..kkddddk.",
            "...kddddk.",
            "...kddk...",
            "...kook...",
        };
        // hat, top, trousers, hair, bag for: player, bug kid, hiker, ranger, champion, professor
        public static readonly int[][] Looks = {
            new int[] { 0x28A8A0, 0xF08838, 0x384058, 0x5A3C28, 0x58A848 },
            new int[] { 0x40A040, 0xF0D040, 0x705030, 0x302018, 0x8A5A30 },
            new int[] { 0x8A6A40, 0xC05030, 0x504030, 0x403028, 0x607040 },
            new int[] { 0x2E7D32, 0x80B060, 0x404830, 0xC08040, 0x806040 },
            new int[] { 0x7030A0, 0x303048, 0x581C80, 0xE8E8F0, 0x7030A0 },
            new int[] { 0xE8E8E8, 0xF4F4F4, 0x606070, 0x909098, 0x909098 },
            new int[] { 0x7A6A58, 0xA08A60, 0x4A4038, 0x2A2420, 0x6A5A48 }, // 6 Slate (rock)
            new int[] { 0x2F7FD8, 0x58B0F0, 0x1E3C70, 0x2A6AA8, 0xF0F0F8 }, // 7 Marina (water)
            new int[] { 0x3A9A48, 0x9AD070, 0x3A5030, 0xD89048, 0x6A8A38 }, // 8 Fern (leaf)
            new int[] { 0xD83828, 0xF08030, 0x402020, 0xE85020, 0x302828 }, // 9 Blaze (fire)
            new int[] { 0xE8C020, 0x303848, 0x202838, 0xF8E890, 0xE8C020 }, // 10 Volta (volt)
            new int[] { 0x30B8D0, 0xF06878, 0x2850A0, 0x5A3418, 0xF8D040 }, // 11 swimmer
            new int[] { 0xF0A020, 0x5A6A80, 0x303848, 0x403028, 0x808890 }, // 12 engineer
        };

        // Five regions in a row along the taskbar. Each ends in a town with a gym.
        public static readonly string[] BadgeNames = { "Pebble", "Wave", "Sprout", "Flame", "Spark" };
        public static readonly int[] BadgeTypes = { ROCK, WATER, LEAF, FIRE, VOLT };

        public static readonly ZoneDef[] Zones = {
            Z("Pixelvale",     "Meadowmark", 0,    300,  0, new int[0], 1, 1),
            Z("Breezy Path",   "Meadowmark", 300,  800,  1, new int[] { 10, 40, 8, 35, 6, 15, 12, 10 }, 2, 5),
            Z("Cobbleton",     "Meadowmark", 800,  1040, 0, new int[0], 1, 1),
            Z("Misty Shore",   "Tidewater",  1040, 1580, 2, new int[] { 14, 40, 2, 12, 10, 20, 6, 28 }, 5, 9),
            Z("Port Ripple",   "Tidewater",  1580, 1820, 2, new int[0], 1, 1),
            Z("Glowwood",      "Verdant",    1820, 2390, 3, new int[] { 4, 25, 18, 25, 6, 20, 10, 20, 0, 10 }, 9, 13),
            Z("Mossbrook",     "Verdant",    2390, 2630, 3, new int[0], 1, 1),
            Z("Ember Cave",    "Cinder",     2630, 3220, 4, new int[] { 8, 30, 0, 20, 12, 20, 14, 15, 6, 15 }, 13, 17),
            Z("Kilnburg",      "Cinder",     3220, 3460, 4, new int[0], 1, 1),
            Z("Thunder Ridge", "Stormcrest", 3460, 4030, 5, new int[] { 16, 30, 6, 30, 8, 20, 10, 20 }, 17, 21),
            Z("Boltgate",      "Stormcrest", 4030, 4270, 5, new int[0], 1, 1),
            Z("Victory Road",  "Stormcrest", 4270, 4700, 4, new int[] { 8, 25, 10, 25, 12, 20, 14, 15, 16, 15 }, 22, 26),
        };
        public const int WorldW = 4700;

        static ZoneDef Z(string n, string region, int x0, int x1, int kind, int[] wild, int a, int b)
        {
            ZoneDef z = new ZoneDef(); z.Name = n; z.Region = region; z.X0 = x0; z.X1 = x1; z.Kind = kind; z.Wild = wild; z.MinLv = a; z.MaxLv = b;
            return z;
        }

        public static readonly int[] Grass = {
            330, 420, 480, 600, 640, 735,
            1080, 1200, 1260, 1380, 1430, 1510,
            1870, 2010, 2060, 2200, 2240, 2320,
            2680, 2840, 2890, 3030, 3070, 3150,
            3510, 3650, 3700, 3840, 3880, 3960,
            4310, 4440, 4490, 4600 };

        public static readonly BuildingDef[] Buildings = {
            new BuildingDef(40, 2), new BuildingDef(110, 0), new BuildingDef(200, 1),
            new BuildingDef(850, 0), new BuildingDef(910, 1), new BuildingDef(990, 3, ROCK),
            new BuildingDef(1630, 0), new BuildingDef(1690, 1), new BuildingDef(1770, 3, WATER),
            new BuildingDef(2440, 0), new BuildingDef(2500, 1), new BuildingDef(2580, 3, LEAF),
            new BuildingDef(3270, 0), new BuildingDef(3330, 1), new BuildingDef(3410, 3, FIRE),
            new BuildingDef(4080, 0), new BuildingDef(4140, 1), new BuildingDef(4220, 3, VOLT),
        };

        public static readonly TrainerDef[] Trainers = {
            T("Bug Kid Tam", 770, 1, -1, new int[] { 12, 4, 10, 5 },
              "Hey! My critters have been itching for a fight!", "Aww, my bugs got squished..."),
            T("Leader Slate", 990, 6, 0, new int[] { 10, 7, 8, 9 },
              "Welcome to Cobbleton Gym! My rock critters don't budge. Can you move them?", "Solid work. Take the Pebble Badge, you earned it."),
            T("Swimmer Rae", 1550, 11, -1, new int[] { 14, 9, 2, 10 },
              "The tide's coming in, and so am I!", "Washed out... the gym's just ahead."),
            T("Leader Marina", 1770, 7, 1, new int[] { 14, 11, 2, 12, 14, 13 },
              "Port Ripple Gym! Let's see if you can ride the waves.", "What a splash! The Wave Badge is yours."),
            T("Ranger Ivy", 2360, 3, -1, new int[] { 4, 12, 18, 13, 6, 13 },
              "Glowwood is my patrol. Show me you respect the forest!", "You're ready for Mossbrook Gym."),
            T("Leader Fern", 2580, 8, 2, new int[] { 18, 14, 4, 16, 18, 16 },
              "Mossbrook Gym grows strong roots. Try to pull them up!", "You blossomed! Here's the Sprout Badge."),
            T("Hiker Brom", 3190, 2, -1, new int[] { 8, 16, 8, 17, 12, 17 },
              "These rocks are tougher than they look!", "Crumbled like gravel. Nice work!"),
            T("Leader Blaze", 3410, 9, 3, new int[] { 12, 18, 0, 19, 13, 20 },
              "Kilnburg Gym burns hot! Hope you brought water.", "I'm burnt out. The Flame Badge, it's yours!"),
            T("Engineer Watt", 4000, 12, -1, new int[] { 6, 20, 16, 21, 7, 22 },
              "I wired these critters myself. Prepare for a shock!", "Short circuit! Boltgate's just past here."),
            T("Leader Volta", 4220, 10, 4, new int[] { 16, 22, 7, 23, 17, 24 },
              "Boltgate Gym, the last gym! Thunder doesn't wait!", "Electrifying! The Spark Badge. Go face the Champion."),
            T("Champion Vex", 4660, 4, -1, new int[] { 11, 26, 13, 27, 15, 27, 5, 28, 7, 29 },
              "Five badges, all the way to the end of the taskbar. Let's see what you've got!", "Incredible... you are the new Taskbar Champion!"),
        };

        static TrainerDef T(string n, int x, int look, int gym, int[] team, string intro, string lose)
        {
            TrainerDef t = new TrainerDef(); t.Name = n; t.X = x; t.Look = look; t.Gym = gym; t.Team = team; t.Intro = intro; t.Lose = lose;
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
