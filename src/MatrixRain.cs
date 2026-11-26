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
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint uParam, ref int pvParam, uint winIni);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint uParam, IntPtr pvParam, uint winIni);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
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
        [DllImport("gdi32.dll")] public static extern int ExcludeClipRect(IntPtr dc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int SelectClipRgn(IntPtr dc, IntPtr rgn);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

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
        float speedFactor;
        int maxDelay;

        // Speed keys 0-9 (settings 1-10): equal ~1.33x steps from 0.15 up to 1.95 (key 9 unchanged).
        static float SpeedFactorFor(int speedSetting)
        {
            return 1.95f * (float)Math.Pow(13.0, (Settings.Clamp(speedSetting, 1, 10) - 10) / 9.0);
        }
        // Density 1..10 = 0.1..1.0 = share of columns that rain (and shorter gaps between drops).
        static float DensityFor(int densitySetting) { return Settings.Clamp(densitySetting, 1, 10) / 10f; }
        static int MaxDelayFor(int densitySetting) { return 6 + (int)((1f - DensityFor(densitySetting)) * 90); } // frames

        readonly float[] rank;   // fixed random 0..1 per column; the column rains while rank < density
        float density;

        // Live changes while running (keyboard): rescale drops already falling so the change is instant.
        public void SetSpeed(int speedSetting)
        {
            float nf = SpeedFactorFor(speedSetting);
            float k = nf / speedFactor;
            for (int c = 0; c < cols; c++) speed[c] *= k;
            speedFactor = nf;
        }

        public void SetDensity(int densitySetting)
        {
            float old = density;
            density = DensityFor(densitySetting);
            maxDelay = MaxDelayFor(densitySetting);
            for (int c = 0; c < cols; c++)
            {
                if (delay[c] > maxDelay) delay[c] = rnd.Next(maxDelay + 1);
                // Columns that just switched off are cleared at once, so less rain shows immediately.
                if (rank[c] < old && rank[c] >= density)
                {
                    for (int r = 0; r < rows; r++) DrawCell(c, r, -1);
                    NewDrop(c);
                    continue;
                }
                // Columns that just switched on start almost right away.
                if (rank[c] >= old && rank[c] < density && drawnHead[c] < 0)
                {
                    delay[c] = rnd.Next(Math.Max(1, maxDelay / 3));
                    if (head[c] < -3) { head[c] = -1 - rnd.Next(3); drawnHead[c] = (int)Math.Floor(head[c]); }
                }
            }
        }

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
            : this(width, height, cellHeight, s, seed, false) { }

        // prefill = start with drops already part-way down the screen (used when the font size
        // changes while running, so the screen doesn't go blank).
        public RainRenderer(int width, int height, int cellHeight, Settings s, int seed, bool prefill)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            ch = Math.Max(5, cellHeight);
            // Column pitch ~ one character height: glyphs are about half that wide, so there's
            // always a clear gap between neighbouring columns, even at density 1.0.
            cw = Math.Max(4, (int)Math.Round(ch * 0.95));
            cols = (Width + cw - 1) / cw;
            rows = (Height + ch - 1) / ch;
            nGlyphs = Glyphs.Length;
            rnd = new Random(seed);
            speedFactor = SpeedFactorFor(s.Speed);
            maxDelay = MaxDelayFor(s.Density);
            density = DensityFor(s.Density);
            rank = new float[cols];
            for (int c = 0; c < cols; c++) rank[c] = (float)rnd.NextDouble();

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
                if (prefill && rank[c] < density)
                {
                    // The first Step() then draws this column's whole trail at once.
                    head[c] = rnd.Next(rows + trail[c]);
                    drawnHead[c] = (int)head[c] - trail[c] - 1;
                    delay[c] = 0;
                }
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

                // Draw every glyph once in white, then tint copies of that row for each brightness
                // level. Much faster than drawing text Levels times (matters for live font-size changes).
                using (Bitmap row = new Bitmap(nGlyphs * cw, ch, PixelFormat.Format24bppRgb))
                {
                    using (Graphics rg = Graphics.FromImage(row))
                    {
                        rg.Clear(Color.Black);
                        for (int i = 0; i < nGlyphs; i++)
                            TextRenderer.DrawText(rg, Glyphs[i].ToString(), font, new Rectangle(i * cw, 0, cw, ch), Color.White, Color.Black, flags);
                    }
                    for (int lvl = 0; lvl < Levels; lvl++)
                    {
                        Color col = LevelColor(lvl, baseColor, headColor);
                        ColorMatrix cm = new ColorMatrix(new float[][] {
                            new float[] { col.R / 255f, 0, 0, 0, 0 },
                            new float[] { 0, col.G / 255f, 0, 0, 0 },
                            new float[] { 0, 0, col.B / 255f, 0, 0 },
                            new float[] { 0, 0, 0, 1, 0 },
                            new float[] { 0, 0, 0, 0, 1 } });
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(row, new Rectangle(0, lvl * ch, row.Width, ch), 0, 0, row.Width, ch, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
                atlasBmp = bmp.GetHbitmap(Color.Black);
            }
            oldAtlas = Native.SelectObject(atlasDC, atlasBmp);
        }

        static Color LevelColor(int lvl, Color baseColor, Color headColor)
        {
            if (lvl == 0) return headColor;
            double t = (lvl - 1) / (double)(Levels - 1);     // 0 (bright) .. ~1 (dark)
            double b = lvl == 1 ? 1.0 : Math.Pow(1.0 - t, 1.15) * 0.92 + 0.08;
            return Color.FromArgb((int)(baseColor.R * b), (int)(baseColor.G * b), (int)(baseColor.B * b));
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
                // A column switched off by density finishes its current drop, then stays dark.
                if (rank[c] >= density && drawnHead[c] < 0) continue;
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

        public void BlitRect(IntPtr dc, Rectangle r)
        {
            Native.BitBlt(dc, r.X, r.Y, r.Width, r.Height, backDC, r.X, r.Y, Native.SRCCOPY);
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
            double px = 8 + settings.Size * 3; // font size 0.1..1.0 -> 11..38 px at 100% scaling (0.5 -> 23 px)
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
            if (ovDC != IntPtr.Zero)
            {
                if (unchecked(Environment.TickCount - ovUntil) >= 0)
                {
                    renderer.BlitDirtyTo(dc);
                    renderer.BlitRect(dc, ovRect); // put the rain back where the indicator was
                    FreeOverlay();
                }
                else
                {
                    // Keep rain updates out of the indicator box so it never flickers.
                    Native.ExcludeClipRect(dc, ovRect.Left, ovRect.Top, ovRect.Right, ovRect.Bottom);
                    renderer.BlitDirtyTo(dc);
                    Native.SelectClipRgn(dc, IntPtr.Zero);
                    Native.BitBlt(dc, ovRect.X, ovRect.Y, ovRect.Width, ovRect.Height, ovDC, 0, 0, Native.SRCCOPY);
                }
            }
            else renderer.BlitDirtyTo(dc);
            Native.ReleaseDC(Handle, dc);
        }

        // ---- temporary on-screen indicator ("SPEED 7", "DENSITY 0.6")
        IntPtr ovDC, ovBmp, ovOld;
        Rectangle ovRect;
        int ovUntil;

        void FreeOverlay()
        {
            if (ovDC == IntPtr.Zero) return;
            Native.SelectObject(ovDC, ovOld);
            Native.DeleteObject(ovBmp);
            Native.DeleteDC(ovDC);
            ovDC = IntPtr.Zero;
        }

        public void ShowIndicator(string label, string value, int filled)
        {
            if (!IsHandleCreated || IsDisposed) return;
            EnsureRenderer();
            if (ovDC != IntPtr.Zero)
            {
                IntPtr wdc = Native.GetDC(Handle);
                renderer.BlitRect(wdc, ovRect);
                Native.ReleaseDC(Handle, wdc);
                FreeOverlay();
            }

            string text = label + "  " + value + "   " + new string('█', filled) + new string('░', 10 - filled);
            Color baseC = settings.BaseColor();
            Color textC = Color.FromArgb(baseC.R + (255 - baseC.R) / 3, baseC.G + (255 - baseC.G) / 3, baseC.B + (255 - baseC.B) / 3);
            int fontPx = Math.Max(14, CellHeight() * 3 / 2);
            using (Font font = new Font("Consolas", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                Size ts = TextRenderer.MeasureText(text, font, Size.Empty, flags);
                int padX = fontPx, padY = fontPx / 2;
                int w = Math.Min(ClientSize.Width, ts.Width + padX * 2), h = ts.Height + padY * 2;
                ovRect = new Rectangle((ClientSize.Width - w) / 2, ClientSize.Height * 3 / 4 - h / 2, w, h);
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(baseC, Math.Max(2, fontPx / 10)))
                {
                    g.Clear(Color.Black);
                    g.DrawRectangle(pen, pen.Width / 2, pen.Width / 2, w - pen.Width, h - pen.Width);
                    TextRenderer.DrawText(g, text, font, new Point(padX, padY), textC, Color.Black, flags);
                    ovBmp = bmp.GetHbitmap(Color.Black);
                }
            }
            ovDC = Native.CreateCompatibleDC(IntPtr.Zero);
            ovOld = Native.SelectObject(ovDC, ovBmp);
            ovUntil = unchecked(Environment.TickCount + 1500);
        }

        // Font size changed while running: rebuild the grid at the new size, already mid-rain.
        public void RebuildForNewSize()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (renderer != null) renderer.Dispose();
            Size sz = ClientSize;
            renderer = new RainRenderer(sz.Width, sz.Height, CellHeight(), settings, Environment.TickCount ^ seed, true);
            renderer.Step();
            IntPtr dc = Native.GetDC(Handle);
            renderer.BlitTo(dc);
            if (ovDC != IntPtr.Zero)
                Native.BitBlt(dc, ovRect.X, ovRect.Y, ovRect.Width, ovRect.Height, ovDC, 0, 0, Native.SRCCOPY);
            Native.ReleaseDC(Handle, dc);
        }

        public void ApplyLiveSettings()
        {
            if (renderer == null) return;
            renderer.SetSpeed(settings.Speed);
            renderer.SetDensity(settings.Density);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (renderer == null) { e.Graphics.Clear(Color.Black); return; }
            IntPtr dc = e.Graphics.GetHdc();
            renderer.BlitTo(dc);
            if (ovDC != IntPtr.Zero)
                Native.BitBlt(dc, ovRect.X, ovRect.Y, ovRect.Width, ovRect.Height, ovDC, 0, 0, Native.SRCCOPY);
            e.Graphics.ReleaseHdc(dc);
        }

        protected override void Dispose(bool disposing)
        {
            if (renderer != null) { renderer.Dispose(); renderer = null; }
            FreeOverlay();
            base.Dispose(disposing);
        }
    }

    // Closes the screensaver on any real user input, from whichever monitor window gets it.
    // Exceptions (live controls): 0-9 set the speed (0 = slowest, 9 = fastest),
    // d = less rain, D (Shift+d) = more rain, f / F = smaller / bigger text.
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
                case 0x0100:                              // WM_KEYDOWN
                    if (HandleControlKey((int)(m.WParam.ToInt64() & 0xFF))) return true;
                    // Auto-repeat of a key that was already held when we started (e.g. Ctrl+M) doesn't exit.
                    if ((m.LParam.ToInt64() & 0x40000000) != 0) return true;
                    Program.Quit();
                    return true;
                case 0x0104:                              // WM_SYSKEYDOWN (Alt combos)
                case 0x0201: case 0x0204: case 0x0207:    // L/R/M button down
                case 0x020B: case 0x020A: case 0x020E:    // X button, wheel, h-wheel
                    Program.Quit();
                    return true;
            }
            return false;
        }

        static bool HandleControlKey(int vk)
        {
            Keys mods = Control.ModifierKeys;
            if (vk == 0x11 || vk == 0xA2 || vk == 0xA3) return true;                     // Ctrl on its own
            if (vk == 0x4D && (mods & Keys.Control) != 0) return true;                   // Ctrl+M (the start hotkey)
            if ((mods & (Keys.Control | Keys.Alt)) != 0) return false;
            if (vk == 0x10 || vk == 0x14 || vk == 0xA0 || vk == 0xA1) return true; // Shift, Caps Lock
            if (vk >= 0x30 && vk <= 0x39) { Program.SetSpeed(vk - 0x30 + 1); return true; } // top-row 0-9
            if (vk >= 0x60 && vk <= 0x69) { Program.SetSpeed(vk - 0x60 + 1); return true; } // numpad 0-9
            if (vk == 0x44)                                                                   // D
            {
                bool upper = ((mods & Keys.Shift) != 0) ^ Control.IsKeyLocked(Keys.CapsLock);
                Program.ChangeDensity(upper ? +1 : -1);
                return true;
            }
            if (vk == 0x46)                                                                   // F
            {
                bool upper = ((mods & Keys.Shift) != 0) ^ Control.IsKeyLocked(Keys.CapsLock);
                Program.ChangeFontSize(upper ? +1 : -1);
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
            size = AddSlider("Font size", s.Size, ref y);

            Label cl = new Label { Text = "Colour", Location = new Point(16, y + 4), AutoSize = true };
            color = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(140, y), Width = 220 };
            color.Items.AddRange(Settings.ColorNames);
            color.SelectedItem = s.Color;
            Controls.Add(cl); Controls.Add(color);
            y += 40;

            Label info = new Label
            {
                Text = "While running: 0-9 = speed, d / D = less / more rain,\r\nf / F = smaller / bigger text. Other keys or the mouse exit.",
                Location = new Point(16, y), Size = new Size(350, 36)
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
        static List<RainForm> liveForms = new List<RainForm>();
        static Settings liveSettings;
        static bool liveChanged;
        static bool sizePending;

        public static void Quit()
        {
            if (quitting) return;
            quitting = true;
            Application.Exit();
        }

        public static void SetSpeed(int value)
        {
            if (liveSettings == null) return;
            value = Settings.Clamp(value, 1, 10);
            if (liveSettings.Speed != value) { liveSettings.Speed = value; ApplyLive(); }
            // Speed keys are 0-9, so show the key number.
            foreach (RainForm f in liveForms) f.ShowIndicator("SPEED", (value - 1).ToString(), value);
        }

        public static void ChangeDensity(int delta)
        {
            if (liveSettings == null) return;
            int value = Settings.Clamp(liveSettings.Density + delta, 1, 10);
            if (liveSettings.Density != value) { liveSettings.Density = value; ApplyLive(); }
            string shown = (value / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            foreach (RainForm f in liveForms) f.ShowIndicator("DENSITY", shown, value);
        }

        public static void ChangeFontSize(int delta)
        {
            if (liveSettings == null) return;
            int value = Settings.Clamp(liveSettings.Size + delta, 1, 10);
            if (liveSettings.Size != value)
            {
                liveSettings.Size = value;
                liveChanged = true;
                sizePending = true; // rebuilt once on the next frame, however many presses arrive
            }
            string shown = (value / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            foreach (RainForm f in liveForms) f.ShowIndicator("FONT SIZE", shown, value);
        }

        static void ApplyLive()
        {
            liveChanged = true;
            foreach (RainForm f in liveForms) f.ApplyLiveSettings();
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
                case "hotkey": return RunHotkeyListener();
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

                liveForms = forms;
                liveSettings = settings;

                foreach (RainForm f in forms) f.Show();
                if (forms.Count > 0) { forms[0].Activate(); Native.SetForegroundWindow(forms[0].Handle); }
                Cursor.Hide();
                Application.AddMessageFilter(new InputWatcher());

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps
                timer.Tick += delegate
                {
                    if (sizePending) { sizePending = false; foreach (RainForm f in forms) f.RebuildForNewSize(); }
                    foreach (RainForm f in forms) f.Tick();
                };
                timer.Start();

                Application.Run();

                timer.Stop();
                foreach (RainForm f in forms) f.Dispose();
                if (liveChanged) { try { settings.Save(); } catch { } } // remember keyboard changes for next time
                Cursor.Show();
            }
            return 0;
        }

        // Background listener started at sign-in ("MatrixRain.exe /hotkey"): Ctrl+M starts the rain.
        class HotkeyWindow : NativeWindow
        {
            public HotkeyWindow() { CreateHandle(new CreateParams()); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) // WM_HOTKEY
                {
                    try
                    {
                        Native.AllowSetForegroundWindow(-1); // let the new process take the foreground
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/s");
                        psi.UseShellExecute = false;
                        Process.Start(psi);
                    }
                    catch { }
                    return;
                }
                base.WndProc(ref m);
            }
        }

        // Compares the saved screen saver settings with the live ones and re-applies the saved ones
        // if something turned the screen saver off. Saved settings are never changed here.
        static void RestoreScreenSaverIfOff()
        {
            try
            {
                int savedActive, savedTimeout;
                string scr;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (k == null) return;
                    scr = k.GetValue("SCRNSAVE.EXE") as string;
                    if (!int.TryParse(Convert.ToString(k.GetValue("ScreenSaveActive")), out savedActive)) return;
                    if (!int.TryParse(Convert.ToString(k.GetValue("ScreenSaveTimeOut")), out savedTimeout)) return;
                }
                if (savedActive != 1 || savedTimeout <= 0 || string.IsNullOrEmpty(scr)) return; // user chose "off"

                const uint SPI_GETSCREENSAVETIMEOUT = 14, SPI_SETSCREENSAVETIMEOUT = 15;
                const uint SPI_GETSCREENSAVEACTIVE = 16, SPI_SETSCREENSAVEACTIVE = 17, SPIF_SENDCHANGE = 2;
                int liveActive = 0, liveTimeout = 0;
                Native.SystemParametersInfo(SPI_GETSCREENSAVEACTIVE, 0, ref liveActive, 0);
                Native.SystemParametersInfo(SPI_GETSCREENSAVETIMEOUT, 0, ref liveTimeout, 0);
                if (liveActive != 0 && liveTimeout == savedTimeout) return;

                Native.SystemParametersInfo(SPI_SETSCREENSAVETIMEOUT, (uint)savedTimeout, IntPtr.Zero, SPIF_SENDCHANGE);
                Native.SystemParametersInfo(SPI_SETSCREENSAVEACTIVE, 1, IntPtr.Zero, SPIF_SENDCHANGE);

                // Note what was in front, to help find the app that keeps turning it off.
                string fg = "?";
                try
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                    fg = Process.GetProcessById((int)pid).ProcessName;
                }
                catch { }
                string log = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "screensaver-restore.log");
                if (File.Exists(log) && new FileInfo(log).Length > 100000) File.Delete(log);
                File.AppendAllText(log, string.Format("{0:yyyy-MM-dd HH:mm:ss}  restored (was active={1}, wait={2}s; now wait={3}s)  foreground app: {4}\r\n",
                    DateTime.Now, liveActive, liveTimeout, savedTimeout, fg));
            }
            catch { }
        }

        static int RunHotkeyListener()
        {
            bool created;
            using (Mutex m = new Mutex(true, "MatrixRainScreensaver_Hotkey", out created))
            {
                if (!created) return 0; // already running
                HotkeyWindow w = new HotkeyWindow();
                const uint MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
                if (!Native.RegisterHotKey(w.Handle, 1, MOD_CONTROL | MOD_NOREPEAT, 0x4D)) // 'M'
                {
                    w.DestroyHandle();
                    return 2; // Ctrl+M is in use by another program
                }
                // Some apps (video calls, games) switch the screen saver off and never switch it back on.
                // Once a minute, put Windows' live setting back to what is saved in Screen Saver Settings.
                System.Windows.Forms.Timer guard = new System.Windows.Forms.Timer();
                guard.Interval = 60000;
                guard.Tick += delegate { RestoreScreenSaverIfOff(); };
                guard.Start();
                RestoreScreenSaverIfOff();
                Application.Run();
                guard.Stop();
                Native.UnregisterHotKey(w.Handle, 1);
                w.DestroyHandle();
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
            int cell = 8 + settings.Size * 3;
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
