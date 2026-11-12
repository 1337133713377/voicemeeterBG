// VoicemeeterBG - affiche une image de fond dans toutes les fenetres Voicemeeter
// (Voicemeeter, Banana, Potato, 32 et 64 bits).
//
// Principe : Voicemeeter ne permet pas de changer son fond. Le programme pose
// donc, par-dessus chaque fenetre Voicemeeter, une fenetre transparente qui
// laisse passer les clics. En mode "intelligent", il regarde l'image de la
// fenetre Voicemeeter et ne dessine l'image que sur les pixels sombres (le
// fond), ce qui laisse visibles les textes, boutons, curseurs et vumetres.
//
// Code volontairement en C# 5 : il se compile avec le csc.exe livre avec
// Windows (.NET Framework 4), sans rien installer. Voir build.bat.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("VoicemeeterBG")]
[assembly: AssemblyProduct("VoicemeeterBG")]
[assembly: AssemblyVersion("1.0.2.0")]

namespace VoicemeeterBG
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX, CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
            public uint bmiColors;
        }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int SW_HIDE = 0;
        public const int SW_SHOWNA = 8;
        public const int ULW_ALPHA = 2;
        public const uint PW_CLIENTONLY = 1;
        public const uint PW_RENDERFULLCONTENT = 2;

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int key,
            ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        public static extern bool GdiFlush();
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi,
            uint usage, out IntPtr bits, IntPtr section, uint offset);

        // Windows 10 1607+ ; absents avant (EntryPointNotFoundException geree).
        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    // Voicemeeter peut etre mis a l'echelle par Windows (ecrans a 125 %, 150 %...).
    // Pour que la position, la capture et l'overlay tombent pile sur ses pixels,
    // on travaille sur chaque fenetre avec le meme mode DPI qu'elle.
    static class Dpi
    {
        static bool supported = true;

        public static void InitProcess()
        {
            try
            {
                if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))   // per-monitor v2
                    Native.SetProcessDpiAwarenessContext(new IntPtr(-3));    // per-monitor
            }
            catch (EntryPointNotFoundException)
            {
                supported = false;
                try { Native.SetProcessDPIAware(); } catch { }
            }
        }

        public static IntPtr ContextOf(IntPtr hwnd)
        {
            if (!supported) return IntPtr.Zero;
            try { return Native.GetWindowDpiAwarenessContext(hwnd); }
            catch (EntryPointNotFoundException) { supported = false; return IntPtr.Zero; }
        }

        public static IntPtr Enter(IntPtr ctx)
        {
            if (!supported || ctx == IntPtr.Zero) return IntPtr.Zero;
            try { return Native.SetThreadDpiAwarenessContext(ctx); }
            catch (EntryPointNotFoundException) { supported = false; return IntPtr.Zero; }
        }

        public static void Leave(IntPtr previous)
        {
            if (previous != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previous);
        }
    }

    enum FitMode { Cover, Contain, Stretch, Tile }

    sealed class Config
    {
        public string ImagePath = "";
        public int Opacity = 70;          // 5..100 %
        public bool Smart = true;         // true : image seulement sur le fond
        public FitMode Fit = FitMode.Cover;
        public int Sensitivity = 1;       // 0 strict, 1 normal, 2 large
        public bool Enabled = true;
        public bool MacroButtons = false;
        public int Fps = 15;              // rafraichissement du masque (mode intelligent)

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData), "VoicemeeterBG", "config.ini");
            }
        }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    int n;
                    bool isInt = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                    if (k == "image") c.ImagePath = v;
                    else if (k == "opacity" && isInt) c.Opacity = Clamp(n, 5, 100);
                    else if (k == "smart" && isInt) c.Smart = n != 0;
                    else if (k == "fit" && isInt) c.Fit = (FitMode)Clamp(n, 0, 3);
                    else if (k == "sensitivity" && isInt) c.Sensitivity = Clamp(n, 0, 2);
                    else if (k == "enabled" && isInt) c.Enabled = n != 0;
                    else if (k == "macrobuttons" && isInt) c.MacroButtons = n != 0;
                    else if (k == "fps" && isInt) c.Fps = Clamp(n, 1, 60);
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] {
                    "image=" + ImagePath,
                    "opacity=" + Opacity.ToString(CultureInfo.InvariantCulture),
                    "smart=" + (Smart ? "1" : "0"),
                    "fit=" + ((int)Fit).ToString(CultureInfo.InvariantCulture),
                    "sensitivity=" + Sensitivity.ToString(CultureInfo.InvariantCulture),
                    "enabled=" + (Enabled ? "1" : "0"),
                    "macrobuttons=" + (MacroButtons ? "1" : "0"),
                    "fps=" + Fps.ToString(CultureInfo.InvariantCulture)
                }, Encoding.UTF8);
            }
            catch { }
        }

        public static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }

    // Une surface GDI 32 bits dont on peut lire/ecrire les pixels directement.
    sealed class Surface : IDisposable
    {
        public IntPtr DC, Bits;
        IntPtr bitmap, oldBitmap;

        public Surface(int w, int h)
        {
            Native.BITMAPINFO bmi = new Native.BITMAPINFO();
            bmi.biSize = 40;
            bmi.biWidth = w;
            bmi.biHeight = -h;   // haut vers bas
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            DC = Native.CreateCompatibleDC(IntPtr.Zero);
            bitmap = Native.CreateDIBSection(DC, ref bmi, 0, out Bits, IntPtr.Zero, 0);
            if (DC == IntPtr.Zero || bitmap == IntPtr.Zero || Bits == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException("CreateDIBSection");
            }
            oldBitmap = Native.SelectObject(DC, bitmap);
        }

        public void Dispose()
        {
            if (DC != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(DC, oldBitmap);
                Native.DeleteDC(DC);
                DC = IntPtr.Zero;
            }
            if (bitmap != IntPtr.Zero) { Native.DeleteObject(bitmap); bitmap = IntPtr.Zero; }
            Bits = IntPtr.Zero;
        }
    }

    // L'overlay pose sur une fenetre Voicemeeter.
    sealed class Overlay : NativeWindow, IDisposable
    {
        public readonly IntPtr Target;
        readonly IntPtr dpiContext;
        readonly Config cfg;

        int x, y, w, h;
        bool shown;
        bool dirty = true;        // image ou reglages modifies
        int lastCapture;
        Surface scratch, capture, output;   // capture en cours, derniere capture valide, overlay
        int[] image;              // image mise a la taille, BGRA premultiplie
        bool hasCapture;          // "capture" contient une image valide de Voicemeeter
        int badCaptures;          // captures ratees consecutives
        int bgLevel = -1;         // niveau du fond de Voicemeeter, verrouille une fois trouve
        int bgCandidate, bgCandidateCount;
        byte[] alphaNow, alphaWait;   // alpha affiche par pixel, et attente avant de le remonter
        bool filterReady;

        public Overlay(IntPtr target, Config cfg)
        {
            Target = target;
            this.cfg = cfg;
            dpiContext = Dpi.ContextOf(target);

            IntPtr prev = Dpi.Enter(dpiContext);
            try
            {
                CreateParams cp = new CreateParams();
                cp.Caption = "VoicemeeterBG overlay";
                cp.Style = Native.WS_POPUP;
                cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                             Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                // Fenetre "possedee" par Voicemeeter : Windows la garde juste
                // au-dessus de lui, sans passer devant les autres applications.
                cp.Parent = target;
                CreateHandle(cp);
            }
            finally { Dpi.Leave(prev); }
        }

        public void Invalidate() { dirty = true; }

        public void InvalidateImage() { image = null; dirty = true; }

        // Retourne false quand la fenetre Voicemeeter n'existe plus.
        public bool Sync(Bitmap source)
        {
            if (!Native.IsWindow(Target)) return false;
            if (Handle == IntPtr.Zero) return false;   // detruit avec son proprietaire

            IntPtr prev = Dpi.Enter(dpiContext);
            try
            {
                Native.RECT rc;
                if (source == null || !Native.IsWindowVisible(Target) || Native.IsIconic(Target) ||
                    !Native.GetClientRect(Target, out rc) || rc.Right < 16 || rc.Bottom < 16)
                {
                    Hide();
                    return true;
                }

                Native.POINT origin = new Native.POINT();
                Native.ClientToScreen(Target, ref origin);
                bool moved = origin.X != x || origin.Y != y;
                bool resized = rc.Right != w || rc.Bottom != h || output == null;
                x = origin.X; y = origin.Y;

                if (resized)
                {
                    w = rc.Right; h = rc.Bottom;
                    FreeSurfaces();
                    scratch = new Surface(w, h);
                    capture = new Surface(w, h);
                    output = new Surface(w, h);
                    hasCapture = false;
                    alphaNow = new byte[w * h];
                    alphaWait = new byte[w * h];
                    filterReady = false;
                    image = null;
                    dirty = true;
                }
                if (image == null) image = RenderImage(source, w, h, cfg.Fit);

                int now = Environment.TickCount;
                bool captureDue = cfg.Smart && unchecked(now - lastCapture) >= 1000 / cfg.Fps;
                bool changed = false;
                if (dirty || captureDue)
                {
                    changed = Compose();
                    lastCapture = now;
                    dirty = false;
                }

                if (changed || moved || resized || !shown) Present();
                if (!shown)
                {
                    Native.ShowWindow(Handle, Native.SW_SHOWNA);
                    shown = true;
                }
                return true;
            }
            catch (InvalidOperationException)
            {
                Hide();
                return true;
            }
            finally { Dpi.Leave(prev); }
        }

        void Hide()
        {
            if (shown)
            {
                Native.ShowWindow(Handle, Native.SW_HIDE);
                shown = false;
            }
        }

        void Present()
        {
            Native.POINT dst = new Native.POINT(); dst.X = x; dst.Y = y;
            Native.POINT src = new Native.POINT();
            Native.SIZE size = new Native.SIZE(); size.CX = w; size.CY = h;
            Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = 1;   // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size,
                output.DC, ref src, 0, ref bf, Native.ULW_ALPHA);
        }

        // Capture Voicemeeter dans "scratch" et, si l'image est valide, l'echange
        // avec "capture". Une capture ratee (noire, unie) est simplement ignoree :
        // on garde la precedente, sinon l'image clignoterait.
        unsafe void Grab()
        {
            bool ok = Native.PrintWindow(Target, scratch.DC,
                          Native.PW_CLIENTONLY | Native.PW_RENDERFULLCONTENT) ||
                      Native.PrintWindow(Target, scratch.DC, Native.PW_CLIENTONLY);
            Native.GdiFlush();

            int n = w * h;
            uint* px = (uint*)scratch.Bits;
            int* hist = stackalloc int[256];
            for (int i = 0; i < 256; i++) hist[i] = 0;
            int samples = 0;
            for (int i = 0; i < n; i += 3) { hist[Level(px[i])]++; samples++; }

            int top = 0;
            for (int v = 1; v < 256; v++) if (hist[v] > hist[top]) top = v;
            if (!ok || hist[top] >= samples || hist[0] > samples * 6 / 10)
            {
                badCaptures++;
                return;
            }
            badCaptures = 0;
            Surface t = scratch; scratch = capture; capture = t;
            hasCapture = true;

            // Le fond de Voicemeeter est la teinte sombre la plus frequente. On la
            // cherche sur une fenetre glissante (plus stable qu'un seul niveau).
            int est = 0, bestSum = -1;
            for (int v = 0; v <= 140; v++)
            {
                int sum = 0;
                for (int d = -6; d <= 6; d++)
                    if (v + d >= 0 && v + d < 256) sum += hist[v + d];
                if (sum > bestSum) { bestSum = sum; est = v; }
            }
            // Le niveau retenu est verrouille : il ne change que si une autre valeur
            // s'impose durablement (environ 2 s), jamais d'une capture a l'autre.
            if (bgLevel < 0) bgLevel = est;
            else if (Math.Abs(est - bgLevel) <= 5) bgCandidateCount = 0;
            else
            {
                if (bgCandidateCount > 0 && Math.Abs(est - bgCandidate) <= 5) bgCandidateCount++;
                else { bgCandidate = est; bgCandidateCount = 1; }
                if (bgCandidateCount >= 2 * cfg.Fps)
                {
                    bgLevel = bgCandidate;
                    bgCandidateCount = 0;
                }
            }
        }

        // Calcule les pixels de l'overlay. Retourne true si quelque chose a change.
        unsafe bool Compose()
        {
            int n = w * h;
            byte* lut = stackalloc byte[256];   // niveau du pixel Voicemeeter -> alpha
            int opacity = cfg.Opacity * 255 / 100;
            bool masked = false;

            if (cfg.Smart)
            {
                Grab();
                if (hasCapture)
                {
                    int tol = cfg.Sensitivity == 0 ? 8 : (cfg.Sensitivity == 1 ? 16 : 30);
                    int soft = cfg.Sensitivity == 0 ? 24 : (cfg.Sensitivity == 1 ? 40 : 60);
                    int full = bgLevel + 6 + tol;
                    for (int v = 0; v < 256; v++)
                    {
                        int a = v <= full ? opacity
                              : (v >= full + soft ? 0 : opacity * (full + soft - v) / soft);
                        lut[v] = (byte)a;
                    }
                    masked = true;
                }
                else if (badCaptures < cfg.Fps)
                {
                    return false;   // pas encore de capture valide : on attend
                }
            }
            if (!masked)
            {
                // Mode simple, ou repli si la capture echoue durablement : opacite
                // uniforme (plafonnee dans le cas du repli pour ne pas masquer l'interface).
                int a = cfg.Smart ? Math.Min(opacity, 90) : opacity;
                for (int v = 0; v < 256; v++) lut[v] = (byte)a;
            }

            uint* cap = (uint*)capture.Bits;
            uint* dst = (uint*)output.Bits;
            // Filtre anti-clignotement : des qu'un pixel de Voicemeeter s'eclaire,
            // l'image s'y efface tout de suite ; elle n'y revient (en fondu) que si
            // le pixel est reste sombre environ une demi-seconde. Une zone qui
            // change sans arret (vumetre, capture prise en plein redessin) reste
            // donc degagee au lieu de faire clignoter l'image.
            bool snap = !masked || !filterReady || dirty;
            int hold = Math.Max(2, Math.Min(250, cfg.Fps / 2));
            uint step = (uint)Math.Max(16, 1020 / cfg.Fps);
            filterReady = masked;

            bool changed = false;
            fixed (int* imgInt = image)
            fixed (byte* now = alphaNow, wait = alphaWait)
            {
                uint* img = (uint*)imgInt;
                for (int i = 0; i < n; i++)
                {
                    uint t = lut[Level(cap[i])];
                    uint a = now[i];
                    if (snap || t <= a) { a = t; wait[i] = 0; }
                    else if (wait[i] < hold) wait[i]++;
                    else a = a + step > t ? t : a + step;
                    now[i] = (byte)a;
                    uint o = 0;
                    if (a != 0)
                    {
                        uint p = img[i];
                        if (a == 255) o = p;
                        else
                            o = ((((p & 0x00FF00FFu) * a) >> 8) & 0x00FF00FFu) |
                                ((((p >> 8) & 0x00FF00FFu) * a) & 0xFF00FF00u);
                    }
                    if (dst[i] != o) { dst[i] = o; changed = true; }
                }
            }
            return changed;
        }

        // "Luminosite" d'un pixel = sa composante la plus forte, pour que les
        // elements colores (vumetres verts, boutons rouges/bleus) restent visibles.
        static int Level(uint c)
        {
            uint r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            uint m = r > g ? r : g;
            return (int)(m > b ? m : b);
        }

        static int[] RenderImage(Bitmap source, int w, int h, FitMode fit)
        {
            int[] px = new int[w * h];
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float sw = source.Width, sh = source.Height;
                    if (fit == FitMode.Tile)
                    {
                        for (int ty = 0; ty < h; ty += source.Height)
                            for (int tx = 0; tx < w; tx += source.Width)
                                g.DrawImage(source, tx, ty, source.Width, source.Height);
                    }
                    else if (fit == FitMode.Stretch)
                    {
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);   // evite un lisere sur les bords
                            g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0,
                                source.Width, source.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    else
                    {
                        float scale = fit == FitMode.Cover
                            ? Math.Max(w / sw, h / sh) : Math.Min(w / sw, h / sh);
                        float dw = sw * scale, dh = sh * scale;
                        g.DrawImage(source, (w - dw) / 2f, (h - dh) / 2f, dw, dh);
                    }
                }
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int row = 0; row < h; row++)
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)row * data.Stride),
                            px, row * w, w);
                }
                finally { bmp.UnlockBits(data); }
            }
            return px;
        }

        void FreeSurfaces()
        {
            if (scratch != null) { scratch.Dispose(); scratch = null; }
            if (capture != null) { capture.Dispose(); capture = null; }
            if (output != null) { output.Dispose(); output = null; }
        }

        public void Dispose()
        {
            FreeSurfaces();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    sealed class App : ApplicationContext
    {
        readonly Config cfg;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly Dictionary<IntPtr, Overlay> overlays = new Dictionary<IntPtr, Overlay>();
        readonly Native.EnumWindowsProc enumProc;
        readonly HashSet<uint> pids = new HashSet<uint>();
        readonly List<IntPtr> found = new List<IntPtr>();
        readonly StringBuilder className = new StringBuilder(64);
        Bitmap source;
        int lastScan;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public App(string imageArg)
        {
            cfg = Config.Load();
            enumProc = OnEnumWindow;
            if (!string.IsNullOrEmpty(imageArg)) { cfg.ImagePath = imageArg; cfg.Save(); }

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "VoicemeeterBG";
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            tray.DoubleClick += delegate { ChooseImage(); };
            tray.Visible = true;
            BuildMenu();

            LoadImage(false);
            if (source == null) ChooseImage();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            lastScan = Environment.TickCount - 2000;
        }

        void Tick()
        {
            if (!cfg.Enabled || source == null)
            {
                if (overlays.Count > 0) ClearOverlays();
                return;
            }
            int now = Environment.TickCount;
            if (unchecked(now - lastScan) >= 1000) { lastScan = now; Scan(); }

            List<IntPtr> dead = null;
            foreach (KeyValuePair<IntPtr, Overlay> kv in overlays)
            {
                bool alive;
                try { alive = kv.Value.Sync(source); }
                catch { alive = false; }
                if (!alive)
                {
                    if (dead == null) dead = new List<IntPtr>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
                foreach (IntPtr hwnd in dead)
                {
                    overlays[hwnd].Dispose();
                    overlays.Remove(hwnd);
                }
        }

        // Cherche les fenetres de tous les processus Voicemeeter.
        void Scan()
        {
            pids.Clear();
            uint self = (uint)Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    // voicemeeter.exe, voicemeeterpro.exe, voicemeeter8.exe,
                    // voicemeeter8x64.exe, voicemeeter_x64.exe, ...
                    string name = p.ProcessName.ToLowerInvariant();
                    if ((uint)p.Id != self && name.StartsWith("voicemeeter") &&
                        !name.Contains("setup") && !name.StartsWith("voicemeeterbg") &&
                        (cfg.MacroButtons || !name.Contains("macrobuttons")))
                        pids.Add((uint)p.Id);
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (pids.Count == 0) return;

            found.Clear();
            Native.EnumWindows(enumProc, IntPtr.Zero);
            foreach (IntPtr hwnd in found)
            {
                if (overlays.ContainsKey(hwnd)) continue;
                try { overlays.Add(hwnd, new Overlay(hwnd, cfg)); }
                catch { }
            }
        }

        bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (!pids.Contains(pid) || !Native.IsWindowVisible(hwnd)) return true;

            // On ignore les menus, infobulles et boites de dialogue Windows standard.
            className.Length = 0;
            Native.GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "#32768" || cls == "#32770" || cls.StartsWith("tooltips_class")) return true;

            Native.RECT rc;
            if (!Native.GetClientRect(hwnd, out rc)) return true;
            if (rc.Right < 200 || rc.Bottom < 120) return true;
            found.Add(hwnd);
            return true;
        }

        void ClearOverlays()
        {
            foreach (Overlay o in overlays.Values) o.Dispose();
            overlays.Clear();
        }

        void LoadImage(bool showErrors)
        {
            Bitmap loaded = null;
            if (!string.IsNullOrEmpty(cfg.ImagePath))
            {
                try
                {
                    // Copie en memoire : le fichier n'est pas verrouille.
                    byte[] bytes = File.ReadAllBytes(cfg.ImagePath);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image img = Image.FromStream(ms))
                        loaded = new Bitmap(img);
                }
                catch (Exception ex)
                {
                    if (showErrors)
                        MessageBox.Show("Impossible d'ouvrir l'image :\n" + ex.Message,
                            "VoicemeeterBG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            if (loaded == null && source != null && showErrors) return;   // on garde l'ancienne
            if (source != null) source.Dispose();
            source = loaded;
            foreach (Overlay o in overlays.Values) o.InvalidateImage();
        }

        void ChooseImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "VoicemeeterBG - choisir l'image de fond";
                dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tous les fichiers|*.*";
                try
                {
                    if (!string.IsNullOrEmpty(cfg.ImagePath))
                        dlg.InitialDirectory = Path.GetDirectoryName(cfg.ImagePath);
                }
                catch { }
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string previous = cfg.ImagePath;
                cfg.ImagePath = dlg.FileName;
                LoadImage(true);
                if (source == null) cfg.ImagePath = previous;
                cfg.Save();
            }
        }

        void Changed(bool imageLayout)
        {
            cfg.Save();
            foreach (Overlay o in overlays.Values)
            {
                if (imageLayout) o.InvalidateImage(); else o.Invalidate();
            }
        }

        static ToolStripMenuItem Item(string text, bool check, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = check;
            it.Click += click;
            return it;
        }

        void BuildMenu()
        {
            ToolStripItemCollection m = tray.ContextMenuStrip.Items;
            m.Clear();
            m.Add(Item("Choisir l'image...", false, delegate { ChooseImage(); }));
            m.Add(Item("Activé", cfg.Enabled, delegate
            {
                cfg.Enabled = !cfg.Enabled; cfg.Save(); lastScan = Environment.TickCount - 2000;
            }));
            m.Add(new ToolStripSeparator());

            ToolStripMenuItem opacity = new ToolStripMenuItem("Opacité");
            foreach (int pct in new int[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 })
            {
                int value = pct;
                opacity.DropDownItems.Add(Item(pct + " %", cfg.Opacity == pct,
                    delegate { cfg.Opacity = value; Changed(false); }));
            }
            m.Add(opacity);

            ToolStripMenuItem mode = new ToolStripMenuItem("Mode");
            mode.DropDownItems.Add(Item("Intelligent (image derrière les commandes)", cfg.Smart,
                delegate { cfg.Smart = true; Changed(false); }));
            mode.DropDownItems.Add(Item("Simple (voile uniforme, aucune charge CPU)", !cfg.Smart,
                delegate { cfg.Smart = false; Changed(false); }));
            m.Add(mode);

            ToolStripMenuItem sens = new ToolStripMenuItem("Détection du fond");
            string[] sensNames = { "Stricte (commandes plus nettes)", "Normale", "Large (image plus présente)" };
            for (int i = 0; i < sensNames.Length; i++)
            {
                int value = i;
                sens.DropDownItems.Add(Item(sensNames[i], cfg.Sensitivity == i,
                    delegate { cfg.Sensitivity = value; Changed(false); }));
            }
            sens.Enabled = cfg.Smart;
            m.Add(sens);

            ToolStripMenuItem fit = new ToolStripMenuItem("Ajustement de l'image");
            string[] fitNames = { "Remplir (rogner)", "Ajuster (entière)", "Étirer", "Mosaïque" };
            for (int i = 0; i < fitNames.Length; i++)
            {
                FitMode value = (FitMode)i;
                fit.DropDownItems.Add(Item(fitNames[i], cfg.Fit == value,
                    delegate { cfg.Fit = value; Changed(true); }));
            }
            m.Add(fit);

            m.Add(Item("Inclure Macro Buttons", cfg.MacroButtons, delegate
            {
                cfg.MacroButtons = !cfg.MacroButtons; cfg.Save();
                ClearOverlays(); lastScan = Environment.TickCount - 2000;
            }));
            m.Add(new ToolStripSeparator());
            m.Add(Item("Lancer au démarrage de Windows", IsAutoStart(),
                delegate { SetAutoStart(!IsAutoStart()); }));
            m.Add(Item("Quitter", false, delegate { ExitThread(); }));
        }

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key != null && key.GetValue("VoicemeeterBG") != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) key.SetValue("VoicemeeterBG", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("VoicemeeterBG", false);
                }
            }
            catch { }
        }

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush back = new SolidBrush(Color.FromArgb(44, 61, 77)))
                        g.FillRectangle(back, 1, 3, 30, 26);
                    using (Brush sun = new SolidBrush(Color.FromArgb(255, 200, 80)))
                        g.FillEllipse(sun, 20, 7, 7, 7);
                    using (Brush hill = new SolidBrush(Color.FromArgb(104, 230, 248)))
                        g.FillPolygon(hill, new Point[] {
                            new Point(3, 27), new Point(12, 13), new Point(19, 22),
                            new Point(23, 18), new Point(29, 27) });
                }
                IntPtr handle = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            ClearOverlays();
            tray.Visible = false;
            tray.Dispose();
            if (source != null) source.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (Mutex mutex = new Mutex(true, "VoicemeeterBG.SingleInstance", out first))
            {
                if (!first) return;
                Dpi.InitProcess();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App(args.Length > 0 ? args[0] : null));
                GC.KeepAlive(mutex);
            }
        }
    }
}
