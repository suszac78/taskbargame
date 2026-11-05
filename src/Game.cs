// Taskbar Tamer: a tiny creature-catching game that lives in a strip above the Windows taskbar.
// Written in C# 5 so Windows PowerShell's built-in compiler can build it with no installs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        class Hit { public RectangleF R; public Action A; }

        // ---------- rendering ----------
        Bitmap low = new Bitmap(VW, VH);
        Graphics lg;
        float s = 2f;
        List<Hit> hits = new List<Hit>();
        Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        Dictionary<int, Font> fonts = new Dictionary<int, Font>();
        Dictionary<string, Bitmap> sprites = new Dictionary<string, Bitmap>();
        StringFormat fmt, fmtWrap;
        string fontName = "Segoe UI Semibold";
        FontStyle fontStyle = FontStyle.Regular;

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
            using (Font test = new Font(fontName, 10))
            {
                if (test.Name != fontName) { fontName = "Segoe UI"; fontStyle = FontStyle.Bold; }
            }

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

        // ---------- rounded window corners ----------
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        bool dwmRounded;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (headless) return;
            try { int round = 2; dwmRounded = DwmSetWindowAttribute(Handle, 33, ref round, 4) == 0; } // Windows 11
            catch { dwmRounded = false; }
            ApplyRoundRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundRegion();
        }

        // Older Windows has no rounded windows, so cut the corners ourselves.
        void ApplyRoundRegion()
        {
            if (dwmRounded || headless || Width <= 0 || Height <= 0) return;
            float r = mini ? Width / 2.5f : Math.Max(6, 5 * s);
            using (GraphicsPath p = RoundRect(new RectangleF(0, 0, Width, Height), r)) Region = new Region(p);
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
        // The world (sky, ground, buildings, grass, people) is pixel art drawn on a small canvas.
        // Everything else (panels, buttons, bars, critters, text) goes on a smooth layer drawn
        // at full resolution, in the order it was queued.
        // =====================================================================
        const int K_RECT = 0, K_TEXT = 1, K_WRAP = 2, K_IMG = 3, K_ELL = 4, K_POLY = 5;
        class Ui
        {
            public int Kind; public float X, Y, W, H, R, Clip = -1, Size;
            public int Fill, Border = -1, Alpha = 255, Align;
            public string S; public Bitmap Img; public PointF[] Pts;
        }
        List<Ui> ui = new List<Ui>();

        SolidBrush Br(int rgb, int a = 255)
        {
            int key = (a << 24) | (rgb & 0xFFFFFF);
            SolidBrush b;
            if (!brushes.TryGetValue(key, out b)) { b = new SolidBrush(Data.C(rgb, a)); brushes[key] = b; }
            return b;
        }

        // pixel-art canvas
        void R(float x, float y, float w, float h, int rgb, int a = 255) { lg.FillRectangle(Br(rgb, a), x, y, w, h); }
        void Tri(float x1, float y1, float x2, float y2, float x3, float y3, int rgb)
        {
            lg.FillPolygon(Br(rgb), new PointF[] { new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3) });
        }
        void Blit(Bitmap b, float x, float y, float w, float h)
        {
            lg.DrawImage(b, new RectangleF((float)Math.Round(x), (float)Math.Round(y), w, h));
        }

        // smooth layer
        Ui Q(int kind) { Ui u = new Ui(); u.Kind = kind; ui.Add(u); return u; }
        void URect(float x, float y, float w, float h, int fill, float r = 0, int alpha = 255, int border = -1)
        {
            Ui u = Q(K_RECT); u.X = x; u.Y = y; u.W = w; u.H = h; u.Fill = fill; u.R = r; u.Alpha = alpha; u.Border = border;
        }
        void UEll(float x, float y, float w, float h, int fill, int alpha = 255, int border = -1)
        {
            Ui u = Q(K_ELL); u.X = x; u.Y = y; u.W = w; u.H = h; u.Fill = fill; u.Alpha = alpha; u.Border = border;
        }
        void UPoly(int fill, params float[] xy)
        {
            Ui u = Q(K_POLY); u.Fill = fill; u.Pts = new PointF[xy.Length / 2];
            for (int i = 0; i < u.Pts.Length; i++) u.Pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        }
        void UImg(Bitmap b, float x, float y, float w, float h, float clipY = -1)
        {
            Ui u = Q(K_IMG); u.Img = b; u.X = x; u.Y = y; u.W = w; u.H = h; u.Clip = clipY;
        }
        void Txt(string str, float x, float y, float size, int col, int align = 0)
        {
            Ui u = Q(K_TEXT); u.S = str; u.X = x; u.Y = y; u.Size = size; u.Fill = col; u.Align = align;
        }
        void TxtWrap(string str, float x, float y, float w, float size, int col)
        {
            Ui u = Q(K_WRAP); u.S = str; u.X = x; u.Y = y; u.W = w; u.Size = size; u.Fill = col;
        }

        void AddHit(RectangleF r, Action a) { Hit h = new Hit(); h.R = r; h.A = a; hits.Add(h); }

        // A rounded card with a soft shadow.
        void Panel(float x, float y, float w, float h, int fill = 0xFFFFFF, float r = 3.5f, int alpha = 245)
        {
            URect(x + 0.4f, y + 0.9f, w, h, 0x101020, r, 45);
            URect(x, y, w, h, fill, r, alpha, 0xD8DCE6);
        }

        void HpBar(float x, float y, float w, float cur, float max)
        {
            float f = max <= 0 ? 0 : Math.Max(0, Math.Min(1, cur / max));
            URect(x, y, w, 3, 0x2C3040, 1.5f);
            int col = f > 0.5f ? 0x4CD964 : f > 0.2f ? 0xFFC233 : 0xFF5A4E;
            if (f > 0) URect(x + 0.5f, y + 0.5f, Math.Max(2, (w - 1) * f), 2, col, 1);
        }

        void TypeTag(int type, float x, float y, float w = 22)
        {
            URect(x, y, w, 6.4f, Data.TypeColors[type], 3.2f);
            Txt(Data.TypeNames[type], x + w / 2, y + 1.1f, 3.9f, 0xFFFFFF, 1);
        }

        void Button(RectangleF r, string label, bool on, Action a, float size = 6.2f, int tag = -1)
        {
            URect(r.X, r.Y + 0.5f, r.Width, r.Height, on ? 0xE09020 : 0x101020, 3, on ? 90 : 25);
            URect(r.X, r.Y, r.Width, r.Height, on ? 0xFFF3D0 : 0xF6F7FA, 3, 255, on ? 0xF0A830 : 0xD8DCE6);
            float tx = r.X + 6;
            if (tag >= 0) { UEll(r.X + 4, r.Y + r.Height / 2 - 1.6f, 3.2f, 3.2f, tag); tx = r.X + 10; }
            Txt(label, tx, r.Y + (r.Height - size) / 2 - 0.5f, size, 0x1E2230);
            AddHit(r, a);
        }

        Bitmap MonSprite(int sp, bool flip)
        {
            string key = "m" + sp + (flip ? "f" : "");
            Bitmap b;
            if (sprites.TryGetValue(key, out b)) return b;
            SpeciesDef d = Data.Species[sp];
            Dictionary<char, Color> pal = new Dictionary<char, Color>();
            pal['k'] = Data.C(0x1A1A24); pal['w'] = Data.C(0xFFFFFF); pal['e'] = Data.C(0x14141C);
            pal['p'] = Data.C(0xF07888); pal['c'] = Data.C(0xFFF4DC);
            pal['1'] = Data.C(d.Pal[0]); pal['2'] = Data.C(d.Pal[1]); pal['3'] = Data.C(d.Pal[2]);
            pal['h'] = Mix(d.Pal[0], 0xFFFFFF, 0.45f); pal['4'] = Mix(d.Pal[2], 0x000000, 0.3f);
            b = Data.MakeSprite(Data.Sprites[d.Sprite], 24, pal, flip);
            sprites[key] = b;
            return b;
        }

        static Color Mix(int a, int b, float f)
        {
            Color ca = Data.C(a), cb = Data.C(b);
            return Color.FromArgb(255, (int)(ca.R + (cb.R - ca.R) * f), (int)(ca.G + (cb.G - ca.G) * f), (int)(ca.B + (cb.B - ca.B) * f));
        }

        Bitmap PersonSprite(int look, int frame, bool flip)
        {
            string key = "p" + look + "_" + frame + (flip ? "f" : "");
            Bitmap b;
            if (sprites.TryGetValue(key, out b)) return b;
            int[] lk = Data.Looks[look];
            Dictionary<char, Color> pal = new Dictionary<char, Color>();
            pal['k'] = Data.C(0x1A1A24); pal['e'] = Data.C(0x1A1A24); pal['w'] = Data.C(0xFFFFFF);
            pal['s'] = Data.C(0xF8C8A0); pal['o'] = Data.C(0x5A3A30);
            pal['3'] = Data.C(lk[0]); pal['h'] = Mix(lk[0], 0xFFFFFF, 0.4f);
            pal['b'] = Data.C(lk[1]); pal['c'] = Mix(lk[1], 0xFFFFFF, 0.35f);
            pal['d'] = Data.C(lk[2]); pal['a'] = Data.C(lk[3]); pal['g'] = Data.C(lk[4]);
            b = Data.MakeSprite(frame == 0 ? Data.Person1 : Data.Person2, 10, pal, flip);
            sprites[key] = b;
            return b;
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
            ui.Clear();
            hits.Clear();
            float sc;
            if (mini)
            {
                sc = w / 24f;
                g.Clear(Data.C(0xF6F7FA));
                DrawMini();
            }
            else
            {
                sc = w / (float)VW;
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
                        if (curMsg != null) DrawMsgBox(110, 16, 260, 28);
                    }
                    DrawChrome();
                }
                catch (Exception ex) { Txt("Draw error: " + ex.Message, 10, 20, 6, 0xFFFFFF); }
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(low, new Rectangle(0, 0, w, h));
            }
            FlushUi(g, sc);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void FlushUi(Graphics g, float sc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float bw = Math.Max(1f, sc * 0.45f);
            foreach (Ui u in ui)
            {
                RectangleF r = new RectangleF(u.X * sc, u.Y * sc, u.W * sc, u.H * sc);
                switch (u.Kind)
                {
                    case K_RECT:
                        using (GraphicsPath p = RoundRect(r, u.R * sc))
                        {
                            g.FillPath(Br(u.Fill, u.Alpha), p);
                            if (u.Border >= 0) using (Pen pen = new Pen(Data.C(u.Border), bw)) g.DrawPath(pen, p);
                        }
                        break;
                    case K_ELL:
                        g.FillEllipse(Br(u.Fill, u.Alpha), r);
                        if (u.Border >= 0) using (Pen pen = new Pen(Data.C(u.Border), bw)) g.DrawEllipse(pen, r);
                        break;
                    case K_POLY:
                        {
                            PointF[] pts = new PointF[u.Pts.Length];
                            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(u.Pts[i].X * sc, u.Pts[i].Y * sc);
                            g.FillPolygon(Br(u.Fill), pts);
                        }
                        break;
                    case K_IMG:
                        {
                            InterpolationMode im = g.InterpolationMode; PixelOffsetMode pm = g.PixelOffsetMode;
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            if (u.Clip >= 0) g.SetClip(new RectangleF(0, 0, 100000, u.Clip * sc));
                            g.DrawImage(u.Img, new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height)));
                            if (u.Clip >= 0) g.ResetClip();
                            g.InterpolationMode = im; g.PixelOffsetMode = pm;
                        }
                        break;
                    case K_TEXT:
                        {
                            Font f = GetFont(u.Size * sc);
                            float x = r.X;
                            if (u.Align != 0)
                            {
                                SizeF sz = g.MeasureString(u.S, f, PointF.Empty, fmt);
                                x -= u.Align == 1 ? sz.Width / 2 : sz.Width;
                            }
                            g.DrawString(u.S, f, Br(u.Fill), x, r.Y, fmt);
                        }
                        break;
                    case K_WRAP:
                        g.DrawString(u.S, GetFont(u.Size * sc), Br(u.Fill), new RectangleF(r.X, r.Y, r.Width, 100000), fmtWrap);
                        break;
                }
            }
        }

        Font GetFont(float px)
        {
            int key = (int)(px * 4);
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(fontName, Math.Max(4f, key / 4f), fontStyle, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        void DrawChrome()
        {
            // drag handle on the left edge
            URect(1.6f, 18, 2.4f, 28, 0xFFFFFF, 1.2f, 130);
            // minimise and close buttons
            RectangleF mn = new RectangleF(456.5f, 2, 9, 9), cl = new RectangleF(467.5f, 2, 9, 9);
            UEll(mn.X, mn.Y + 0.4f, 9, 9, 0x101020, 50);
            UEll(mn.X, mn.Y, 9, 9, 0xF6F7FA, 255, 0xC8CCD8);
            URect(mn.X + 2.6f, mn.Y + 4.1f, 3.8f, 0.9f, 0x3A3E50, 0.45f);
            UEll(cl.X, cl.Y + 0.4f, 9, 9, 0x101020, 50);
            UEll(cl.X, cl.Y, 9, 9, 0xFF5F57);
            float cx = cl.X + 4.5f, cy = cl.Y + 4.5f, a = 1.7f, t2 = 0.45f;
            UPoly(0xFFFFFF, cx - a - t2, cy - a + t2, cx - a + t2, cy - a - t2, cx + a + t2, cy + a - t2, cx + a - t2, cy + a + t2);
            UPoly(0xFFFFFF, cx + a - t2, cy - a - t2, cx + a + t2, cy - a + t2, cx - a + t2, cy + a + t2, cx - a - t2, cy + a - t2);
            AddHit(mn, delegate { SetMini(true); });
            AddHit(cl, delegate { Close(); });
        }

        void DrawMini()
        {
            if (party.Count == 0) return;
            Mon m = party[0];
            float bob = (float)Math.Round(Math.Sin(t * 3) * 0.6);
            UImg(MonSprite(m.Sp, false), 3, 0.5f + bob, 18, 18);
            HpBar(3.5f, 19.5f, 17, m.Hp, m.MaxHp);
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
                R(sx - 7, 33, 14, 7, 0x6A4A30); R(sx - 6, 34, 12, 5, 0xE8D0A0);
                Tri(sx + 1, 34.5f, sx + 1, 38.5f, sx + 4, 36.5f, 0x6A4A30);
                R(sx - 4, 36, 5, 1, 0x6A4A30);
            }

            foreach (BuildingDef b in Data.Buildings) DrawBuilding(b, cam);

            int kind = Data.ZoneAt(px).Kind;
            DrawGrass(cam, false);

            // professor before you have a partner
            if (party.Count == 0) Blit(PersonSprite(5, 0, true), 172 - cam - 5, 41, 10, 16);

            for (int i = 0; i < Data.Trainers.Length; i++)
            {
                TrainerDef tr = Data.Trainers[i];
                float sx = tr.X - cam;
                if (sx < -10 || sx > VW + 10) continue;
                Blit(PersonSprite(tr.Look, 0, true), sx - 5, 41, 10, 16);
                if (tr.Champion) { R(sx - 2, 39, 5, 2, 0xF0C030); R(sx - 2, 38, 1, 1, 0xF0C030); R(sx, 38, 1, 1, 0xF0C030); R(sx + 2, 38, 1, 1, 0xF0C030); }
                if (!Beaten(i) && Math.Abs(px - tr.X) < 80 && mode == M_WORLD)
                {
                    float bob = (float)Math.Round(Math.Sin(t * 6) * 0.6, 1);
                    URect(sx - 3.5f, 29 + bob, 8, 8, 0xFFFFFF, 2.5f, 255, 0xD8DCE6);
                    Txt("!", sx + 0.5f, 29.6f + bob, 6.5f, 0xFF4040, 1);
                }
            }

            // player
            {
                int frame = walking && ((int)(walkAnim / 0.14f)) % 2 == 1 ? 1 : 0;
                float bob = walking && frame == 1 ? -1 : 0;
                Blit(PersonSprite(0, frame, facing < 0), px - cam - 5, 41 + bob, 10, 16);
            }

            DrawGrass(cam, true);

            // night tint
            if (TimeOfDay() == 2 && kind != 4) R(0, 0, VW, VH, 0x101040, 60);

            // building signs (smooth layer)
            foreach (BuildingDef b in Data.Buildings)
            {
                float x = b.X - cam;
                if (x < -40 || x > VW + 40) continue;
                if (b.Kind == 0) { URect(x - 10, 15.5f, 20, 7, 0xFFFFFF, 2, 255, 0xE04848); Txt("REST", x, 16.3f, 4.6f, 0xD03030, 1); }
                else if (b.Kind == 1) { URect(x - 10, 20.5f, 20, 6.5f, 0x2F5FC0, 2); Txt("SHOP", x, 21.2f, 4.6f, 0xFFFFFF, 1); }
            }

            // interact hint
            if (mode == M_WORLD && Idle && party.Count > 0)
            {
                int b = NearBuilding();
                float hx = -100, hy = 3;
                if (b >= 0) hx = Data.Buildings[b].X - cam;
                else if (NearChampion()) { hx = Data.Trainers[Data.Trainers.Length - 1].X - cam; hy = 27; }
                if (hx > -50)
                {
                    float bob = (float)Math.Round(Math.Sin(t * 5) * 0.8, 1);
                    URect(hx - 5, hy + bob, 10, 9, 0x1E2230, 3, 230);
                    Txt("Z", hx, hy + 1.2f + bob, 6, 0xFFFFFF, 1);
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
                    float mx = Mod(400 - cam * 0.02f, VW);
                    lg.FillEllipse(Br(0xF4F0D0), mx, 4, 8, 8);
                    lg.FillEllipse(Br(sky[0]), mx + 2.5f, 2.5f, 7, 7);
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
                        float top = 42 + Hash((int)(x + cam)) % 3;
                        Tri(x, 50, x + 2 + sway, top, x + 4, 50, dark);
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
                R(x - 20, 24, 40, 25, 0x504048); R(x - 19, 25, 38, 24, 0xF4F0E8);
                Tri(x - 24, 25, x, 11, x + 24, 25, 0xD04040);
                R(x - 22, 24, 44, 2, 0xA02828);
                R(x - 5, 37, 10, 12, 0x6A4A3A); R(x - 4, 38, 8, 11, 0x8A6A50);
                R(x - 16, 30, 7, 2, 0xE03838); R(x - 13.5f, 27.5f, 2, 7, 0xE03838);
                R(x + 10, 29, 6, 5, 0x88C8F0); R(x + 10, 31, 6, 1, 0x504048);
            }
            else if (b.Kind == 1)
            {
                R(x - 18, 26, 36, 23, 0x404858); R(x - 17, 27, 34, 22, 0xE8E8F0);
                R(x - 21, 20, 42, 7, 0x3060C0);
                for (int i = 0; i < 7; i++) R(x - 21 + i * 6, 26, 3, 3, i % 2 == 0 ? 0x3060C0 : 0xFFFFFF);
                R(x - 5, 37, 10, 12, 0x404858); R(x - 4, 38, 8, 11, 0x80A0D0);
                R(x - 15, 32, 7, 6, 0x88C8F0); R(x + 8, 32, 7, 6, 0x88C8F0);
            }
            else
            {
                R(x - 17, 27, 34, 22, 0x604030); R(x - 16, 28, 32, 21, 0xF0E0C0);
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
            Panel(9, 2, 118, 12, 0xFFFFFF, 6, 235);
            UImg(MonSprite(m.Sp, false), 10.5f, 2.6f, 10.5f, 10.5f);
            Txt(m.Name, 23, 3.6f, 5.6f, 0x1E2230);
            Txt("Lv" + m.Level, 76, 4.2f, 4.6f, 0x7A8090, 2);
            HpBar(80, 6.6f, 43, m.DispHp, m.MaxHp);
            AddHit(new RectangleF(9, 2, 118, 12), delegate { if (Idle) OpenTeam(); });

            Panel(318, 2, 134, 12, 0xFFFFFF, 6, 235);
            UEll(323, 4.6f, 6, 6, 0xF5B800, 255, 0xC08800); UEll(324.8f, 6.2f, 2.4f, 2.4f, 0xFFE070);
            Txt(coins.ToString(), 331, 3.6f, 5.6f, 0x1E2230);
            UEll(359, 4.6f, 6, 6, 0xFF4D4D, 255, 0x2A2A38); URect(359.3f, 7.3f, 5.4f, 0.6f, 0x2A2A38);
            UEll(361.2f, 6.8f, 1.6f, 1.6f, 0xFFFFFF, 255, 0x2A2A38);
            Txt("Orbs " + orbs, 367, 3.6f, 5.6f, 0x1E2230);
            URect(401, 4.4f, 5.6f, 6.4f, 0x4AA8F0, 1.6f); URect(403.3f, 5.6f, 1, 4, 0xFFFFFF); URect(401.8f, 7.1f, 4, 1, 0xFFFFFF);
            Txt("Potions " + potions, 409, 3.6f, 5.6f, 0x1E2230);

            if (bannerT > 0 && lastZone != null)
            {
                int a = (int)(Math.Min(1, bannerT) * 220);
                URect(185, 17, 110, 11, 0x14182A, 5.5f, a);
                Txt(lastZone.Name, 240, 18.4f, 6.6f, 0xFFFFFF, 1);
            }
            else if (hintT > 0 && curMsg == null)
            {
                URect(148, 56.5f, 184, 6.5f, 0x14182A, 3.2f, 170);
                Txt("Arrows: walk  |  Z: talk  |  Tab: team  |  H: help", 240, 57.4f, 4.6f, 0xFFFFFF, 1);
            }
            if (crashNote != null) Txt("Oops: " + crashNote, 132, 4, 5, 0xE03030);
        }

        void DrawMsgBox(float x, float y, float w, float h)
        {
            Panel(x, y, w, h, 0xFFFFFF, 4, 250);
            TxtWrap(curMsg, x + 6, y + 4, w - 14, 6.8f, 0x1E2230);
            float bob = (float)Math.Round(Math.Sin(t * 6) * 0.6, 1);
            UPoly(0x8A90A0, x + w - 9, y + h - 6.5f + bob, x + w - 5, y + h - 6.5f + bob, x + w - 7, y + h - 4 + bob);
        }

        void DrawStarter()
        {
            Panel(60, 3, 360, 58, 0xF7F8FB, 6);
            Txt("Choose your partner!", 240, 5.2f, 7, 0x1E2230, 1);
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                float cx = 120 + i * 120;
                SpeciesDef d = Data.Species[Data.Starters[i]];
                bool on = cursor == i;
                RectangleF r = new RectangleF(cx - 52, 14, 104, 44);
                URect(r.X, r.Y + 0.6f, r.Width, r.Height, on ? 0xE09020 : 0x101020, 5, on ? 70 : 25);
                URect(r.X, r.Y, r.Width, r.Height, on ? 0xFFF3D0 : 0xFFFFFF, 5, 255, on ? 0xF0A830 : 0xE0E3EA);
                float bob = on ? (float)Math.Round(Math.Sin(t * 6) * 1.2, 1) : 0;
                UEll(cx - 46, 49, 32, 6, 0x101020, 30);
                UImg(MonSprite(Data.Starters[i], false), cx - 47, 17 + bob, 34, 34);
                Txt(d.Name, cx - 8, 21, 7, 0x1E2230);
                TypeTag(d.Type, cx - 8, 31, 28);
                string[] blurb = { "Fiery & fast", "Calm & sturdy", "Tough & leafy" };
                Txt(blurb[i], cx - 8, 41, 5.4f, 0x6A7080);
                AddHit(r, delegate { if (!Idle) return; if (cursor == ii) PickStarter(ii); else cursor = ii; });
            }
        }

        void DrawTeam()
        {
            Panel(8, 2, 446, 60, 0xF7F8FB, 6);
            Txt("Your team", 14, 4, 6.5f, 0x1E2230);
            Txt("Storage: " + box.Count, 60, 5, 5, 0x7A8090);
            Button(new RectangleF(254, 3.5f, 60, 9.5f), "Make lead", false, delegate { if (Idle) TeamLead(); }, 5.2f);
            Button(new RectangleF(317, 3.5f, 74, 9.5f), "Swap w/ storage", false, delegate { if (Idle) TeamStorage(); }, 5.2f);
            Button(new RectangleF(394, 3.5f, 54, 9.5f), "Close", false, delegate { if (Idle) mode = M_WORLD; }, 5.2f);
            for (int i = 0; i < 6; i++)
            {
                RectangleF r = new RectangleF(12 + i * 73.5f, 15, 70, 44);
                if (i >= party.Count) { URect(r.X, r.Y, r.Width, r.Height, 0xECEEF3, 5); continue; }
                Mon m = party[i];
                bool on = cursor == i;
                URect(r.X, r.Y + 0.6f, r.Width, r.Height, on ? 0xE09020 : 0x101020, 5, on ? 70 : 25);
                URect(r.X, r.Y, r.Width, r.Height, on ? 0xFFF3D0 : 0xFFFFFF, 5, 255, on ? 0xF0A830 : 0xE0E3EA);
                UImg(MonSprite(m.Sp, false), r.X + 1, r.Y + 3, 26, 26);
                if (i == 0) { URect(r.X + 3, r.Y + 30, 18, 5.5f, 0xF0A830, 2.7f); Txt("LEAD", r.X + 12, r.Y + 30.7f, 3.9f, 0xFFFFFF, 1); }
                Txt(m.Name, r.X + 29, r.Y + 3.5f, 5.4f, 0x1E2230);
                Txt("Lv" + m.Level, r.X + 28, r.Y + 10.5f, 5, 0x7A8090);
                TypeTag(m.Def.Type, r.X + 44, r.Y + 11, 22);
                HpBar(r.X + 28, r.Y + 20, 38, m.Hp, m.MaxHp);
                Txt(m.Hp + "/" + m.MaxHp, r.X + 28, r.Y + 24.5f, 4.6f, 0x7A8090);
                URect(r.X + 4, r.Y + 38, 62, 1.6f, 0xE0E3EA, 0.8f);
                URect(r.X + 4, r.Y + 38, Math.Max(1.6f, 62f * m.Xp / Math.Max(1, m.XpNeed)), 1.6f, 0x4AA8F0, 0.8f);
                int ii = i;
                AddHit(r, delegate { if (Idle) cursor = ii; });
            }
        }

        void DrawShop()
        {
            Panel(130, 15, 220, 44, 0xF7F8FB, 6);
            Txt("Shop", 137, 17.5f, 7, 0x1E2230);
            Txt("You have " + coins + " coins", 343, 18.5f, 5.6f, 0x7A8090, 2);
            string[] labels = { "Orb  10c", "Potion  15c", "Leave" };
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                Button(new RectangleF(137 + i * 69.5f, 29, 66, 13), labels[i], cursor == i, delegate { cursor = ii; ShopPick(ii); });
            }
            string[] desc = { "Throw at a weak wild critter to catch it.", "Heals 30 HP during a battle.", "See you next time!" };
            Txt(desc[cursor], 240, 47, 5.4f, 0x6A7080, 1);
        }

        // ---------- battle ----------
        void DrawBattle()
        {
            DrawBackground(battleKind, Cam(), true);

            // platforms
            UEll(18, 52, 66, 10, 0x000000, 45);
            UEll(395, 52, 66, 10, 0x000000, 45);

            // foe
            if (foeIdx < foes.Count)
            {
                Mon f = F;
                if (!foeHidden)
                {
                    float off = fLunge > 0 ? -(float)Math.Sin(fLunge / 0.35f * Math.PI) * 10 : 0;
                    off += fSlide * 80;
                    DrawMon(f, 428 + off, 58, true, fFlash, fFaint);
                }
                if (orbT > 0 || caught)
                {
                    float wob = caught ? 0 : (float)Math.Sin(orbT * 9) * (orbT < 0.35f ? 0 : 2);
                    float oy = orbT < 0.35f ? 30 + (orbT / 0.35f) * 22 : 52;
                    DrawOrb(428 + wob, oy);
                    if (caught && ((int)(t * 6)) % 2 == 0) { UEll(418, 40, 2, 2, 0xFFF080); UEll(437, 42, 2, 2, 0xFFF080); UEll(428, 36, 2, 2, 0xFFF080); }
                }
                Panel(282, 3, 110, 22, 0xFFFFFF, 4);
                Txt(f.Name, 287, 4.6f, 6.4f, 0x1E2230);
                Txt("Lv" + f.Level, 387, 5.4f, 5.2f, 0x7A8090, 2);
                TypeTag(f.Def.Type, 287, 14, 24);
                Txt("HP", 314, 13.6f, 4.8f, 0xF0A030);
                HpBar(323, 15.2f, 64, f.DispHp, f.MaxHp);
                if (curTr != null)
                    for (int i = 0; i < foes.Count; i++)
                        UEll(350 + i * 5, 20.2f, 3, 3, foes[i].Hp > 0 ? 0xFF5A4E : 0xC0C4CE);
            }

            // player critter
            Mon p = P;
            {
                float off = pLunge > 0 ? (float)Math.Sin(pLunge / 0.35f * Math.PI) * 10 : 0;
                float intro = Math.Max(0, 1 - introT * 2.5f) * 80;
                DrawMon(p, 51 + off - intro, 58, false, pFlash, pFaint);
                if (evoFlash > 0) UEll(14, 4, 74, 56, 0xFFFFFF, (int)(evoFlash * 230));
            }
            Panel(86, 3, 112, 24, 0xFFFFFF, 4);
            Txt(p.Name, 91, 4.6f, 6.4f, 0x1E2230);
            Txt("Lv" + p.Level, 193, 5.4f, 5.2f, 0x7A8090, 2);
            Txt("HP", 91, 13.6f, 4.8f, 0xF0A030);
            HpBar(100, 15.2f, 62, p.DispHp, p.MaxHp);
            Txt((int)Math.Ceiling(p.DispHp) + "/" + p.MaxHp, 193, 13.4f, 4.8f, 0x5A6070, 2);
            URect(91, 22.4f, 102, 1.6f, 0xE0E3EA, 0.8f);
            URect(91, 22.4f, Math.Max(1.6f, 102f * p.Xp / Math.Max(1, p.XpNeed)), 1.6f, 0x4AA8F0, 0.8f);

            // message / menu area
            float bx = 100, by = 29, bw = 278, bh = 33;
            if (curMsg != null) DrawMsgBox(bx, by, bw, bh);
            else if (Idle) DrawBattleMenu(bx, by, bw, bh);
            else Panel(bx, by, bw, bh, 0xFFFFFF, 4, 250);

            // intro fade
            if (introT < 0.4f) URect(0, 0, VW, VH, 0x000000, 0, (int)((1 - introT / 0.4f) * 255));
        }

        void DrawOrb(float cx, float cy)
        {
            UEll(cx - 4, cy - 4, 8, 8, 0xFF4D4D, 255, 0x1A1A24);
            UPoly(0xFFFFFF, cx - 3.6f, cy + 0.4f, cx + 3.6f, cy + 0.4f, cx + 2.6f, cy + 2.8f, cx, cy + 3.6f, cx - 2.6f, cy + 2.8f);
            URect(cx - 3.8f, cy - 0.4f, 7.6f, 0.9f, 0x1A1A24);
            UEll(cx - 1.3f, cy - 1.3f, 2.6f, 2.6f, 0xFFFFFF, 255, 0x1A1A24);
        }

        void DrawMon(Mon m, float cx, float baseY, bool flip, float flash, float faint)
        {
            if (flash > 0 && ((int)(flash * 16)) % 2 == 0) return;
            if (faint >= 1) return;
            float size = m.Def.Big ? 54 : 46;
            float bob = (float)Math.Round(Math.Sin(t * 3 + (flip ? 1 : 0)) * 0.8, 1);
            float y = baseY - size + 1 + bob + faint * size;
            UImg(MonSprite(m.Sp, flip), cx - size / 2, y, size, size, faint > 0 ? baseY : -1);
        }

        void DrawBattleMenu(float x, float y, float w, float h)
        {
            Panel(x, y, w, h, 0xFFFFFF, 4, 250);
            if (phase == PH_MENU)
            {
                TxtWrap("What will " + P.Name + " do?", x + 6, y + 4, 96, 6.8f, 0x1E2230);
                string[] l = { "FIGHT", "BAG", "TEAM", "RUN" };
                for (int i = 0; i < 4; i++)
                {
                    int ii = i;
                    RectangleF r = new RectangleF(x + 108 + (i % 2) * 84, y + 3.5f + (i / 2) * 13.5f, 81, 12);
                    Button(r, l[i], cursor == i, delegate { cursor = ii; BattlePick(ii); });
                }
            }
            else if (phase == PH_FIGHT)
            {
                for (int i = 0; i < P.Moves.Count; i++)
                {
                    int ii = i;
                    MoveDef mv = Data.Moves[P.Moves[i]];
                    RectangleF r = new RectangleF(x + 4 + (i % 2) * 92, y + 3.5f + (i / 2) * 13.5f, 89, 12);
                    Button(r, mv.Name, cursor == i, delegate { cursor = ii; BattlePick(ii); }, 6.2f, Data.TypeColors[mv.Type]);
                }
                if (cursor < P.Moves.Count)
                {
                    MoveDef mv = Data.Moves[P.Moves[cursor]];
                    TypeTag(mv.Type, x + 192, y + 4.5f, 30);
                    Txt("Power " + mv.Power, x + 192, y + 13.5f, 5.2f, 0x5A6070);
                    Txt("Accuracy " + mv.Acc, x + 192, y + 20.5f, 5.2f, 0x5A6070);
                    double e = Data.Eff(mv.Type, F.Def.Type);
                    if (e != 1) Txt(e > 1 ? "Super effective!" : "Not very effective", x + 226, y + 5, 4.6f, e > 1 ? 0x2FA84F : 0xD05040);
                }
                Button(new RectangleF(x + w - 34, y + h - 12.5f, 30, 9.5f), "Back", false, delegate { BattleBack(); }, 5);
            }
            else if (phase == PH_BAG)
            {
                string[] l = { "Orb x" + orbs, "Potion x" + potions, "Back" };
                for (int i = 0; i < 3; i++)
                {
                    int ii = i;
                    Button(new RectangleF(x + 4 + i * 91, y + 3.5f, 88, 12), l[i], cursor == i, delegate { cursor = ii; BattlePick(ii); });
                }
                string[] d = { curTr != null ? "Can't catch a trainer's critter." : "Catch the wild critter. Works best at low HP.", "Heal 30 HP. Uses your turn.", "" };
                Txt(d[Math.Min(cursor, 2)], x + 7, y + 21, 5.4f, 0x6A7080);
            }
            else if (phase == PH_TEAM)
            {
                int n = party.Count + (forcedSwitch ? 0 : 1);
                for (int i = 0; i < n; i++)
                {
                    int ii = i;
                    RectangleF r = new RectangleF(x + 4 + (i % 4) * 68.5f, y + 3.5f + (i / 4) * 13.5f, 66, 12);
                    if (i >= party.Count) { Button(r, "Back", cursor == i, delegate { cursor = ii; BattlePick(ii); }, 5.4f); continue; }
                    Mon m = party[i];
                    Button(r, m.Name, cursor == i, delegate { cursor = ii; BattlePick(ii); }, 5.2f, m.Hp > 0 ? (i == act ? 0xF0A030 : 0x4CD964) : 0xB0B4C0);
                }
                if (forcedSwitch) Txt("Pick who goes next!", x + w - 6, y + h - 9, 5.4f, 0xD05040, 2);
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
