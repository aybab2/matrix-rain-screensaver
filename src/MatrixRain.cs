// Matrix Rain screensaver - green "digital rain" on every monitor at once.
// Plain C# 5 / .NET Framework 4.x WinForms + GDI. Built by build.ps1 with the csc.exe that ships with Windows.
//
// Screensaver arguments (sent by Windows):
//   /s            run full screen on all monitors, exit on mouse move / click / key
//   /p <hwnd>     draw the small preview inside the Screen Saver Settings dialog
//   /c[:hwnd]     show the settings dialog (also used when started with no arguments)
// Extra (for testing only):
//   /snapshot <file.png> <width> <height> <frames>   render offscreen, save a PNG and a timing .txt

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Matrix Rain Screensaver")]
[assembly: System.Reflection.AssemblyProduct("Matrix Rain Screensaver")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace MatrixRain
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr dc, int x, int y, int w, int h, int rop);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public const int SRCCOPY = 0x00CC0020;
        public const int BLACKNESS = 0x00000042;
        public const int GWL_STYLE = -16;
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
    }

    // ------------------------------------------------------------------ settings
    class Settings
    {
        const string KeyPath = @"Software\MatrixRainScreensaver";
        public int Speed = 5;      // 1..10
        public int Density = 6;    // 1..10
        public int Size = 5;       // 1..10 (character size)
        public string Color = "Green";

        public static readonly string[] ColorNames = { "Green", "Cyan", "Blue", "Purple", "Red", "Amber", "White" };

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k != null)
                    {
                        s.Speed = Clamp(Convert.ToInt32(k.GetValue("Speed", s.Speed)), 1, 10);
                        s.Density = Clamp(Convert.ToInt32(k.GetValue("Density", s.Density)), 1, 10);
                        s.Size = Clamp(Convert.ToInt32(k.GetValue("Size", s.Size)), 1, 10);
                        string c = Convert.ToString(k.GetValue("Color", s.Color));
                        if (Array.IndexOf(ColorNames, c) >= 0) s.Color = c;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                k.SetValue("Speed", Speed, RegistryValueKind.DWord);
                k.SetValue("Density", Density, RegistryValueKind.DWord);
                k.SetValue("Size", Size, RegistryValueKind.DWord);
                k.SetValue("Color", Color, RegistryValueKind.String);
            }
        }

        public Color BaseColor()
        {
            switch (Color)
            {
                case "Cyan": return System.Drawing.Color.FromArgb(0, 235, 255);
                case "Blue": return System.Drawing.Color.FromArgb(40, 140, 255);
                case "Purple": return System.Drawing.Color.FromArgb(185, 80, 255);
                case "Red": return System.Drawing.Color.FromArgb(255, 45, 45);
                case "Amber": return System.Drawing.Color.FromArgb(255, 176, 0);
                case "White": return System.Drawing.Color.FromArgb(220, 220, 220);
                default: return System.Drawing.Color.FromArgb(20, 255, 70);
            }
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    // ------------------------------------------------------------------ renderer
    // Draws the rain into an off-screen GDI bitmap. Only the cells that change are touched each
    // frame (blits from a pre-rendered glyph atlas), so CPU cost stays low even on 3440x1440.
    class RainRenderer : IDisposable
    {
        const int Levels = 22; // 0 = head (near white), 1..Levels-1 = fading trail colour

        static readonly string Glyphs = BuildGlyphs();

        public readonly int Width, Height;
        readonly int cw, ch, cols, rows, nGlyphs;
        readonly int[] grid;      // glyph index per cell
        readonly float[] head;    // head position (in rows) per column
        readonly int[] drawnHead; // last integer head row drawn
        readonly float[] speed;   // rows per frame
        readonly int[] trail;     // trail length (rows)
        readonly int[] delay;     // frames to wait before the next drop starts
        readonly Random rnd;
        readonly float speedFactor;
        readonly int maxDelay;

        IntPtr backDC, backBmp, oldBack, atlasDC, atlasBmp, oldAtlas;

        static string BuildGlyphs()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (char c = 'ｦ'; c <= 'ﾝ'; c++) sb.Append(c); // half-width katakana
            sb.Append("0123456789");
            sb.Append("Z:.=*+-<>|\"");
            return sb.ToString();
        }

        public RainRenderer(int width, int height, int cellHeight, Settings s, int seed)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            ch = Math.Max(5, cellHeight);
            cw = Math.Max(4, (int)Math.Round(ch * 0.68));
            cols = (Width + cw - 1) / cw;
            rows = (Height + ch - 1) / ch;
            nGlyphs = Glyphs.Length;
            rnd = new Random(seed);
            speedFactor = 0.35f + s.Speed * 0.16f;          // 0.51 .. 1.95
            maxDelay = 8 + (10 - s.Density) * 14;              // frames

            grid = new int[cols * rows];
            for (int i = 0; i < grid.Length; i++) grid[i] = rnd.Next(nGlyphs);
            head = new float[cols];
            drawnHead = new int[cols];
            speed = new float[cols];
            trail = new int[cols];
            delay = new int[cols];
            dirtyTop = new int[cols];
            dirtyBottom = new int[cols];
            for (int c = 0; c < cols; c++) { dirtyTop[c] = -1; dirtyBottom[c] = -1; }
            for (int c = 0; c < cols; c++)
            {
                NewDrop(c);
                // Stagger the first drops so the screen fills in naturally.
                head[c] = -rnd.Next(rows + 1);
                drawnHead[c] = (int)Math.Floor(head[c]);
                delay[c] = rnd.Next(maxDelay);
            }

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            backDC = Native.CreateCompatibleDC(screen);
            backBmp = Native.CreateCompatibleBitmap(screen, Width, Height);
            oldBack = Native.SelectObject(backDC, backBmp);
            Native.PatBlt(backDC, 0, 0, Width, Height, Native.BLACKNESS);
            atlasDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            BuildAtlas(s.BaseColor());
        }

        void NewDrop(int c)
        {
            speed[c] = (0.30f + (float)rnd.NextDouble() * 0.70f) * speedFactor;
            int minLen = Math.Max(4, rows / 6);
            int maxLen = Math.Max(minLen + 2, (int)(rows * 0.85));
            trail[c] = rnd.Next(minLen, maxLen);
            head[c] = -1;
            drawnHead[c] = -1;
            delay[c] = rnd.Next(maxDelay);
        }

        static string PickFont()
        {
            string[] prefs = { "MS Gothic", "Yu Gothic", "MS UI Gothic", "Yu Gothic UI", "Meiryo", "Consolas" };
            foreach (string p in prefs)
            {
                try
                {
                    using (FontFamily f = new FontFamily(p)) { if (f.Name == p) return p; }
                }
                catch { }
            }
            return FontFamily.GenericMonospace.Name;
        }

        void BuildAtlas(Color baseColor)
        {
            Color headColor = Color.FromArgb(
                baseColor.R + (255 - baseColor.R) * 4 / 5,
                baseColor.G + (255 - baseColor.G) * 4 / 5,
                baseColor.B + (255 - baseColor.B) * 4 / 5);

            using (Bitmap bmp = new Bitmap(nGlyphs * cw, Levels * ch, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font font = new Font(PickFont(), ch * 0.82f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Black);
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter |
                                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                        TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;
                for (int lvl = 0; lvl < Levels; lvl++)
                {
                    Color col;
                    if (lvl == 0) col = headColor;
                    else
                    {
                        double t = (lvl - 1) / (double)(Levels - 1);     // 0 (bright) .. ~1 (dark)
                        double b = Math.Pow(1.0 - t, 1.15) * 0.92 + 0.08;
                        if (lvl == 1) b = 1.0;
                        col = Color.FromArgb((int)(baseColor.R * b), (int)(baseColor.G * b), (int)(baseColor.B * b));
                    }
                    for (int i = 0; i < nGlyphs; i++)
                    {
                        Rectangle r = new Rectangle(i * cw, lvl * ch, cw, ch);
                        TextRenderer.DrawText(g, Glyphs[i].ToString(), font, r, col, Color.Black, flags);
                    }
                }
                atlasBmp = bmp.GetHbitmap(Color.Black);
            }
            oldAtlas = Native.SelectObject(atlasDC, atlasBmp);
        }

        int[] dirtyTop, dirtyBottom; // per column rows touched since last blit (-1 = clean)

        void DrawCell(int c, int r, int level)
        {
            if (r < 0 || r >= rows) return;
            if (dirtyTop[c] < 0 || r < dirtyTop[c]) dirtyTop[c] = r;
            if (r > dirtyBottom[c]) dirtyBottom[c] = r;
            int x = c * cw, y = r * ch;
            if (level < 0)
                Native.PatBlt(backDC, x, y, cw, ch, Native.BLACKNESS);
            else
                Native.BitBlt(backDC, x, y, cw, ch, atlasDC, grid[c * rows + r] * cw, level * ch, Native.SRCCOPY);
        }

        int LevelFor(int dist, int len)
        {
            if (dist == 0) return 0;
            if (dist > len) return -1;
            int lvl = 1 + (dist - 1) * (Levels - 1) / len;
            return lvl >= Levels ? Levels - 1 : lvl;
        }

        public void Step()
        {
            for (int c = 0; c < cols; c++)
            {
                if (delay[c] > 0) { delay[c]--; continue; }
                head[c] += speed[c];
                int hi = (int)Math.Floor(head[c]);
                int old = drawnHead[c];
                if (hi == old) continue;

                // New random characters enter as the head moves down.
                for (int r = Math.Max(old + 1, 0); r <= hi && r < rows; r++)
                    grid[c * rows + r] = rnd.Next(nGlyphs);

                int len = trail[c];
                int top = hi - len - (hi - old);
                int from = Math.Max(top, 0), to = Math.Min(hi, rows - 1);
                for (int r = from; r <= to; r++)
                {
                    int lvl = LevelFor(hi - r, len);
                    // Skip cells already showing the right brightness (unchanged since last draw).
                    if (r <= old && old >= 0 && LevelFor(old - r, len) == lvl) continue;
                    DrawCell(c, r, lvl);
                }
                drawnHead[c] = hi;

                if (hi - len > rows) NewDrop(c);
            }

            // Characters inside trails flicker to new symbols now and then.
            int flickers = Math.Max(1, cols / 6);
            for (int i = 0; i < flickers; i++)
            {
                int c = rnd.Next(cols);
                int hi = drawnHead[c];
                if (delay[c] > 0 || hi < 0) continue;
                int dist = 1 + rnd.Next(trail[c]);
                int r = hi - dist;
                if (r < 0 || r >= rows) continue;
                grid[c * rows + r] = rnd.Next(nGlyphs);
                DrawCell(c, r, LevelFor(dist, trail[c]));
            }
        }

        public void BlitTo(IntPtr dc)
        {
            Native.BitBlt(dc, 0, 0, Width, Height, backDC, 0, 0, Native.SRCCOPY);
        }

        // Copy only the column strips that changed this frame (much cheaper than a full-screen copy).
        public void BlitDirtyTo(IntPtr dc)
        {
            for (int c = 0; c < cols; c++)
            {
                if (dirtyTop[c] < 0) continue;
                int x = c * cw, y = dirtyTop[c] * ch;
                int hgt = (dirtyBottom[c] - dirtyTop[c] + 1) * ch;
                Native.BitBlt(dc, x, y, cw, hgt, backDC, x, y, Native.SRCCOPY);
                dirtyTop[c] = -1; dirtyBottom[c] = -1;
            }
        }

        public void SavePng(string path)
        {
            Native.SelectObject(backDC, oldBack);
            using (Bitmap b = Image.FromHbitmap(backBmp)) b.Save(path, ImageFormat.Png);
            oldBack = Native.SelectObject(backDC, backBmp);
        }

        public void Dispose()
        {
            if (backDC != IntPtr.Zero)
            {
                Native.SelectObject(backDC, oldBack);
                Native.DeleteObject(backBmp);
                Native.DeleteDC(backDC);
                backDC = IntPtr.Zero;
            }
            if (atlasDC != IntPtr.Zero)
            {
                Native.SelectObject(atlasDC, oldAtlas);
                Native.DeleteObject(atlasBmp);
                Native.DeleteDC(atlasDC);
                atlasDC = IntPtr.Zero;
            }
        }
    }

    // ------------------------------------------------------------------ window per monitor
    class RainForm : Form
    {
        readonly Settings settings;
        readonly int seed;
        readonly bool preview;
        readonly Rectangle targetBounds;
        RainRenderer renderer;

        public RainForm(Rectangle bounds, Settings s, int seed, bool preview)
        {
            settings = s;
            this.seed = seed;
            this.preview = preview;
            targetBounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            TopMost = !preview;
            Text = "Matrix Rain";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!preview) Bounds = targetBounds; // re-apply in case a DPI change nudged it
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0, WM_ACTIVATEAPP = 0x001C;
            if (m.Msg == WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // keep exact monitor bounds
            if (!preview && m.Msg == WM_ACTIVATEAPP && m.WParam == IntPtr.Zero) Program.Quit();
            base.WndProc(ref m);
        }

        int CellHeight()
        {
            if (preview) return Math.Max(5, ClientSize.Height / 16);
            uint dpi = 96;
            try { dpi = Native.GetDpiForWindow(Handle); } catch { }
            if (dpi == 0) dpi = 96;
            double px = 10 + settings.Size * 2.4; // size 5 -> 22 px at 100% scaling
            return (int)Math.Round(px * dpi / 96.0);
        }

        void EnsureRenderer()
        {
            Size sz = ClientSize;
            if (renderer != null && renderer.Width == sz.Width && renderer.Height == sz.Height) return;
            if (renderer != null) renderer.Dispose();
            renderer = new RainRenderer(sz.Width, sz.Height, CellHeight(), settings, seed);
        }

        public void Tick()
        {
            if (!IsHandleCreated || IsDisposed) return;
            EnsureRenderer();
            renderer.Step();
            IntPtr dc = Native.GetDC(Handle);
            renderer.BlitDirtyTo(dc);
            Native.ReleaseDC(Handle, dc);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (renderer == null) { e.Graphics.Clear(Color.Black); return; }
            IntPtr dc = e.Graphics.GetHdc();
            renderer.BlitTo(dc);
            e.Graphics.ReleaseHdc(dc);
        }

        protected override void Dispose(bool disposing)
        {
            if (renderer != null) { renderer.Dispose(); renderer = null; }
            base.Dispose(disposing);
        }
    }

    // Closes the screensaver on any real user input, from whichever monitor window gets it.
    class InputWatcher : IMessageFilter
    {
        readonly Point start;
        public InputWatcher() { start = Cursor.Position; }

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0200: // WM_MOUSEMOVE
                    Point p = Cursor.Position;
                    if (Math.Abs(p.X - start.X) > 10 || Math.Abs(p.Y - start.Y) > 10) Program.Quit();
                    break;
                case 0x0100: case 0x0104:                 // key down / sys key down
                case 0x0201: case 0x0204: case 0x0207:    // L/R/M button down
                case 0x020B: case 0x020A: case 0x020E:    // X button, wheel, h-wheel
                    Program.Quit();
                    return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ settings dialog
    class SettingsForm : Form
    {
        readonly Settings s;
        TrackBar speed, density, size;
        ComboBox color;

        public SettingsForm(Settings s)
        {
            this.s = s;
            Text = "Matrix Rain - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(380, 300);

            int y = 14;
            speed = AddSlider("Falling speed", s.Speed, ref y);
            density = AddSlider("Amount of rain", s.Density, ref y);
            size = AddSlider("Character size", s.Size, ref y);

            Label cl = new Label { Text = "Colour", Location = new Point(16, y + 4), AutoSize = true };
            color = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(140, y), Width = 220 };
            color.Items.AddRange(Settings.ColorNames);
            color.SelectedItem = s.Color;
            Controls.Add(cl); Controls.Add(color);
            y += 40;

            Label info = new Label
            {
                Text = "Runs on every monitor. Move the mouse or press a key to exit.",
                Location = new Point(16, y), Size = new Size(350, 20)
            };
            Controls.Add(info);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(196, 256), Width = 80 };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(284, 256), Width = 80 };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ok.Click += delegate
            {
                s.Speed = speed.Value; s.Density = density.Value; s.Size = size.Value;
                s.Color = (string)color.SelectedItem ?? "Green";
                try { s.Save(); } catch (Exception ex) { MessageBox.Show("Could not save settings: " + ex.Message); }
            };
        }

        TrackBar AddSlider(string label, int value, ref int y)
        {
            Label l = new Label { Text = label, Location = new Point(16, y + 8), AutoSize = true };
            TrackBar t = new TrackBar
            {
                Minimum = 1, Maximum = 10, Value = Settings.Clamp(value, 1, 10), TickFrequency = 1,
                Location = new Point(132, y), Width = 236
            };
            Controls.Add(l); Controls.Add(t);
            y += 56;
            return t;
        }
    }

    // ------------------------------------------------------------------ entry point
    static class Program
    {
        static bool quitting;

        public static void Quit()
        {
            if (quitting) return;
            quitting = true;
            Application.Exit();
        }

        static void ParseArgs(string[] args, out string mode, out string value, out int nextIndex)
        {
            mode = "c"; value = null; nextIndex = 1;
            if (args.Length == 0) return;
            string a = args[0].Trim().ToLowerInvariant();
            if (a.StartsWith("/") || a.StartsWith("-")) a = a.Substring(1);
            int colon = a.IndexOf(':');
            if (colon >= 0) { value = a.Substring(colon + 1); a = a.Substring(0, colon); }
            else if (args.Length > 1) { value = args[1]; nextIndex = 2; }
            mode = a;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string mode, value; int next;
            ParseArgs(args, out mode, out value, out next);

            if (mode == "s" || mode == "p" || mode == "snapshot")
            {
                bool ok = false;
                try { ok = Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } // per-monitor v2
                if (!ok) { try { Native.SetProcessDPIAware(); } catch { } }
            }
            else
            {
                try { Native.SetProcessDPIAware(); } catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings settings = Settings.Load();

            switch (mode)
            {
                case "s": return RunFullScreen(settings);
                case "p": return RunPreview(settings, value);
                case "snapshot": return RunSnapshot(settings, args);
                default:
                    using (SettingsForm f = new SettingsForm(settings)) f.ShowDialog();
                    return 0;
            }
        }

        static int RunFullScreen(Settings settings)
        {
            bool created;
            using (Mutex m = new Mutex(true, "MatrixRainScreensaver_FullScreen", out created))
            {
                if (!created) return 0;

                List<RainForm> forms = new List<RainForm>();
                int seed = Environment.TickCount;
                foreach (Screen sc in Screen.AllScreens)
                    forms.Add(new RainForm(sc.Bounds, settings, seed++, false));

                foreach (RainForm f in forms) f.Show();
                if (forms.Count > 0) forms[0].Activate();
                Cursor.Hide();
                Application.AddMessageFilter(new InputWatcher());

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps
                timer.Tick += delegate { foreach (RainForm f in forms) f.Tick(); };
                timer.Start();

                Application.Run();

                timer.Stop();
                foreach (RainForm f in forms) f.Dispose();
                Cursor.Show();
            }
            return 0;
        }

        static int RunPreview(Settings settings, string value)
        {
            long h;
            if (value == null || !long.TryParse(value, out h) || h == 0) return 0;
            IntPtr parent = new IntPtr(h);
            if (!Native.IsWindow(parent)) return 0;

            Native.RECT rc;
            Native.GetClientRect(parent, out rc);
            Rectangle bounds = new Rectangle(0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top);

            RainForm f = new RainForm(bounds, settings, Environment.TickCount, true);
            IntPtr hwnd = f.Handle; // create the window
            Native.SetParent(hwnd, parent);
            int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, (style | Native.WS_CHILD) & ~Native.WS_POPUP);
            f.Location = new Point(0, 0);
            f.Size = bounds.Size;
            f.Show();

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                if (!Native.IsWindow(parent)) { Application.Exit(); return; }
                f.Tick();
            };
            timer.Start();
            Application.Run(f);
            timer.Stop();
            return 0;
        }

        static int RunSnapshot(Settings settings, string[] args)
        {
            // /snapshot <file.png> <width> <height> <frames>
            string path = args.Length > 1 ? args[1] : "snapshot.png";
            int w = args.Length > 2 ? int.Parse(args[2]) : 3440;
            int h = args.Length > 3 ? int.Parse(args[3]) : 1440;
            int frames = args.Length > 4 ? int.Parse(args[4]) : 300;
            int cell = (int)Math.Round(10 + settings.Size * 2.4);
            using (RainRenderer r = new RainRenderer(w, h, cell, settings, 12345))
            {
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                IntPtr tmpDC = Native.CreateCompatibleDC(screen);
                IntPtr tmpBmp = Native.CreateCompatibleBitmap(screen, w, h);
                IntPtr old = Native.SelectObject(tmpDC, tmpBmp);
                Native.ReleaseDC(IntPtr.Zero, screen);

                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < frames; i++) { r.Step(); r.BlitDirtyTo(tmpDC); } // includes the per-frame window copy
                sw.Stop();

                Native.SelectObject(tmpDC, old);
                Native.DeleteObject(tmpBmp);
                Native.DeleteDC(tmpDC);

                r.SavePng(path);
                File.WriteAllText(path + ".txt", string.Format(
                    "{0}x{1}, {2} frames, {3:F3} ms per frame (step + window copy)\r\n",
                    w, h, frames, sw.Elapsed.TotalMilliseconds / frames));
            }
            return 0;
        }
    }
}
