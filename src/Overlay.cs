using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GamepadOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new OverlayForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Gamepad Overlay");
            }
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int GWL_EXSTYLE      = -20;
        private const int WS_EX_LAYERED    = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_HOTKEY        = 0x0312;
        private const int WM_NCHITTEST     = 0x0084;
        private const int HTCLIENT         = 1;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWl(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWl(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);

        private readonly WebView2 _web;
        private NotifyIcon _tray;
        private string _pageUrl;
        private bool _clickThrough = true;
        private bool _invY;
        private bool _topMost = true;
        private bool _opaque;
        private bool _probe;
        private string _anchor = "tr";
        private double _scale = 1.0;
        private int _margin = 12;
        private int _posX = int.MinValue;
        private int _posY = int.MinValue;

        private const int HK_CLICK = 0x0B01;
        private const int HK_TOP   = 0x0B02;
        private const int HK_EXIT  = 0x0B03;
        private const int HK_NW    = 0x0B04;
        private const int HK_NW2   = 0x0B05;
        private const int HK_NS    = 0x0B06;
        private const int HK_NE    = 0x0B07;
        private const int HK_TINY  = 0x0B08;
        private const int HK_BIG   = 0x0B09;
        private const uint MOD_CTRL_ALT = 0x0003;

        [StructLayout(LayoutKind.Sequential)]
        private struct XIPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XISTATE
        {
            public uint dwPacketNumber;
            public XIPAD pad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState(uint index, out XISTATE state);

        private static readonly int[] XI_BUTTON =
        {
            0x1000, 0x2000, 0x4000, 0x8000,   // A B X Y
            0x0100, 0x0200,                   // LB RB
            -1, -1,                           // LT RT（模拟量）
            0x0020, 0x0010,                   // VIEW MENU
            0x0040, 0x0080,                   // L3 R3
            0x0001, 0x0002, 0x0004, 0x0008,   // ↑ ↓ ← →
            0x0400                            // GUIDE
        };

        private Timer _bridgeTimer;
        private Timer _topTimer;
        private bool _noNative;
        private string _lastJson = "";
        private int _tickCount;

        /* ---------------- 蓝牙手柄电量 ----------------
           蓝牙 LE 手柄的电量由 Windows 存在设备的属性仓库里：
             DEVPKEY_Bluetooth_Battery = {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2  (DEVPROP_TYPE_BYTE)
           XInput 的 XInputGetBatteryInformation 对这种 BLE 手柄只会返回
           BATTERY_TYPE_DISCONNECTED，拿不到真实电量，所以这里直接读属性仓库。 */
        private static readonly Guid GUID_BT_BATTERY = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
        private const uint BT_BATTERY_PID = 2;
        private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000001;
        private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, uint len, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key,
                                                          out uint propType, byte[] buffer,
                                                          ref uint size, uint flags);

        private int _battery = -1;
        // ---- PresentMon (real game FPS) ----
        private IntPtr _pmSess = IntPtr.Zero;
        private IntPtr _pmQuery = IntPtr.Zero;
        private uint _pmPid = 0;
        private double _gameFps = -1;
        private bool _pmOk = false;
        private IntPtr _pmBlob = IntPtr.Zero;

        private static string[] DeviceIds(string enumerator)
        {
            uint len = 0;
            if (CM_Get_Device_ID_List_SizeW(out len, enumerator, CM_GETIDLIST_FILTER_PRESENT) != 0 || len == 0)
                return new string[0];
            char[] buf = new char[len];
            if (CM_Get_Device_ID_ListW(enumerator, buf, len, CM_GETIDLIST_FILTER_PRESENT) != 0)
                return new string[0];
            return new string(buf).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /* 从 HID 设备实例 ID 里抠出 12 位蓝牙地址：
           HID\{...}&DEV&VID_045E&PID_0B13&REV_0509&AC8EBD493E96&IG_00\... */
        private static string FindBtAddress()
        {
            foreach (string id in DeviceIds("HID"))
            {
                if (id.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (id.IndexOf("PID_0B13", StringComparison.OrdinalIgnoreCase) < 0 &&
                    id.IndexOf("PID_0B20", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] parts = id.Split('&');
                foreach (string part in parts)
                {
                    if (part.Length != 12) continue;
                    bool hex = true;
                    for (int i = 0; i < 12; i++)
                    {
                        char c = part[i];
                        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) { hex = false; break; }
                    }
                    if (hex) return part;
                }
            }
            return null;
        }

        private static int ReadBattery()
        {
            try
            {
                string addr = FindBtAddress();
                if (addr == null) return -1;   // 有线连接 / 走 USB 时没有 BTHLE 节点
                foreach (string id in DeviceIds("BTHLE"))
                {
                    if (id.IndexOf(addr, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    uint inst;
                    if (CM_Locate_DevNodeW(out inst, id, CM_LOCATE_DEVNODE_NORMAL) != 0) continue;
                    DEVPROPKEY key = new DEVPROPKEY();
                    key.fmtid = GUID_BT_BATTERY;
                    key.pid = BT_BATTERY_PID;
                    byte[] buf = new byte[8];
                    uint size = (uint)buf.Length;
                    uint type;
                    if (CM_Get_DevNode_PropertyW(inst, ref key, out type, buf, ref size, 0) != 0) continue;
                    if (size >= 1) return buf[0];
                }
            }
            catch { }
            return -1;
        }

        private void StartBatteryPoll()
        {
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                while (true)
                {
                    try { _battery = ReadBattery(); }
                    catch { _battery = -1; }
                    System.Threading.Thread.Sleep(15000);
                }
            });
            t.IsBackground = true;
            t.Start();
        }



        /* 页面报上来的卡片矩形（物理像素，相对客户区），用于精确贴边 */
        private double _cardL, _cardT, _cardW, _cardH;
        private bool _haveRect;
        private bool _freePos;

        public OverlayForm(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _pageUrl = Path.Combine(baseDir, "index.html");
            _pageUrl = new Uri(_pageUrl).AbsoluteUri;

            int w = 900, h = 700;
            string query = "";
            bool sizeGiven = false;
            bool scaleGiven = false;
            bool anchorGiven = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string low = a.ToLowerInvariant();
                if (low.StartsWith("--anchor="))    { _anchor = a.Substring(9); anchorGiven = true; }
                else if (low.StartsWith("--w="))    { int.TryParse(a.Substring(4), out w); sizeGiven = true; }
                else if (low.StartsWith("--h="))    { int.TryParse(a.Substring(4), out h); sizeGiven = true; }
                else if (low.StartsWith("--x="))    int.TryParse(a.Substring(4), out _posX);
                else if (low.StartsWith("--y="))    int.TryParse(a.Substring(4), out _posY);
                else if (low.StartsWith("--margin=")) int.TryParse(a.Substring(9), out _margin);
                else if (low.StartsWith("--scale=")) { double.TryParse(a.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _scale); scaleGiven = true; }
                else if (low == "--click-through")  _clickThrough = true;
                else if (low == "--no-click-through") _clickThrough = false;
                else if (low == "--opaque")         _opaque = true;
                else if (low == "--probe")          _probe = true;
                else if (low == "--no-native")      _noNative = true;
                else if (low == "--no-top")         _topMost = false;
                else if (low.StartsWith("--query=")) query = a.Substring(8);
                else if (!low.StartsWith("--"))     _pageUrl = a;
            }

            LoadSettings(ref w, ref h, sizeGiven, scaleGiven, anchorGiven);

            if (_opaque) query = AppendQ(query, "bg=dark");
            else         query = AppendQ(query, "bg=window");
            if (Math.Abs(_scale - 1.0) > 0.001)
                query = AppendQ(query, "scale=" + _scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (query.Length > 0) _pageUrl = _pageUrl + "?" + query;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = _topMost;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = _opaque ? Color.FromArgb(8, 10, 15) : Color.Black;
            if (!_opaque) { BackColor = Color.Fuchsia; TransparencyKey = Color.Fuchsia; }
            Text            = "Gamepad Overlay";
            KeyPreview      = true;
            ClientSize      = new Size(w, h);
            MinimumSize     = new Size(200, 140);

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            try { _web.DefaultBackgroundColor = _opaque ? Color.FromArgb(8, 10, 15) : Color.Transparent; }
            catch { }
            Controls.Add(_web);

            if (_posX != int.MinValue && _posY != int.MinValue) { Location = new Point(_posX, _posY); _freePos = true; }
            else Place(_anchor);

            BuildTray();
            Load += OnLoaded;
        }

        private static string AppendQ(string q, string kv)
        {
            return q.Length == 0 ? kv : q + "&" + kv;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey(Handle, HK_CLICK, MOD_CTRL_ALT, 0x4F); // O
            RegisterHotKey(Handle, HK_TOP,   MOD_CTRL_ALT, 0x54); // T
            RegisterHotKey(Handle, HK_EXIT,  MOD_CTRL_ALT, 0x58); // X
            RegisterHotKey(Handle, HK_NW,    MOD_CTRL_ALT, 0x57); // W
            RegisterHotKey(Handle, HK_NW2,   MOD_CTRL_ALT, 0x41); // A
            RegisterHotKey(Handle, HK_NS,    MOD_CTRL_ALT, 0x53); // S
            RegisterHotKey(Handle, HK_NE,    MOD_CTRL_ALT, 0x44); // D
            RegisterHotKey(Handle, HK_TINY,  MOD_CTRL_ALT, 0x5B); // [
            RegisterHotKey(Handle, HK_BIG,   MOD_CTRL_ALT, 0x5D); // ]
        }

        // WinForms 在 Show() 之后会重写扩展样式，所以放在 OnShown 里统一应用
        private void ReassertTopmost()
        {
            if (!_topMost || !IsHandleCreated) return;
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        private void ApplyExStyle()
        {
            long ex = GetWl(Handle, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (!_opaque) ex |= WS_EX_LAYERED;
            if (_clickThrough) ex |= WS_EX_TRANSPARENT;
            else               ex &= ~((long)WS_EX_TRANSPARENT);
            SetWl(Handle, GWL_EXSTYLE, new IntPtr(ex));
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyExStyle();
        }

        private async void OnLoaded(object sender, EventArgs e)
        {
            try
            {
                string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_wv2data");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await _web.EnsureCoreWebView2Async(env);

                CoreWebView2Settings s = _web.CoreWebView2.Settings;
                s.AreDefaultContextMenusEnabled    = false;
                s.AreDevToolsEnabled               = false;
                s.IsStatusBarEnabled               = false;
                s.IsZoomControlEnabled             = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsPasswordAutosaveEnabled        = false;
                s.IsGeneralAutofillEnabled         = false;

                _web.CoreWebView2.Navigate(_pageUrl);
                _web.CoreWebView2.WebMessageReceived += OnWebMessage;
                _web.CoreWebView2.NavigationCompleted += delegate
                {
                    if (Math.Abs(_scale - 1.0) > 0.001) SetScale(_scale);
                    ApplyInvY();
                };

                if (!_noNative) StartBridge();
                if (_probe) StartProbe();
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 初始化失败：\n" + ex.Message, "Gamepad Overlay");
                Close();
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                if (json == null) return;

                if (json.IndexOf("\"rect\"", StringComparison.Ordinal) >= 0)
                {
                    double x = ReadNum(json, "x"), y = ReadNum(json, "y");
                    double w = ReadNum(json, "w"), h = ReadNum(json, "h"), vw = ReadNum(json, "vw");
                    if (vw > 1 && w > 1)
                    {
                        double k = Width / vw;   /* CSS px -> 物理 px（高 DPI 会差一截） */
                        _cardL = x * k; _cardT = y * k; _cardW = w * k; _cardH = h * k;
                        _haveRect = true;

                        if (!_freePos) Place(_anchor);
                    }
                    return;
                }

                if (json.IndexOf("drag", StringComparison.Ordinal) >= 0)
                {
                    _freePos = true;
                    ReleaseCapture();
                    SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, new IntPtr(2 /*HTCAPTION*/), IntPtr.Zero);
                }
            }
            catch { }
        }

        private static double ReadNum(string s, string key)
        {
            string pat = "\"" + key + "\":";
            int i = s.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return double.NaN;
            i += pat.Length;
            int j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '-' || s[j] == '+' ||
                                    s[j] == '.' || s[j] == 'e' || s[j] == 'E')) j++;
            double d;
            double.TryParse(s.Substring(i, j - i), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out d);
            return d;
        }

        private void StartBridge()
        {
            _bridgeTimer = new Timer();
            _bridgeTimer.Interval = 16;
            _bridgeTimer.Tick += delegate { PushPads(); };
            _bridgeTimer.Start();

            // 少数全屏游戏会把自己的窗口设成 topmost、或系统把我们的置顶丢掉；
            // 每 1.2 秒把窗口重新压到 topmost 最上层（NOACTIVATE，不会抢焦点）
            _topTimer = new Timer();
            _topTimer.Interval = 1200;
            _topTimer.Tick += delegate { ReassertTopmost(); };
            _topTimer.Start();
            StartBatteryPoll();
            StartFpsPoll();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PmElem { public int metric; public int stat; public uint deviceId; public uint arrayIndex; public ulong dataOffset; public ulong dataSize; }

        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmOpenSession(out IntPtr h);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmCloseSession(IntPtr h);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmStartTrackingProcess(IntPtr h, uint pid);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmStopTrackingProcess(IntPtr h, uint pid);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmRegisterDynamicQuery(IntPtr h, out IntPtr q, PmElem[] els, ulong n, double winMs, double offMs);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmPollDynamicQuery(IntPtr q, uint pid, IntPtr blob, ref uint nsc);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmFreeDynamicQuery(IntPtr q);
        [DllImport("PresentMonAPI2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pmSetEtwFlushPeriod(IntPtr h, uint ms);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private static void PmLog(string msg)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ov_pm.log"), DateTime.Now.ToString("HH:mm:ss ") + msg + "\r\n"); } catch { }
        }

        private void StartFpsPoll()
        {
            try
            {
                IntPtr sess;
                int orc = pmOpenSession(out sess);
                if (orc != 0 || sess == IntPtr.Zero) { _pmOk = false; PmLog("open=" + orc); return; }
                _pmSess = sess;
                _pmOk = true;
                try { pmSetEtwFlushPeriod(sess, 16); } catch { }
            }
            catch (Exception ex) { _pmOk = false; PmLog("ex " + ex.GetType().Name + ": " + ex.Message); return; }
            System.Threading.Thread th = new System.Threading.Thread(FpsLoop);
            th.IsBackground = true;
            th.Start();
        }

        private void FpsLoop()
        {
            while (true)
            {
                try { FpsStep(); } catch (Exception ex) { PmLog("loop " + ex.GetType().Name + ": " + ex.Message); }
                System.Threading.Thread.Sleep(400);
            }
        }

        // ---- RTSS shared memory (game FPS, no admin needed) ----
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint offHigh, uint offLow, UIntPtr bytes);
        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        private IntPtr _rtssMap = IntPtr.Zero;
        private IntPtr _rtssView = IntPtr.Zero;

        private bool RtssOpen()
        {
            if (_rtssView != IntPtr.Zero) return true;
            IntPtr h = OpenFileMapping(0x0004, false, "RTSSSharedMemoryV2");
            if (h == IntPtr.Zero) return false;
            IntPtr v = MapViewOfFile(h, 0x0004, 0, 0, UIntPtr.Zero);
            if (v == IntPtr.Zero) { try { CloseHandle(h); } catch { } return false; }
            _rtssMap = h;
            _rtssView = v;
            return true;
        }

        private double RtssFps(uint pid)
        {
            if (!RtssOpen()) return -1;
            IntPtr p = _rtssView;
            uint sig = (uint)Marshal.ReadInt32(p, 0);
            if (sig != 0x53535452) return -1;
            int entrySize = Marshal.ReadInt32(p, 8);
            int arrOff = Marshal.ReadInt32(p, 12);
            int arrSize = Marshal.ReadInt32(p, 16);
            if (entrySize < 300 || entrySize > 8192) return -1;
            if (arrOff < 64 || arrSize <= 0 || arrSize > 256) return -1;
            for (int i = 0; i < arrSize; i++)
            {
                int b = arrOff + i * entrySize;
                if (Marshal.ReadInt32(p, b) != (int)pid) continue;
                int t0 = Marshal.ReadInt32(p, b + 268);
                int t1 = Marshal.ReadInt32(p, b + 272);
                int frames = Marshal.ReadInt32(p, b + 276);
                int dt = t1 - t0;
                if (dt <= 0 || frames < 0) return -1;
                double f = (double)frames / dt;
                if (f < 0 || f > 2000) return -1;
                return f;
            }
            return -1;
        }

        private void FpsStep()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) { _gameFps = -1; return; }
            uint pid = 0;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == 0 || pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) { _gameFps = -1; return; }

            double rf = RtssFps(pid);
            if (rf >= 0) { _gameFps = rf; return; }

            if (!_pmOk) { _gameFps = -1; return; }

            if (pid != _pmPid)
            {
                if (_pmQuery != IntPtr.Zero) { try { pmFreeDynamicQuery(_pmQuery); } catch { } _pmQuery = IntPtr.Zero; }
                if (_pmPid != 0) { try { pmStopTrackingProcess(_pmSess, _pmPid); } catch { } }
                _pmPid = pid;
                int trc = pmStartTrackingProcess(_pmSess, pid);
                if (trc != 0) { _gameFps = -1; PmLog("track pid=" + pid + " rc=" + trc); return; }
                PmElem[] el = new PmElem[1];
                el[0].metric = 12;
                el[0].stat = 1;
                el[0].deviceId = 0;
                el[0].arrayIndex = 0;
                el[0].dataOffset = 0;
                el[0].dataSize = 8;
                IntPtr q;
                int rgc = pmRegisterDynamicQuery(_pmSess, out q, el, 1, 500.0, 0.0);
                if (rgc != 0) { _gameFps = -1; PmLog("reg rc=" + rgc); return; }
                _pmQuery = q;
                if (_pmBlob == IntPtr.Zero) _pmBlob = Marshal.AllocHGlobal(8 * 16);
            }
            if (_pmQuery == IntPtr.Zero) { _gameFps = -1; return; }

            uint nsc = 8;
            for (int k = 0; k < 16; k++) Marshal.WriteInt64(_pmBlob, k * 8, 0);
            int r = pmPollDynamicQuery(_pmQuery, pid, _pmBlob, ref nsc);
            if (r != 0) { _gameFps = -1; PmLog("poll rc=" + r); return; }
            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(_pmBlob, 0));
            _gameFps = v > 0.05 ? v : 0.0;
        }

        private static double Axis(int v)
        {
            double d = v / 32767.0;
            if (d > 1.0) d = 1.0;
            if (d < -1.0) d = -1.0;
            return d;
        }

        private void PushPads()
        {
            if (_web.CoreWebView2 == null) return;

            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\"t\":\"pad\",\"pads\":[");
            int found = 0;

            for (uint i = 0; i < 4; i++)
            {
                XISTATE st;
                if (XInputGetState(i, out st) != 0) continue;

                if (found > 0) sb.Append(',');
                found++;

                sb.Append("{\"i\":").Append(i).Append(",\"id\":\"Xbox Controller ").Append(i + 1).Append("\"");

                sb.Append(",\"b\":[");
                for (int k = 0; k < 17; k++)
                {
                    if (k > 0) sb.Append(',');
                    double v;
                    if (k == 6)      v = st.pad.bLeftTrigger / 255.0;
                    else if (k == 7) v = st.pad.bRightTrigger / 255.0;
                    else if (XI_BUTTON[k] >= 0) v = ((st.pad.wButtons & XI_BUTTON[k]) != 0) ? 1.0 : 0.0;
                    else             v = 0.0;
                    sb.Append(Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                sb.Append(']');

                sb.Append(",\"lx\":").Append(Axis(st.pad.sThumbLX).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(",\"ly\":").Append(Axis(-st.pad.sThumbLY).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(",\"rx\":").Append(Axis(st.pad.sThumbRX).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(",\"ry\":").Append(Axis(-st.pad.sThumbRY).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(",\"bat\":").Append(_battery);
                sb.Append(",\"fps\":").Append(_gameFps < 0 ? "-1" : Math.Round(_gameFps, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('}');
            }

            sb.Append("]}");
            string json = sb.ToString();

            _tickCount++;
            if (json != _lastJson || (_tickCount % 32) == 0)
            {
                _lastJson = json;
                try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
            }
        }

        private void StartProbe()
        {
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_probe.log");
            try { File.Delete(log); } catch { }
            int n = 0;
            Timer t = new Timer();
            t.Interval = 1000;
            t.Tick += async (s, e) =>
            {
                n++;
                string js =
                    "(function(){try{var g=navigator.getGamepads?navigator.getGamepads():[];var o=[];" +
                    "for(var i=0;i<g.length;i++){var p=g[i];if(p)o.push({i:i,id:p.id,mp:p.mapping,c:p.connected," +
                    "nb:p.buttons.length,na:p.axes.length,pr:p.buttons.map(function(b){return b.pressed?1:0}).join('')," +
                    "ax:Array.prototype.slice.call(p.axes).map(function(v){return Math.round(v*100)/100})});}" +
                    "return JSON.stringify({n:o.length,focus:document.hasFocus(),vis:document.visibilityState,pads:o});}" +
                    "catch(err){return 'ERR '+err;}})()";
                string r = await _web.CoreWebView2.ExecuteScriptAsync(js);
                try { File.AppendAllText(log, n + " " + DateTime.Now.ToString("HH:mm:ss.fff") + "  " + r + "\r\n"); } catch { }
                if (n >= 6) { t.Stop(); t.Dispose(); Close(); }
            };
            t.Start();
        }

        private static Rectangle WorkArea()
        {
            return Screen.PrimaryScreen.WorkingArea;
        }

        private void Place(string anchor)
        {
            Rectangle wa = WorkArea();
            int m = _margin;

            double cw = _haveRect ? _cardW : Width;
            double ch = _haveRect ? _cardH : Height;
            double cl = _haveRect ? _cardL : 0;
            double ct = _haveRect ? _cardT : 0;

            int x, y;
            switch (anchor.ToLowerInvariant())
            {
                case "tl": x = (int)Math.Round(wa.Left + m - cl);                    y = (int)Math.Round(wa.Top + m - ct); break;
                case "tr": x = (int)Math.Round(wa.Right - m - cw - cl);              y = (int)Math.Round(wa.Top + m - ct); break;
                case "bl": x = (int)Math.Round(wa.Left + m - cl);                    y = (int)Math.Round(wa.Bottom - m - ch - ct); break;
                case "c":  x = (int)Math.Round(wa.Left + (wa.Width - cw) / 2 - cl);  y = (int)Math.Round(wa.Top + (wa.Height - ch) / 2 - ct); break;
                default:   x = (int)Math.Round(wa.Right - m - cw - cl);              y = (int)Math.Round(wa.Bottom - m - ch - ct); break;
            }
            Location = new Point(x, y);
        }

        /* 自由摆放：谁按都能放，允许贴到屏幕最边缘（含负坐标） */
        private void MoveBy(int dx, int dy)
        {
            _freePos = true;
            Rectangle wa = WorkArea();
            int x = Math.Max(wa.Left - Width + 60, Math.Min(wa.Right - 60, Left + dx));
            int y = Math.Max(wa.Top - Height + 60, Math.Min(wa.Bottom - 60, Top + dy));
            Location = new Point(x, y);
        }

        private void MoveToCursor()
        {
            _freePos = true;
            Point p = Cursor.Position;
            Rectangle wa = WorkArea();
            double cw = _haveRect ? _cardW : Width;
            double ch = _haveRect ? _cardH : Height;
            double cl = _haveRect ? _cardL : 0;
            double ct = _haveRect ? _cardT : 0;
            int x = (int)Math.Round(p.X - cl - cw / 2);
            int y = (int)Math.Round(p.Y - ct - ch / 2);
            x = Math.Max((int)(wa.Left - cl), Math.Min((int)(wa.Right - cw - cl), x));
            y = Math.Max((int)(wa.Top - ct), Math.Min((int)(wa.Bottom - ch - ct), y));
            Location = new Point(x, y);
        }

        private void SaveSettings()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string[] lines = {
                    "x=" + Left,
                    "y=" + Top,
                    "scale=" + _scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    "clickthrough=" + (_clickThrough ? "1" : "0"),
                    "top=" + (_topMost ? "1" : "0"),
                    "invy=" + (_invY ? "1" : "0")
                };
                File.WriteAllLines(Path.Combine(dir, "settings.ini"), lines);
            }
            catch { }
        }

        private void LoadSettings(ref int w, ref int h, bool sizeGiven, bool scaleGiven, bool anchorGiven)
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int n;
                    int.TryParse(v, out n);
                    if (k == "x" && _posX == int.MinValue && !anchorGiven) _posX = n;
                    else if (k == "y" && _posY == int.MinValue && !anchorGiven) _posY = n;
                    else if (k == "scale" && !sizeGiven && !scaleGiven)
                    {
                        double d;
                        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out d) &&
                            d >= 0.3 && d <= 2.0)
                            _scale = d;
                    }
                    else if (k == "clickthrough") _clickThrough = (v == "1");
                    else if (k == "top") _topMost = (v == "1");
                    else if (k == "invy") _invY = (v == "1");
                }
            }
            catch { }
        }

        private Icon BuildIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 150, 180, 230)))
                    g.FillEllipse(b, 5, 9, 22, 14);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 67, 217, 123)))
                    g.FillEllipse(b, 18, 17, 11, 11);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void ApplyInvY()
        {
            if (_web.CoreWebView2 == null) return;
            try
            {
                _web.CoreWebView2.ExecuteScriptAsync(
                    "window.__ol&&window.__ol.setInvY&&window.__ol.setInvY(" + (_invY ? "true" : "false") + ")");
            }
            catch { }
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = BuildIcon();
            _tray.Text = "Gamepad Overlay";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem miClick = new ToolStripMenuItem("鼠标穿透（Ctrl+Alt+O）");
            miClick.Checked = _clickThrough;
            miClick.Click += delegate
            {
                miClick.Checked = !miClick.Checked;
                SetClickThrough(miClick.Checked);
            };

            ToolStripMenuItem miTop = new ToolStripMenuItem("窗口置顶（Ctrl+Alt+T）");
            miTop.Checked = _topMost;
            miTop.Click += delegate
            {
                miTop.Checked = !miTop.Checked;
                TopMost = miTop.Checked;
                _topMost = miTop.Checked;
            };

            ToolStripMenuItem miInvY = new ToolStripMenuItem("摇杆 Y 轴反向");
            miInvY.Checked = _invY;
            miInvY.Click += delegate
            {
                miInvY.Checked = !miInvY.Checked;
                _invY = miInvY.Checked;
                ApplyInvY();
                SaveSettings();
            };

            ToolStripMenuItem miGreen = new ToolStripMenuItem("绿幕底色（给 OBS 抠像）");
            miGreen.Click += delegate { SwapBg("green"); };

            ToolStripMenuItem miDark = new ToolStripMenuItem("深色底色");
            miDark.Click += delegate { SwapBg("dark"); };

            ToolStripMenuItem miClear = new ToolStripMenuItem("透明底色");
            miClear.Click += delegate { SwapBg("window"); };

            ToolStripMenuItem miPos = new ToolStripMenuItem("吸附位置");
            string[,] spots = { { "左上角", "tl" }, { "右上角", "tr" }, { "左下角", "bl" }, { "右下角", "br" }, { "屏幕正中", "c" } };
            for (int i = 0; i < spots.GetLength(0); i++)
            {
                string code = spots[i, 1];
                ToolStripMenuItem sub = new ToolStripMenuItem(spots[i, 0]);
                sub.Click += delegate { Place(code); };
                miPos.DropDownItems.Add(sub);
            }
            miPos.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem miToCursor = new ToolStripMenuItem("移到鼠标位置");
            miToCursor.Click += delegate { MoveToCursor(); };
            miPos.DropDownItems.Add(miToCursor);

            ToolStripMenuItem miNudge = new ToolStripMenuItem("位置微调");
            string[,] nudges = { { "↑ 上移 (Ctrl+Alt+W)", "0", "-12" }, { "↓ 下移 (Ctrl+Alt+S)", "0", "12" },
                                 { "← 左移 (Ctrl+Alt+A)", "-12", "0" }, { "→ 右移 (Ctrl+Alt+D)", "12", "0" } };
            for (int i = 0; i < nudges.GetLength(0); i++)
            {
                int dx = int.Parse(nudges[i, 1]);
                int dy = int.Parse(nudges[i, 2]);
                ToolStripMenuItem sub = new ToolStripMenuItem(nudges[i, 0]);
                sub.Click += delegate { MoveBy(dx, dy); };
                miNudge.DropDownItems.Add(sub);
            }

            ToolStripMenuItem miScale = new ToolStripMenuItem("大小（Ctrl+Alt+[ / ]）");
            for (int i = 0; i < SIZES.Length; i++)
            {
                double v = SIZES[i];
                ToolStripMenuItem sub = new ToolStripMenuItem(((int)Math.Round(v * 100)) + "%");
                sub.Click += delegate { SetScale(v); };
                miScale.DropDownItems.Add(sub);
            }

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Close(); };

            menu.Items.Add(miClick);
            menu.Items.Add(miTop);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miPos);
            menu.Items.Add(miNudge);
            menu.Items.Add(miScale);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miDark);
            menu.Items.Add(miGreen);
            menu.Items.Add(miClear);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate
            {
                SetClickThrough(!_clickThrough);
                miClick.Checked = _clickThrough;
            };
        }

        private static readonly double[] SIZES = { 0.45, 0.55, 0.7, 0.85, 1.0, 1.2, 1.4 };

        private void StepScale(int dir)
        {
            int idx = -1;
            for (int i = 0; i < SIZES.Length; i++)
                if (Math.Abs(SIZES[i] - _scale) < 0.001) { idx = i; break; }
            if (idx < 0) idx = 4;
            idx = Math.Max(0, Math.Min(SIZES.Length - 1, idx + dir));
            SetScale(SIZES[idx]);
        }

        private void SetScale(double v)
        {
            _scale = v;
            if (_web != null && _web.CoreWebView2 != null)
            {
                try
                {
                    _web.CoreWebView2.ExecuteScriptAsync(
                        "document.documentElement.style.setProperty('--s-scale', '" +
                        v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "');" +
                        "window.__ol && window.__ol.report && window.__ol.report();");
                }
                catch { }
            }
            /* 卡片在窗口里居中，窗口不动就等于以卡片中心缩放 */
        }

        private void SwapBg(string bg)
        {
            if (_web.CoreWebView2 == null) return;
            _web.CoreWebView2.ExecuteScriptAsync("(function(){document.documentElement.setAttribute('data-bg', '" + bg + "');})()");
        }

        public void SetClickThrough(bool on)
        {
            _clickThrough = on;
            ApplyExStyle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HK_CLICK) SetClickThrough(!_clickThrough);
                else if (id == HK_TOP) { _topMost = !_topMost; TopMost = _topMost; }
                else if (id == HK_EXIT) { Close(); return; }
                else if (id == HK_NW)  MoveBy(0, -12);
                else if (id == HK_NW2) MoveBy(-12, 0);
                else if (id == HK_NS)  MoveBy(0, 12);
                else if (id == HK_NE)  MoveBy(12, 0);
                else if (id == HK_TINY) StepScale(-1);
                else if (id == HK_BIG)  StepScale(1);
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            if (_bridgeTimer != null) { _bridgeTimer.Stop(); _bridgeTimer.Dispose(); }
            if (_topTimer != null) { _topTimer.Stop(); _topTimer.Dispose(); }
            UnregisterHotKey(Handle, HK_CLICK);
            UnregisterHotKey(Handle, HK_TOP);
            UnregisterHotKey(Handle, HK_EXIT);
            UnregisterHotKey(Handle, HK_NW);
            UnregisterHotKey(Handle, HK_NW2);
            UnregisterHotKey(Handle, HK_NS);
            UnregisterHotKey(Handle, HK_NE);
            UnregisterHotKey(Handle, HK_TINY);
            UnregisterHotKey(Handle, HK_BIG);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnFormClosing(e);
        }
    }
}
