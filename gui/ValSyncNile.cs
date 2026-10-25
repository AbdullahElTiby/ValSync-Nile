// VALSYNC Nile - setup app for the DPI bypass engines.
// The app only configures two Windows services (ValSync = GoodbyeDPI for websites, ValSync-Voice = zapret
// winws for UDP voice). The services run on their own, so the app can be closed or deleted after setup.
// Targets .NET Framework 4.x (C# 5) so it builds with the csc.exe that ships with Windows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ValSyncNile
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            // GDI+ text rendering, needed for the embedded brand fonts.
            Application.SetCompatibleTextRenderingDefault(true);

            if (args.Length >= 2 && args[0] == "--selftest")
            {
                File.WriteAllText(args[1], Engine.SelfTest());
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--preview")
                return MainForm.Preview(args[1], args[2], args.Length > 3 ? args[3] : "off");

            bool created;
            using (var mutex = new Mutex(true, "Global\\ValSyncNileSetup", out created))
            {
                if (!created) return 0;
                Application.Run(new MainForm(false));
            }
            return 0;
        }
    }

    // ---------------------------------------------------------------- engine: files, services, tests

    static class Engine
    {
        public const string Version = "1.1.0";
        public const string WebService = "ValSync";
        public const string VoiceService = "ValSync-Voice";

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ValSync-Nile");
        public static string EngineDir { get { return Path.Combine(Dir, "engine"); } }
        public static bool Is64 { get { return Environment.Is64BitOperatingSystem; } }

        // Yandex DNS on a non-standard port: Egyptian ISPs cannot hijack it like port 53.
        const string Dns = "--dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253";

        // Index = website mode (1-6). Same flags as profiles.cmd.
        static readonly string[] WebTemplates =
        {
            null,
            "-9 " + Dns,
            "-5 --set-ttl 5 " + Dns,
            "-9 --frag-by-sni --fake-gen 5 --fake-resend 2 " + Dns,
            "-5 --set-ttl 3 -q",
            "-1 -q " + Dns,
            "-9 --frag-by-sni " + Dns + " --blacklist \"{E}\\egypt-blacklist.txt\" --allow-no-sni",
        };
        public const int WebModes = 6;
        // Modes tried by automatic detection, in order. 6 only covers listed sites, so it is not a candidate.
        public static readonly int[] DetectOrder = { 1, 2, 3, 4, 5 };

        // Index = voice mode (1 light, 2 full). Same flags as voice_profiles.cmd.
        static readonly string[] VoiceTemplates =
        {
            null,
            "--wf-raw-part=@\"{F}\\windivert_part.discord_media.txt\" --wf-raw-part=@\"{F}\\windivert_part.stun.txt\" " +
                "--filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6",
            "--wf-udp=12000-65535 --wf-raw-part=@\"{F}\\windivert_part.discord_media.txt\" " +
                "--wf-raw-part=@\"{F}\\windivert_part.stun.txt\" --wf-raw-part=@\"{F}\\windivert_part.wireguard.txt\" " +
                "--filter-l7=discord,stun,wireguard --dpi-desync=fake --dpi-desync-repeats=6 --new " +
                "--filter-udp=12000-65535 --dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n3 --dpi-desync-repeats=6",
        };

        public static string WebExe { get { return Path.Combine(EngineDir, Is64 ? "x86_64" : "x86", "goodbyedpi.exe"); } }
        public static string VoiceExe { get { return Path.Combine(EngineDir, "voice", "winws.exe"); } }

        public static string WebArgs(int mode) { return WebTemplates[mode].Replace("{E}", EngineDir); }
        public static string VoiceArgs(int mode)
        {
            return VoiceTemplates[mode].Replace("{F}", Path.Combine(EngineDir, "voice", "windivert.filter"));
        }

        // ---- embedded engine files

        public static void Extract()
        {
            var asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames().Where(n => n.StartsWith("engine/")).ToArray();
            string versionFile = Path.Combine(EngineDir, "version.txt");

            bool needed = !File.Exists(versionFile) || File.ReadAllText(versionFile).Trim() != Version;
            if (!needed)
                needed = names.Any(n => !File.Exists(Path.Combine(Dir, n.Replace('/', '\\'))));
            if (!needed) return;

            // Running services lock their files; restart them after the update.
            bool webWasRunning = IsRunning(WebService), voiceWasRunning = IsRunning(VoiceService);
            Stop(VoiceService);
            Stop(WebService);

            foreach (string name in names)
            {
                string target = Path.Combine(Dir, name.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream src = asm.GetManifestResourceStream(name))
                using (FileStream dst = File.Create(target))
                    src.CopyTo(dst);
            }
            File.WriteAllText(versionFile, Version);

            if (webWasRunning) Start(WebService);
            if (voiceWasRunning) Start(VoiceService);
        }

        // ---- services (sc.exe, same commands as the .cmd scripts)

        static int Sc(string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static ServiceControllerStatus? Status(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                    return sc.Status;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public static bool Exists(string name) { return Status(name).HasValue; }
        public static bool IsRunning(string name) { return Status(name) == ServiceControllerStatus.Running; }

        public static string BinPath(string exe, string args)
        {
            // sc.exe takes binPath as one quoted argument, so every inner quote is escaped.
            return "\"" + ("\"" + exe + "\" " + args).Replace("\"", "\\\"") + "\"";
        }

        public static void Install(string name, string display, string description, string exe, string args, bool autoStart)
        {
            string start = autoStart ? "auto" : "demand";
            if (Exists(name))
                Sc("config " + name + " binPath= " + BinPath(exe, args) + " start= " + start);
            else
                Sc("create " + name + " binPath= " + BinPath(exe, args) + " start= " + start + " DisplayName= \"" + display + "\"");
            Sc("description " + name + " \"" + description + "\"");
        }

        public static bool Start(string name)
        {
            Sc("start " + name);
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 10000)
            {
                ServiceControllerStatus? s = Status(name);
                if (s == ServiceControllerStatus.Running) return true;
                // Exited right away: usually the antivirus removed or blocked the WinDivert driver.
                if (s == ServiceControllerStatus.Stopped && timer.ElapsedMilliseconds > 1500) return false;
                Thread.Sleep(200);
            }
            return false;
        }

        public static void Stop(string name)
        {
            if (!Exists(name)) return;
            Sc("stop " + name);
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 10000)
            {
                ServiceControllerStatus? s = Status(name);
                if (s == null || s == ServiceControllerStatus.Stopped) return;
                Thread.Sleep(200);
            }
        }

        public static void Remove(string name)
        {
            Stop(name);
            if (Exists(name)) Sc("delete " + name);
        }

        public static void SetAutoStart(string name, bool autoStart)
        {
            if (Exists(name)) Sc("config " + name + " start= " + (autoStart ? "auto" : "demand"));
        }

        public static void RemoveDrivers()
        {
            foreach (string driver in new[] { "WinDivert", "WinDivert14" })
            {
                Sc("stop " + driver);
                Sc("delete " + driver);
            }
        }

        // ---- connection test against sites Egyptian ISPs block

        public static readonly string[] TestUrls = { "https://www.madamasr.com/", "https://www.aljazeera.net/" };

        static Engine()
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
            // Every mode test must resolve and connect again from scratch.
            ServicePointManager.DnsRefreshTimeout = 0;
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        static extern bool DnsFlushResolverCache();

        public static void FlushDns()
        {
            try { DnsFlushResolverCache(); } catch { }
        }

        // Number of test sites that answered. Any HTTP answer (even 403) means the block was bypassed.
        public static int Reachable()
        {
            Task<bool>[] probes = TestUrls.Select(u => Task.Run(() => Probe(u))).ToArray();
            Task.WaitAll(probes);
            return probes.Count(t => t.Result);
        }

        static bool Probe(string url)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.KeepAlive = false;
                request.AllowAutoRedirect = false;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
                using (request.GetResponse()) return true;
            }
            catch (WebException e)
            {
                if (e.Response == null) return false;
                e.Response.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string SelfTest()
        {
            var lines = new List<string>();
            for (int m = 1; m <= WebModes; m++) lines.Add("web " + m + ": " + BinPath(WebExe, WebArgs(m)));
            for (int v = 1; v <= 2; v++) lines.Add("voice " + v + ": " + BinPath(VoiceExe, VoiceArgs(v)));
            lines.Add("resources: " + string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames()));
            return string.Join(Environment.NewLine, lines);
        }
    }

    // ---------------------------------------------------------------- settings

    class Settings
    {
        public int Web;          // 0 = automatic, 1-6 = fixed mode
        public int Detected;     // mode found by automatic detection, 0 = not yet
        public int Voice = 2;    // 0 off, 1 light, 2 full
        public bool AutoStart = true;
        public string Lang;

        static string FilePath { get { return Path.Combine(Engine.Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            s.Lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    int n;
                    int.TryParse(value, out n);
                    if (key == "web") s.Web = Math.Max(0, Math.Min(Engine.WebModes, n));
                    else if (key == "detected") s.Detected = Math.Max(0, Math.Min(Engine.WebModes, n));
                    else if (key == "voice") s.Voice = Math.Max(0, Math.Min(2, n));
                    else if (key == "autostart") s.AutoStart = n != 0;
                    else if (key == "lang" && (value == "en" || value == "ar")) s.Lang = value;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Engine.Dir);
                File.WriteAllLines(FilePath, new[]
                {
                    "web=" + Web, "detected=" + Detected, "voice=" + Voice,
                    "autostart=" + (AutoStart ? 1 : 0), "lang=" + Lang,
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ---------------------------------------------------------------- strings (English / Arabic)

    static class L
    {
        public static string Lang = "en";
        public static bool Rtl { get { return Lang == "ar"; } }

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            { "subtitle", new[] { "Unblock websites & game voice chat in Egypt", "افتح المواقع المحجوبة وصوت الألعاب في مصر" } },
            { "lang", new[] { "عربي", "English" } },
            { "off", new[] { "DISCONNECTED", "غير متصل" } },
            { "on", new[] { "CONNECTED", "متصل" } },
            { "busy", new[] { "WORKING", "جاري العمل" } },
            { "orb_off", new[] { "OFF", "إيقاف" } },
            { "orb_on", new[] { "ON", "تشغيل" } },
            { "connect", new[] { "CONNECT", "اتصال" } },
            { "disconnect", new[] { "DISCONNECT", "قطع الاتصال" } },
            { "detail_off", new[] { "Press Connect. Setup takes about a minute the first time.", "اضغط اتصال. أول مرة بتاخد حوالي دقيقة." } },
            { "detail_on", new[] { "Websites: mode {0}   ·   Voice: {1}", "المواقع: الوضع {0}   ·   الصوت: {1}" } },
            { "redetect", new[] { "Find best settings again", "ابحث عن أفضل إعدادات مرة تانية" } },
            { "websites", new[] { "WEBSITES", "المواقع" } },
            { "voice", new[] { "GAME VOICE CHAT", "صوت الألعاب والمكالمات" } },
            { "web0", new[] { "Automatic — finds what works on your ISP", "تلقائي — يختار الأنسب لشبكتك" } },
            { "web1", new[] { "Mode 1 · Recommended", "الوضع 1 · موصى به" } },
            { "web2", new[] { "Mode 2 · Fixed TTL", "الوضع 2 · TTL ثابت" } },
            { "web3", new[] { "Mode 3 · Aggressive", "الوضع 3 · قوي" } },
            { "web4", new[] { "Mode 4 · No DNS change", "الوضع 4 · بدون تغيير DNS" } },
            { "web5", new[] { "Mode 5 · Compatible (old routers)", "الوضع 5 · متوافق (راوترات قديمة)" } },
            { "web6", new[] { "Mode 6 · Blocked sites list only", "الوضع 6 · قائمة المواقع المحجوبة فقط" } },
            { "voice0", new[] { "Off", "إيقاف" } },
            { "voice1", new[] { "Light", "خفيف" } },
            { "voice2", new[] { "Full", "كامل" } },
            { "vhint0", new[] { "Voice bypass is off.", "تخطي الصوت متوقف." } },
            { "vhint1", new[] { "Discord voice, WhatsApp & Telegram calls.", "صوت ديسكورد ومكالمات واتساب وتليجرام." } },
            { "vhint2", new[] { "Adds Valorant and all other game voice chat.", "يضيف فالورانت وكل صوت الألعاب التانية." } },
            { "vhint32", new[] { "Voice bypass needs 64-bit Windows.", "تخطي الصوت محتاج ويندوز 64 بت." } },
            { "autostart", new[] { "Start automatically with Windows", "التشغيل تلقائياً مع الويندوز" } },
            { "note_off", new[] { "VALSYNC Nile runs in the background. After setup you can close this window.", "VALSYNC Nile بيشتغل في الخلفية. بعد الإعداد تقدر تقفل النافذة دي." } },
            { "note_on", new[] { "Done! You can close this window. Restart your game or Discord once so voice goes through.", "تمام! تقدر تقفل النافذة دي. اقفل اللعبة أو ديسكورد وافتحهم تاني مرة واحدة عشان الصوت يشتغل." } },
            { "uninstall", new[] { "Uninstall", "إزالة" } },
            { "preparing", new[] { "Preparing…", "جاري التجهيز…" } },
            { "checking", new[] { "Checking your connection…", "جاري فحص الاتصال…" } },
            { "testing", new[] { "Testing mode {0} of {1}…", "جاري تجربة الوضع {0} من {1}…" } },
            { "connecting", new[] { "Connecting…", "جاري الاتصال…" } },
            { "disconnecting", new[] { "Disconnecting…", "جاري قطع الاتصال…" } },
            { "noprofile", new[] {
                "No mode fully opened the test sites, so VALSYNC Nile connected with the recommended mode.\n\nIf blocked sites still don't open, pick another mode under Websites.",
                "مفيش وضع فتح مواقع الاختبار بالكامل، فـ VALSYNC Nile اتصل بالوضع الموصى به.\n\nلو المواقع المحجوبة لسه مش بتفتح، اختار وضع تاني من المواقع." } },
            { "startfail", new[] {
                "VALSYNC Nile couldn't start. Your antivirus may have blocked or deleted its network driver (WinDivert).\n\nAdd VALSYNC Nile to your antivirus exclusions and press Connect again.",
                "VALSYNC Nile مقدرش يشتغل. ممكن برنامج الحماية يكون منع أو مسح الدرايفر بتاعه (WinDivert).\n\nضيف VALSYNC Nile للاستثناءات في برنامج الحماية واضغط اتصال تاني." } },
            { "uninstall_confirm", new[] {
                "Remove VALSYNC Nile from this PC? This stops it and deletes its services and files.",
                "عايز تشيل VALSYNC Nile من الجهاز؟ ده هيوقفه ويمسح الخدمات والملفات بتاعته." } },
            { "uninstalled", new[] {
                "VALSYNC Nile was removed. You can now delete VALSYNC-Nile.exe.",
                "تم إزالة VALSYNC Nile. تقدر دلوقتي تمسح VALSYNC-Nile.exe." } },
            { "error", new[] { "Something went wrong:\n\n{0}", "حصلت مشكلة:\n\n{0}" } },
        };

        public static string T(string key) { return S[key][Rtl ? 1 : 0]; }
    }

    // ---------------------------------------------------------------- brand: colors, fonts, shapes

    static class Brand
    {
        public static readonly Color Bg = ColorTranslator.FromHtml("#0A141E");
        public static readonly Color Surface = ColorTranslator.FromHtml("#17202B");
        public static readonly Color Elevated = ColorTranslator.FromHtml("#212B35");
        public static readonly Color Red = ColorTranslator.FromHtml("#FF525D");
        public static readonly Color RedDeep = ColorTranslator.FromHtml("#E0313D");
        public static readonly Color Cyan = ColorTranslator.FromHtml("#00EAB8");
        public static readonly Color Gold = ColorTranslator.FromHtml("#EAC33D");
        public static readonly Color Text = ColorTranslator.FromHtml("#D9E3F2");
        public static readonly Color TextDim = ColorTranslator.FromHtml("#8C9AAD");
        public static readonly Color Line = ColorTranslator.FromHtml("#2A3542");

        static PrivateFontCollection fonts;
        static FontFamily display, ui, uiStrong;

        public static void LoadFonts()
        {
            fonts = new PrivateFontCollection();
            var asm = Assembly.GetExecutingAssembly();
            foreach (string name in new[] { "font/display.ttf", "font/ui.ttf", "font/ui-strong.ttf" })
            {
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) continue;
                    byte[] data = new byte[s.Length];
                    s.Read(data, 0, data.Length);
                    // AddMemoryFont keeps a pointer, so the copy is never freed.
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    fonts.AddMemoryFont(mem, data.Length);
                }
            }
            foreach (FontFamily f in fonts.Families)
            {
                if (f.Name.StartsWith("Big Shoulders")) display = f;
                else if (f.Name.Contains("SemiBold")) uiStrong = f;
                else if (f.Name.StartsWith("Barlow")) ui = f;
            }
        }

        // The brand fonts have no Arabic glyphs, so Arabic always uses Segoe UI.
        public static Font Display(float size)
        {
            if (L.Rtl || display == null) return new Font("Segoe UI", size * 0.8f, FontStyle.Bold);
            return new Font(display, size, FontStyle.Bold);
        }

        public static Font Ui(float size)
        {
            if (L.Rtl || ui == null) return new Font("Segoe UI", size * 0.85f);
            return new Font(ui, size, FontStyle.Regular);
        }

        public static Font UiStrong(float size)
        {
            if (L.Rtl || (uiStrong == null && ui == null)) return new Font("Segoe UI Semibold", size * 0.85f);
            return uiStrong != null ? new Font(uiStrong, size, FontStyle.Regular) : new Font(ui, size, FontStyle.Bold);
        }

        public static Image Logo()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("img/logo.png"))
                return s == null ? null : new Bitmap(s);
        }

        // Valorant-style rectangle with two cut corners.
        public static GraphicsPath Chamfer(RectangleF r, float cut)
        {
            var p = new GraphicsPath();
            p.AddPolygon(new[]
            {
                new PointF(r.Left + cut, r.Top), new PointF(r.Right, r.Top),
                new PointF(r.Right, r.Bottom - cut), new PointF(r.Right - cut, r.Bottom),
                new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top + cut),
            });
            return p;
        }

        public static PointF[] Hexagon(float cx, float cy, float radius, float rotation)
        {
            var points = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 180 * (60 * i - 90 + rotation);
                points[i] = new PointF(cx + (float)(radius * Math.Cos(a)), cy + (float)(radius * Math.Sin(a)));
            }
            return points;
        }

        public static StringFormat Centered()
        {
            var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (L.Rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            return f;
        }

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }

    // ---------------------------------------------------------------- custom controls

    class BrandControl : Control
    {
        public BrandControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    // Hexagon status indicator (the VALSYNC mark is built from hexagons).
    class StatusOrb : BrandControl
    {
        public enum State { Off, On, Busy }
        State state;
        float spin;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
        public string Caption = "";

        public StatusOrb()
        {
            timer.Tick += delegate { spin = (spin + 4) % 360; Invalidate(); };
        }

        public State Current
        {
            get { return state; }
            set { state = value; timer.Enabled = value == State.Busy; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Brand.Quality(g);
            float cx = Width / 2f, cy = Height / 2f, r = Math.Min(Width, Height) / 2f - 6;
            Color accent = state == State.On ? Brand.Cyan : state == State.Busy ? Brand.Gold : Brand.Line;

            if (state == State.On)
            {
                // Soft glow rings.
                for (int i = 3; i >= 1; i--)
                    using (var glow = new Pen(Color.FromArgb(18 * i, accent), 10 - 2 * i))
                        g.DrawPolygon(glow, Brand.Hexagon(cx, cy, r - 2 + i * 2, 0));
            }

            using (var outer = new Pen(accent, 3))
            {
                if (state == State.Busy)
                {
                    // Two opposite edges light up and rotate.
                    PointF[] hex = Brand.Hexagon(cx, cy, r, 0);
                    using (var dim = new Pen(Brand.Line, 3)) g.DrawPolygon(dim, hex);
                    int edge = (int)(spin / 60) % 6;
                    g.DrawLine(outer, hex[edge], hex[(edge + 1) % 6]);
                    g.DrawLine(outer, hex[(edge + 3) % 6], hex[(edge + 4) % 6]);
                }
                else
                {
                    g.DrawPolygon(outer, Brand.Hexagon(cx, cy, r, 0));
                }
            }

            using (var fill = new SolidBrush(state == State.On ? Color.FromArgb(40, Brand.Cyan) : Brand.Surface))
                g.FillPolygon(fill, Brand.Hexagon(cx, cy, r * 0.74f, 0));
            using (var inner = new Pen(Color.FromArgb(state == State.Off ? 255 : 160, state == State.Off ? Brand.Elevated : accent), 2))
                g.DrawPolygon(inner, Brand.Hexagon(cx, cy, r * 0.74f, 0));

            using (Font f = Brand.Display(26))
            using (var b = new SolidBrush(state == State.Off ? Brand.TextDim : accent))
            using (StringFormat sf = Brand.Centered())
                g.DrawString(Caption, f, b, new RectangleF(0, 0, Width, Height), sf);
        }
    }

    class HudButton : BrandControl
    {
        public bool Outlined;
        public float FontSize = 19;
        bool hover;

        public HudButton() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Brand.Quality(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Brand.Chamfer(r, 12))
            {
                Color text;
                if (!Enabled)
                {
                    using (var b = new SolidBrush(Brand.Elevated)) g.FillPath(b, path);
                    text = Brand.TextDim;
                }
                else if (Outlined)
                {
                    using (var b = new SolidBrush(hover ? Color.FromArgb(30, Brand.Red) : Brand.Bg)) g.FillPath(b, path);
                    using (var p = new Pen(Brand.Red, 2)) g.DrawPath(p, path);
                    text = Brand.Red;
                }
                else
                {
                    using (var b = new SolidBrush(hover ? Brand.Red : Brand.RedDeep)) g.FillPath(b, path);
                    text = Color.White;
                }
                using (Font f = Brand.Display(FontSize))
                using (var b = new SolidBrush(text))
                using (StringFormat sf = Brand.Centered())
                    g.DrawString(Text, f, b, r, sf);
            }
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    class Segmented : BrandControl
    {
        public string[] Items = new string[0];
        int selected;
        int hover = -1;
        public event EventHandler SelectedChanged;

        public Segmented() { Cursor = Cursors.Hand; }

        public int Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        int IndexAt(int x)
        {
            if (Items.Length == 0) return -1;
            int i = Math.Max(0, Math.Min(Items.Length - 1, x * Items.Length / Math.Max(1, Width)));
            return L.Rtl ? Items.Length - 1 - i : i;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            int i = IndexAt(e.X);
            if (i < 0 || i == selected) return;
            Selected = i;
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Brand.Quality(g);
            float w = (Width - 1f) / Math.Max(1, Items.Length);
            using (var bg = new SolidBrush(Brand.Bg)) g.FillRectangle(bg, 0, 0, Width - 1, Height - 1);
            using (var border = new Pen(Brand.Line)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            for (int i = 0; i < Items.Length; i++)
            {
                int slot = L.Rtl ? Items.Length - 1 - i : i;
                var r = new RectangleF(slot * w + 3, 3, w - 6, Height - 7);
                bool sel = i == selected;
                if (sel)
                    using (var b = new SolidBrush(Enabled ? Brand.Red : Brand.Elevated)) g.FillRectangle(b, r);
                else if (i == hover && Enabled)
                    using (var b = new SolidBrush(Brand.Elevated)) g.FillRectangle(b, r);
                using (Font f = Brand.UiStrong(12))
                using (var b = new SolidBrush(sel && Enabled ? Color.White : Brand.TextDim))
                using (StringFormat sf = Brand.Centered())
                    g.DrawString(L.Rtl ? Items[i] : Items[i].ToUpperInvariant(), f, b, r, sf);
            }
        }
    }

    class Toggle : BrandControl
    {
        bool on;
        public event EventHandler CheckedChanged;

        public Toggle() { Cursor = Cursors.Hand; }

        public bool Checked
        {
            get { return on; }
            set { on = value; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            Checked = !on;
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Brand.Quality(g);
            float h = Height - 2, w = Width - 2;
            using (var track = new GraphicsPath())
            {
                track.AddArc(1, 1, h, h, 90, 180);
                track.AddArc(w - h + 1, 1, h, h, 270, 180);
                track.CloseFigure();
                using (var b = new SolidBrush(on ? Color.FromArgb(70, Brand.Cyan) : Brand.Bg)) g.FillPath(b, track);
                using (var p = new Pen(on ? Brand.Cyan : Brand.Line, 1.5f)) g.DrawPath(p, track);
            }
            // Knob moves toward the reading direction's end when on.
            bool right = on != L.Rtl;
            float knob = h - 8, x = right ? w - knob - 3 : 5;
            using (var b = new SolidBrush(on ? Brand.Cyan : Brand.TextDim)) g.FillEllipse(b, x, 5, knob, knob);
        }
    }

    // Dark, owner-drawn drop-down.
    class BrandCombo : ComboBox
    {
        public BrandCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Brand.Bg;
            ForeColor = Brand.Text;
            ItemHeight = 26;
            Cursor = Cursors.Hand;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(hot ? Brand.Elevated : Brand.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
            if (hot) using (var accent = new SolidBrush(Brand.Red))
                e.Graphics.FillRectangle(accent, L.Rtl ? e.Bounds.Right - 3 : e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            Brand.Quality(e.Graphics);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            if (L.Rtl) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            var r = new RectangleF(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
            using (Font f = Brand.Ui(13))
            using (var t = new SolidBrush(Enabled ? Brand.Text : Brand.TextDim))
                e.Graphics.DrawString(Items[e.Index].ToString(), f, t, r, sf);
            sf.Dispose();
        }
    }

    class LinkText : BrandControl
    {
        public Color Ink = Brand.Cyan;
        public float FontSize = 11;
        public bool Underline = true;
        public StringAlignment Align = StringAlignment.Center;

        public LinkText() { Cursor = Cursors.Hand; }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Brand.Quality(e.Graphics);
            using (Font baseFont = Brand.UiStrong(FontSize))
            using (Font f = Underline ? new Font(baseFont, baseFont.Style | FontStyle.Underline) : (Font)baseFont.Clone())
            using (var b = new SolidBrush(Enabled ? Ink : Brand.Line))
            using (StringFormat sf = Brand.Centered())
            {
                sf.Alignment = Align;
                e.Graphics.DrawString(Text, f, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    // Plain painted text (labels with brand fonts and RTL support).
    class TextLine : LinkText
    {
        public TextLine() { Cursor = Cursors.Default; Underline = false; Ink = Brand.TextDim; }
        public bool Strong = true;
        public bool Display;

        protected override void OnPaint(PaintEventArgs e)
        {
            Brand.Quality(e.Graphics);
            using (Font f = Display ? Brand.Display(FontSize) : Strong ? Brand.UiStrong(FontSize) : Brand.Ui(FontSize))
            using (var b = new SolidBrush(Ink))
            using (StringFormat sf = Brand.Centered())
            {
                sf.Alignment = Align;
                e.Graphics.DrawString(Text, f, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Brand.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Brand.Quality(e.Graphics);
            using (var p = new Pen(Brand.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            // Red tick at the top corner, like the VALSYNC HUD panels.
            using (var b = new SolidBrush(Brand.Red))
                e.Graphics.FillRectangle(b, L.Rtl ? Width - 40 : 0, 0, 40, 3);
        }
    }

    // ---------------------------------------------------------------- main window

    class MainForm : Form
    {
        readonly bool preview;
        Settings settings;
        bool busy, connected;

        readonly Image logo;
        readonly TextLine title = new TextLine { Display = true, FontSize = 26, Ink = Color.White };
        readonly TextLine subtitle = new TextLine { FontSize = 11, Strong = false };
        readonly HudButton langButton = new HudButton { Outlined = true, FontSize = 13 };
        readonly StatusOrb orb = new StatusOrb();
        readonly TextLine status = new TextLine { Display = true, FontSize = 20 };
        readonly TextLine detail = new TextLine { FontSize = 11, Strong = false };
        readonly HudButton connectButton = new HudButton();
        readonly LinkText redetect = new LinkText { FontSize = 10.5f };
        readonly Card card = new Card();
        readonly TextLine websitesLabel = new TextLine { FontSize = 10.5f, Ink = Brand.TextDim };
        readonly BrandCombo webCombo = new BrandCombo();
        readonly TextLine voiceLabel = new TextLine { FontSize = 10.5f, Ink = Brand.TextDim };
        readonly Segmented voiceSeg = new Segmented();
        readonly TextLine voiceHint = new TextLine { FontSize = 10.5f, Strong = false };
        readonly Toggle autoStart = new Toggle();
        readonly TextLine autoStartLabel = new TextLine { FontSize = 12, Ink = Brand.Text };
        readonly TextLine note = new TextLine { FontSize = 10.5f, Strong = false };
        readonly LinkText uninstall = new LinkText { FontSize = 10, Ink = Brand.TextDim };
        readonly TextLine version = new TextLine { FontSize = 10, Strong = false, Ink = Brand.Line };

        public MainForm(bool preview)
        {
            this.preview = preview;
            Brand.LoadFonts();
            settings = preview ? new Settings { Lang = "en" } : Settings.Load();
            L.Lang = settings.Lang;
            logo = Brand.Logo();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "VALSYNC Nile";
            ClientSize = new Size(Px(440), Px(700));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Brand.Bg;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Controls.AddRange(new Control[] { title, subtitle, langButton, orb, status, detail, connectButton, redetect, card, note, uninstall, version });
            card.Controls.AddRange(new Control[] { websitesLabel, webCombo, voiceLabel, voiceSeg, voiceHint, autoStart, autoStartLabel });
            version.Text = "v" + Engine.Version;

            langButton.Click += delegate { settings.Lang = L.Rtl ? "en" : "ar"; L.Lang = settings.Lang; settings.Save(); ApplyLanguage(); };
            connectButton.Click += OnConnectClick;
            redetect.Click += OnRedetectClick;
            webCombo.SelectionChangeCommitted += OnSettingChanged;
            voiceSeg.SelectedChanged += OnSettingChanged;
            autoStart.CheckedChanged += OnAutoStartChanged;
            uninstall.Click += OnUninstallClick;

            ApplyLanguage();
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark title bar on Windows 10 20H1+ and 11 (ignored elsewhere).
            try { int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (preview) return;
            SetBusy(L.T("preparing"));
            try
            {
                await Task.Run(() => Engine.Extract());
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            busy = false;
            RefreshState();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Brand.Quality(g);
            // Blueprint grid + header band.
            using (var grid = new Pen(Color.FromArgb(10, Brand.Cyan)))
            {
                for (int x = 0; x < ClientSize.Width; x += 22) g.DrawLine(grid, x, 0, x, ClientSize.Height);
                for (int y = 0; y < ClientSize.Height; y += 22) g.DrawLine(grid, 0, y, ClientSize.Width, y);
            }
            int header = Px(84);
            using (var band = new SolidBrush(Color.FromArgb(235, Brand.Surface))) g.FillRectangle(band, 0, 0, ClientSize.Width, header);
            using (var red = new SolidBrush(Brand.Red)) g.FillRectangle(red, 0, header - Px(2), ClientSize.Width, Px(2));
            if (logo != null)
            {
                int s = Px(44), x = L.Rtl ? ClientSize.Width - Px(20) - s : Px(20);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, x, Px(20), s, s);
            }
        }

        static readonly float Dpi = SystemDpi();
        static float SystemDpi() { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; }
        static int Px(int v) { return (int)Math.Round(v * Dpi / 96f); }

        // Positions are in 96-DPI units inside a container of the given width; mirrored for Arabic.
        Rectangle At(int x, int y, int w, int h, int containerWidth)
        {
            int px = L.Rtl ? containerWidth - x - w : x;
            return new Rectangle(Px(px), Px(y), Px(w), Px(h));
        }

        void Place(Control c, int x, int y, int w, int h, int containerWidth) { c.Bounds = At(x, y, w, h, containerWidth); }

        void ApplyLanguage()
        {
            RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No;
            StringAlignment near = L.Rtl ? StringAlignment.Far : StringAlignment.Near;
            const int W = 440, CW = 400;

            Place(title, 76, 14, 260, 36, W);
            Place(subtitle, 78, 50, 270, 22, W);
            Place(langButton, 340, 26, 82, 32, W);
            title.Align = subtitle.Align = near;
            title.Text = L.Rtl ? "VALSYNC Nile" : "VALSYNC  NILE";
            subtitle.Text = L.T("subtitle");
            langButton.Text = L.T("lang");

            Place(orb, 140, 100, 160, 160, W);
            Place(status, 0, 266, W, 32, W);
            Place(detail, 20, 298, 400, 24, W);
            Place(connectButton, 70, 330, 300, 56, W);
            Place(redetect, 70, 392, 300, 24, W);

            Place(card, 20, 426, CW, 206, W);
            Place(websitesLabel, 16, 14, 368, 20, CW);
            Place(webCombo, 16, 36, 368, 30, CW);
            Place(voiceLabel, 16, 78, 368, 20, CW);
            Place(voiceSeg, 16, 100, 368, 36, CW);
            Place(voiceHint, 16, 138, 368, 20, CW);
            Place(autoStart, 16, 170, 46, 24, CW);
            Place(autoStartLabel, 70, 170, 314, 24, CW);
            websitesLabel.Align = voiceLabel.Align = voiceHint.Align = autoStartLabel.Align = near;
            websitesLabel.Text = L.T("websites");
            voiceLabel.Text = L.T("voice");
            autoStartLabel.Text = L.T("autostart");

            Place(note, 20, 640, 400, 34, W);
            Place(uninstall, 20, 674, 120, 20, W);
            Place(version, 300, 674, 120, 20, W);
            uninstall.Align = near;
            version.Align = L.Rtl ? StringAlignment.Near : StringAlignment.Far;
            uninstall.Text = L.T("uninstall");
            redetect.Text = L.T("redetect");

            webCombo.RightToLeft = RightToLeft;
            webCombo.Items.Clear();
            for (int i = 0; i <= Engine.WebModes; i++) webCombo.Items.Add(L.T("web" + i));
            webCombo.SelectedIndex = settings.Web;
            voiceSeg.Items = new[] { L.T("voice0"), L.T("voice1"), L.T("voice2") };
            voiceSeg.Selected = Engine.Is64 ? settings.Voice : 0;
            voiceSeg.Enabled = Engine.Is64;
            autoStart.Checked = settings.AutoStart;

            RefreshState();
            Invalidate(true);
        }

        void RefreshState()
        {
            if (busy) return;
            connected = !preview && Engine.IsRunning(Engine.WebService);
            ShowState(connected ? StatusOrb.State.On : StatusOrb.State.Off, null);
        }

        void ShowState(StatusOrb.State state, string busyText)
        {
            orb.Current = state;
            orb.Caption = L.T(state == StatusOrb.State.On ? "orb_on" : "orb_off");
            if (state == StatusOrb.State.Busy) orb.Caption = "";
            status.Text = L.T(state == StatusOrb.State.On ? "on" : state == StatusOrb.State.Busy ? "busy" : "off");
            status.Ink = state == StatusOrb.State.On ? Brand.Cyan : state == StatusOrb.State.Busy ? Brand.Gold : Brand.TextDim;

            int mode = settings.Web == 0 ? settings.Detected : settings.Web;
            if (state == StatusOrb.State.Busy) detail.Text = busyText;
            else if (state == StatusOrb.State.On)
                detail.Text = string.Format(L.T("detail_on"), mode == 0 ? 1 : mode, L.T("voice" + (Engine.Is64 ? settings.Voice : 0)));
            else detail.Text = L.T("detail_off");

            bool idle = state != StatusOrb.State.Busy;
            connectButton.Enabled = idle;
            connectButton.Outlined = state == StatusOrb.State.On;
            connectButton.Text = L.T(state == StatusOrb.State.On ? "disconnect" : "connect");
            redetect.Visible = settings.Web == 0 && settings.Detected != 0;
            redetect.Enabled = idle;
            webCombo.Enabled = idle;
            voiceSeg.Enabled = idle && Engine.Is64;
            autoStart.Enabled = idle;
            uninstall.Enabled = idle;
            langButton.Enabled = idle;
            voiceHint.Text = Engine.Is64 ? L.T("vhint" + voiceSeg.Selected) : L.T("vhint32");
            note.Text = L.T(state == StatusOrb.State.On ? "note_on" : "note_off");
            status.Invalidate();
            connectButton.Invalidate();
        }

        void SetBusy(string text)
        {
            busy = true;
            ShowState(StatusOrb.State.Busy, text);
        }

        // Runs on a worker thread; progress text is marshalled back to the UI.
        void Progress(string text)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => ShowState(StatusOrb.State.Busy, text)));
            else ShowState(StatusOrb.State.Busy, text);
        }

        // ---- actions

        async void OnConnectClick(object sender, EventArgs e)
        {
            if (busy) return;
            if (connected)
            {
                SetBusy(L.T("disconnecting"));
                await RunSafe(() => { Engine.Remove(Engine.VoiceService); Engine.Remove(Engine.WebService); return true; });
                Done();
                return;
            }
            await ConnectAsync(false);
        }

        async void OnRedetectClick(object sender, EventArgs e)
        {
            if (busy) return;
            await ConnectAsync(true);
        }

        async void OnSettingChanged(object sender, EventArgs e)
        {
            settings.Web = Math.Max(0, webCombo.SelectedIndex);
            settings.Voice = voiceSeg.Selected;
            settings.Save();
            voiceHint.Text = Engine.Is64 ? L.T("vhint" + settings.Voice) : L.T("vhint32");
            // While connected, a change applies right away.
            if (connected && !busy) await ConnectAsync(false);
            else ShowState(connected ? StatusOrb.State.On : StatusOrb.State.Off, null);
        }

        async void OnAutoStartChanged(object sender, EventArgs e)
        {
            settings.AutoStart = autoStart.Checked;
            settings.Save();
            bool auto = settings.AutoStart;
            await Task.Run(() => { Engine.SetAutoStart(Engine.WebService, auto); Engine.SetAutoStart(Engine.VoiceService, auto); });
        }

        async Task ConnectAsync(bool forceDetect)
        {
            SetBusy(L.T("connecting"));
            int mode = settings.Web;
            if (mode == 0)
            {
                if (forceDetect || settings.Detected == 0)
                {
                    int found = await Task.Run(() => Detect());
                    if (found < 0)
                    {
                        Done();
                        Message(L.T("startfail"), MessageBoxIcon.Warning);
                        return;
                    }
                    if (found == 0)
                    {
                        found = 1;
                        Message(L.T("noprofile"), MessageBoxIcon.Information);
                    }
                    settings.Detected = found;
                    settings.Save();
                }
                mode = settings.Detected;
            }

            int voice = Engine.Is64 ? settings.Voice : 0;
            bool auto = settings.AutoStart;
            Progress(L.T("connecting"));
            bool ok = await RunSafe(() => Apply(mode, voice, auto));
            Done();
            if (!ok) Message(L.T("startfail"), MessageBoxIcon.Warning);
        }

        // Returns the first mode that opens every test site, 0 if none did, -1 if the engine never started.
        int Detect()
        {
            Progress(L.T("checking"));
            Engine.Remove(Engine.VoiceService);
            Engine.Stop(Engine.WebService);
            Engine.FlushDns();
            int total = Engine.TestUrls.Length;
            int direct = Engine.Reachable();
            if (direct == total) return 1; // nothing blocked here, keep the recommended mode

            int best = 0, bestScore = direct, started = 0;
            for (int i = 0; i < Engine.DetectOrder.Length; i++)
            {
                int mode = Engine.DetectOrder[i];
                Progress(string.Format(L.T("testing"), i + 1, Engine.DetectOrder.Length));
                Engine.Install(Engine.WebService, "VALSYNC Nile", "VALSYNC Nile website unblocking (testing)",
                               Engine.WebExe, Engine.WebArgs(mode), false);
                if (!Engine.Start(Engine.WebService)) continue;
                started++;
                Thread.Sleep(800);
                Engine.FlushDns();
                int score = Engine.Reachable();
                Engine.Stop(Engine.WebService);
                if (score > bestScore) { best = mode; bestScore = score; }
                if (score == total) break;
            }
            return started == 0 ? -1 : best;
        }

        static bool Apply(int mode, int voice, bool auto)
        {
            Engine.Install(Engine.WebService, "VALSYNC Nile", "VALSYNC Nile: unblocks websites in Egypt (mode " + mode + ").",
                           Engine.WebExe, Engine.WebArgs(mode), auto);
            Engine.Stop(Engine.WebService);
            if (!Engine.Start(Engine.WebService)) return false;

            if (voice > 0)
            {
                Engine.Install(Engine.VoiceService, "VALSYNC Nile Voice", "VALSYNC Nile: unblocks game voice chat and calls in Egypt.",
                               Engine.VoiceExe, Engine.VoiceArgs(voice), auto);
                Engine.Stop(Engine.VoiceService);
                if (!Engine.Start(Engine.VoiceService)) return false;
            }
            else
            {
                Engine.Remove(Engine.VoiceService);
            }
            Engine.FlushDns();
            return true;
        }

        async void OnUninstallClick(object sender, EventArgs e)
        {
            if (busy) return;
            if (MessageBox.Show(this, L.T("uninstall_confirm"), "VALSYNC Nile", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                MessageBoxDefaultButton.Button2, L.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0) != DialogResult.Yes)
                return;
            SetBusy(L.T("disconnecting"));
            await RunSafe(() =>
            {
                Engine.Remove(Engine.VoiceService);
                Engine.Remove(Engine.WebService);
                Engine.RemoveDrivers();
                try { Directory.Delete(Engine.Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return true;
            });
            busy = false;
            Message(L.T("uninstalled"), MessageBoxIcon.Information);
            Close();
        }

        async Task<bool> RunSafe(Func<bool> work)
        {
            try
            {
                return await Task.Run(work);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return false;
            }
        }

        void Done()
        {
            busy = false;
            RefreshState();
        }

        void ShowError(Exception ex) { Message(string.Format(L.T("error"), ex.Message), MessageBoxIcon.Error); }

        void Message(string text, MessageBoxIcon icon)
        {
            MessageBox.Show(this, text, "VALSYNC Nile", MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1,
                            L.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
        }

        // ---- design preview (renders the window to a PNG without touching services)

        public static int Preview(string png, string lang, string state)
        {
            using (var form = new MainForm(true))
            {
                L.Lang = lang == "ar" ? "ar" : "en";
                form.settings.Lang = L.Lang;
                form.ApplyLanguage();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                if (state == "on")
                {
                    form.settings.Detected = 1;
                    form.ShowState(StatusOrb.State.On, null);
                }
                else if (state == "busy")
                {
                    form.ShowState(StatusOrb.State.Busy, string.Format(L.T("testing"), 2, 5));
                }
                Application.DoEvents();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                    bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            return 0;
        }
    }
}
