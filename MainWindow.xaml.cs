using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color  = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Point  = System.Windows.Point;

namespace FastBar
{
    public partial class MainWindow : Window
    {
        // ────────────────────────────────────────────────────────────────────────
        // Win32 – keyboard hook
        // ────────────────────────────────────────────────────────────────────────
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk,
            int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN    = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_SPACE      = 0x20;

        // ────────────────────────────────────────────────────────────────────────
        // Win32 – Acrylic / Glass (DWM)
        // Works on Windows 10 (SetWindowCompositionAttribute) and
        // Windows 11 (DwmSetWindowAttribute with DWMWA_SYSTEMBACKDROP_TYPE).
        // ────────────────────────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr,
            ref uint attrValue, uint attrSize);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd,
            ref WindowCompositionAttributeData data);

        private enum AccentState
        {
            ACCENT_DISABLED                   = 0,
            ACCENT_ENABLE_GRADIENT            = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND          = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND   = 4,
            ACCENT_INVALID_STATE              = 5,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint        AccentFlags;
            public uint        GradientColor; // ABGR — alpha 0x99 ≈ 60% opaque
            public uint        AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public uint    Attribute;  // WCA_ACCENT_POLICY = 19
            public IntPtr  Data;
            public int     SizeOfData;
        }

        // DWMWA_SYSTEMBACKDROP_TYPE = 38  (Windows 11 22H2+)
        // Values: 0=auto, 1=none, 2=mica, 3=acrylic/tabbed, 4=acrylic (transient)
        private const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const uint DWMSBT_TRANSIENTWINDOW    = 4; // acrylic

        private static bool _isWin11 =
            Environment.OSVersion.Version >= new Version(10, 0, 22000);

        // ────────────────────────────────────────────────────────────────────────
        // Fields
        // ────────────────────────────────────────────────────────────────────────
        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr _hookID = IntPtr.Zero;

        private readonly ConcurrentDictionary<string, string> _shortcuts =
            new(StringComparer.OrdinalIgnoreCase);

        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private DateTime _lastShown = DateTime.MinValue;
        private bool _isDarkTheme = false;

        private static readonly string _settingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastBar");
        private static readonly string _themeFile =
            Path.Combine(_settingsDir, "theme.txt");
        private static readonly string _prefixesFile = 
            Path.Combine(_settingsDir, "prefixes.txt");
        private static readonly Dictionary<string, (string name, string urlTemplate)> _customEngines = 
            new(StringComparer.OrdinalIgnoreCase);

        // ────────────────────────────────────────────────────────────────────────
        // Constructor
        // ────────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            App.Log("=== MainWindow() start ===");
            InitializeComponent();
            App.Log("InitializeComponent() OK");

            _hookProc = HookCallback;
            InstallKeyboardHook();

            new WindowInteropHelper(this).EnsureHandle();
            App.Log("EnsureHandle() OK");
        }

        // ────────────────────────────────────────────────────────────────────────
        // Keyboard hook
        // ────────────────────────────────────────────────────────────────────────
        private void InstallKeyboardHook()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                var mod = proc.MainModule;
                if (mod == null) { App.Log("[WARN] MainModule null – hook skipped"); return; }

                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                    GetModuleHandle(mod.ModuleName), 0);

                if (_hookID == IntPtr.Zero)
                    App.Log($"[ERROR] Hook failed, Win32={Marshal.GetLastWin32Error()}");
                else
                    App.Log($"Keyboard hook OK 0x{_hookID:X}");
            }
            catch (Exception ex) { App.Log($"[ERROR] InstallKeyboardHook: {ex.Message}"); }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 &&
                (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_SPACE && (GetAsyncKeyState(0x12) & 0x8000) != 0)
                {
                    Task.Run(() => App.Log("Alt+Space → ToggleVisibility"));
                    Dispatcher.InvokeAsync(ToggleVisibility);
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Acrylic / Glass
        // ────────────────────────────────────────────────────────────────────────
        private void EnableGlassEffect()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            App.Log($"EnableGlassEffect  Win11={_isWin11}  HWND=0x{hwnd:X}");

            if (_isWin11)
            {
                // Windows 11: use DWMWA_SYSTEMBACKDROP_TYPE (acrylic transient)
                try
                {
                    uint val = DWMSBT_TRANSIENTWINDOW;
                    int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                        ref val, sizeof(uint));
                    App.Log(hr == 0
                        ? "DwmSetWindowAttribute acrylic OK"
                        : $"[WARN] DwmSetWindowAttribute hr=0x{hr:X}");
                }
                catch (Exception ex) { App.Log($"[WARN] DwmSetWindowAttribute: {ex.Message}"); }
            }

            // Also apply Win10-compatible acrylic via SetWindowCompositionAttribute
            // (works on Win10 and improves the look on Win11 as a complement)
            try
            {
                var policy = new AccentPolicy
                {
                    AccentState  = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags  = 0,
                    GradientColor = 0x00000000, // Fully transparent
                    AnimationId  = 0,
                };

                int size = Marshal.SizeOf(policy);
                IntPtr policyPtr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, policyPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute  = 19, // WCA_ACCENT_POLICY
                        Data       = policyPtr,
                        SizeOfData = size,
                    };
                    int result = SetWindowCompositionAttribute(hwnd, ref data);
                    App.Log(result != 0
                        ? "SetWindowCompositionAttribute acrylic OK"
                        : $"[WARN] SetWindowCompositionAttribute returned 0");
                }
                finally { Marshal.FreeHGlobal(policyPtr); }
            }
            catch (Exception ex) { App.Log($"[WARN] SetWindowCompositionAttribute: {ex.Message}"); }
        }

        // ────────────────────────────────────────────────────────────────────────
        // Window events
        // ────────────────────────────────────────────────────────────────────────
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            App.Log($"Window_SourceInitialized HWND=0x{new WindowInteropHelper(this).Handle:X}");
            // EnableGlassEffect();
            SetupTrayIcon();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            App.Log("Window_Loaded");
            _lastShown = DateTime.UtcNow;

            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            Left = (sw - ActualWidth) / 2;
            Top  = sh * 0.20;

            LoadThemePreference();
            LoadSearchPrefixes();

            SearchBox.Focus();
            Task.Run(BuildShortcutIndex);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Theme engine
        // ────────────────────────────────────────────────────────────────────────
        private void LoadThemePreference()
        {
            try
            {
                if (File.Exists(_themeFile))
                {
                    string pref = File.ReadAllText(_themeFile).Trim();
                    _isDarkTheme = string.Equals(pref, "dark", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { App.Log($"[WARN] LoadThemePreference: {ex.Message}"); }
            ApplyTheme(_isDarkTheme);
        }

        private static void LoadSearchPrefixes()
        {
            try
            {
                if (File.Exists(_prefixesFile))
                {
                    foreach (var line in File.ReadAllLines(_prefixesFile))
                    {
                        var parts = line.Split('|');
                        if (parts.Length == 3)
                        {
                            string prefix = parts[0].Trim();
                            _customEngines[prefix] = (parts[1].Trim(), parts[2].Trim());
                        }
                    }
                }
            }
            catch (Exception ex) { App.Log($"[WARN] LoadSearchPrefixes: {ex.Message}"); }
        }

        private void SaveThemePreference()
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                string tempFile = _themeFile + ".tmp";
                File.WriteAllText(tempFile, _isDarkTheme ? "dark" : "light");
                File.Move(tempFile, _themeFile, true);
            }
            catch (Exception ex) { App.Log($"[WARN] SaveThemePreference: {ex.Message}"); }
        }

        private void ApplyTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            var res = System.Windows.Application.Current.Resources;

            if (isDark)
            {
                // ── Dark: glass / onyx ────────────────────────────────────────────
                res["ThemePrimaryBackground"] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x80, 0x1C, 0x1C, 0x1E), 0.10),
                        new GradientStop(Color.FromArgb(0xA0, 0x00, 0x00, 0x00), 1.0),
                    },
                    new Point(0, 0), new Point(0, 1));

                res["ThemeBorderBrush"] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.4),
                        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
                    },
                    new Point(0, 0), new Point(1, 1));

                res["ThemeTextForeground"]        = new SolidColorBrush(Colors.White);
                res["ThemeCaretBrush"]            = new SolidColorBrush(Colors.White);
                res["ThemePlaceholderForeground"] = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x98));
                res["ThemeItemSelectedBackground"]    = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                res["ThemeItemSelectedForeground"]    = new SolidColorBrush(Colors.White);
                res["ThemeItemHoverBackground"]       = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                res["ThemeItemSelectedHoverBackground"] = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                // ── Light: frosted / ivory ────────────────────────────────────────
                res["ThemePrimaryBackground"] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0xC4, 0xFF, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0xBA, 0xF5, 0xF5, 0xF7), 0.20),
                        new GradientStop(Color.FromArgb(0xAD, 0xE8, 0xE8, 0xEE), 1.0),
                    },
                    new Point(0, 0), new Point(0, 1));

                res["ThemeBorderBrush"] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0xA0, 0xC0, 0xC0, 0xD0), 0.0),
                        new GradientStop(Color.FromArgb(0x50, 0xB0, 0xB0, 0xC8), 0.4),
                        new GradientStop(Color.FromArgb(0x20, 0xA0, 0xA0, 0xC0), 1.0),
                    },
                    new Point(0, 0), new Point(1, 1));

                res["ThemeTextForeground"]        = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
                res["ThemeCaretBrush"]            = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
                res["ThemePlaceholderForeground"] = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                res["ThemeItemSelectedBackground"]    = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
                res["ThemeItemSelectedForeground"]    = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
                res["ThemeItemHoverBackground"]       = new SolidColorBrush(Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
                res["ThemeItemSelectedHoverBackground"] = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00));
            }

            // Update the tray icon label so users know current state
            UpdateThemeTrayLabel();
            App.Log($"ApplyTheme: {(isDark ? "Dark" : "Light")}");
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            App.Log($"Window_StateChanged → {WindowState}");
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                HideWindow();
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            App.Log("Window_Deactivated (no auto-hide)");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            App.Log("Window_Closed – releasing resources");
            try { if (_hookID != IntPtr.Zero) UnhookWindowsHookEx(_hookID); } catch { }
            try { _trayIcon?.Dispose(); } catch { }
            App.Log("=== FastBar closed ===");
        }

        // ────────────────────────────────────────────────────────────────────────
        // Tray icon
        // ────────────────────────────────────────────────────────────────────────
        private void SetupTrayIcon()
        {
            App.Log("SetupTrayIcon…");
            try
            {
                var strip = new System.Windows.Forms.ContextMenuStrip();
                strip.Items.Add("Open / Close FastBar", null, (_, __) =>
                {
                    App.Log("Tray: toggle");
                    Dispatcher.Invoke(ToggleVisibility);
                });
                strip.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                // Theme toggle — label is updated by UpdateThemeTrayLabel()
                var themeItem = new System.Windows.Forms.ToolStripMenuItem("☀ Switch to Light Theme");
                themeItem.Click += (_, __) =>
                {
                    App.Log("Tray: toggle theme");
                    Dispatcher.Invoke(() =>
                    {
                        ApplyTheme(!_isDarkTheme);
                        SaveThemePreference();
                    });
                };
                strip.Items.Add(themeItem);
                strip.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                // Auto-Start toggle
                var autoStartItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows");
                autoStartItem.Checked = IsAutoStartEnabled();
                autoStartItem.Click += (_, __) =>
                {
                    bool enable = !IsAutoStartEnabled();
                    SetAutoStart(enable);
                    autoStartItem.Checked = enable;
                };
                strip.Items.Add(autoStartItem);
                strip.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                strip.Items.Add("Exit", null, (_, __) =>
                {
                    App.Log("Tray: exit");
                    _trayIcon?.Dispose();
                    System.Windows.Application.Current.Shutdown();
                });

                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Text             = "FastBar  —  Alt+Space to toggle",
                    Visible          = true,
                    ContextMenuStrip = strip,
                    Icon             = TryLoadIcon() ?? System.Drawing.SystemIcons.Application,
                };
                _trayIcon.DoubleClick += (_, __) => Dispatcher.Invoke(ToggleVisibility);
                App.Log("Tray icon ready");
            }
            catch (Exception ex) { App.Log($"[ERROR] SetupTrayIcon: {ex}"); }
        }

        private void UpdateThemeTrayLabel()
        {
            if (_trayIcon?.ContextMenuStrip == null) return;
            foreach (System.Windows.Forms.ToolStripItem item in _trayIcon.ContextMenuStrip.Items)
            {
                if (item is System.Windows.Forms.ToolStripMenuItem mi &&
                    (mi.Text?.StartsWith("☀") == true || mi.Text?.StartsWith("🌑") == true))
                {
                    mi.Text = _isDarkTheme ? "☀ Switch to Light Theme" : "🌑 Switch to Dark Theme";
                    break;
                }
            }
        }

        private static System.Drawing.Icon? TryLoadIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }
            try
            {
                string ico = Path.Combine(AppContext.BaseDirectory, "FastbarLogo.ico");
                if (File.Exists(ico)) return new System.Drawing.Icon(ico);
            }
            catch { }
            return null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Show / Hide
        // ────────────────────────────────────────────────────────────────────────
        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible) HideWindow();
            else ShowWindow();
        }

        private void ShowWindow()
        {
            _lastShown = DateTime.UtcNow;

            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            Left = (sw - ActualWidth) / 2;
            Top  = sh * 0.20;   // ~20% from top

            Visibility = Visibility.Visible;
            Activate();
            Keyboard.Focus(SearchBox);
            SearchBox.Focus();
            App.Log($"ShowWindow  left={Left:F0} top={Top:F0}  screen={sw}x{sh}");
        }

        private void HideWindow([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            App.Log($"HideWindow ← '{caller}'");
            Visibility = Visibility.Hidden;
            SearchBox.Clear();
            ResultList.ItemsSource = null;
            ResultList.Visibility  = Visibility.Collapsed;

            // Aggressive garbage collection to free up memory when hidden in the background
            Task.Delay(500).ContinueWith(_ =>
            {
                try
                {
                    bool isVisible = false;
                    Dispatcher.Invoke(() => { isVisible = (Visibility == Visibility.Visible); });

                    if (!isVisible)
                    {
                        App.Log("Aggressive GC (background optimization)");
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch { /* Window may have been closed – ignore */ }
            });
        }

        // ────────────────────────────────────────────────────────────────────────
        // Shortcut index
        // ────────────────────────────────────────────────────────────────────────
        private void BuildShortcutIndex()
        {
            var sw = Stopwatch.StartNew();
            int total = 0;
            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.lnk",
                                                               SearchOption.AllDirectories))
                    {
                        _shortcuts[Path.GetFileNameWithoutExtension(f)] = f;
                        total++;
                    }
                }
                catch (Exception ex) { App.Log($"[WARN] scan '{root}': {ex.Message}"); }
            }
            sw.Stop();
            App.Log($"BuildShortcutIndex: {total} shortcuts in {sw.ElapsedMilliseconds} ms");
        }

        // ────────────────────────────────────────────────────────────────────────
        // File-content search
        // ────────────────────────────────────────────────────────────────────────

        private static readonly int _fileResultCap = 5;

        /// <summary>
        /// Searches file names and text contents using Windows Search Index (OleDb).
        /// Returns at most <see cref="_fileResultCap"/> items prefixed with "📄 ".
        /// </summary>
        private static async Task<List<string>> SearchFilesAsync(string query,
            System.Threading.CancellationToken ct)
        {
            var results = new List<string>();

            await Task.Run(() =>
            {
                try
                {
                    using var connection = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                    connection.Open();

                    string safeQuery = query.Replace("'", "''");

                    string sql = $"SELECT TOP {_fileResultCap} System.ItemPathDisplay FROM SystemIndex " +
                                 $"WHERE System.FileName LIKE '%{safeQuery}%' OR FREETEXT('{safeQuery}') " +
                                 $"ORDER BY System.Search.Rank DESC";

                    using var command = new System.Data.OleDb.OleDbCommand(sql, connection);
                    using var ctRegistration = ct.Register(() => { try { command.Cancel(); } catch {} });
                    using var reader = command.ExecuteReader();

                    while (reader != null && reader.Read() && !ct.IsCancellationRequested)
                    {
                        var path = reader.GetString(0);
                        if (!string.IsNullOrEmpty(path))
                        {
                            results.Add($"📄 {path}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[WARN] OleDb Windows Search failed: {ex.Message}");
                }
            }, ct);

            return results;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Search
        // ────────────────────────────────────────────────────────────────────────
        private System.Threading.CancellationTokenSource? _fileCts;

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                CancelFileSearch();
                ResultList.ItemsSource = null;
                ResultList.Visibility  = Visibility.Collapsed;
                return;
            }

            var items = new List<string>();

            // ── 1. Arithmetic result (highest priority) ──────────────────────────
            if (TryEvaluateMath(query, out string mathResult))
                items.Add($"= {mathResult}");

            // ── 1.5 System commands ─────────────────────────────────────────────
            var lowerQuery = query.ToLowerInvariant();
            if (lowerQuery == "shutdown" || lowerQuery == "restart" || lowerQuery == "sleep" || lowerQuery == "lock")
                items.Add($"💻 System {char.ToUpper(lowerQuery[0])}{lowerQuery.Substring(1)}");

            // ── 1.6 Unit Conversion ─────────────────────────────────────────────
            if (TryEvaluateUnitConversion(query, out string conversionResult))
                items.Add($"🔄 {conversionResult}");

            // ── 2. Check for Search Engine Prefix ────────────────────────────────
            string shortcutQuery = StripSearchPrefix(query, out string? engine);

            if (engine != null)
            {
                items.Add(BuildWebFallbackLabel(query));
                
                ResultList.ItemsSource = items;
                ResultList.Visibility  = Visibility.Visible;
                if (ResultList.Items.Count > 0)
                    ResultList.SelectedIndex = 0;

                CancelFileSearch();
                _fileCts = new System.Threading.CancellationTokenSource();
                var cts = _fileCts;

                try
                {
                    // Small debounce
                    await Task.Delay(250, cts.Token);
                    
                    var suggestions = await FetchSearchSuggestionsAsync(shortcutQuery, cts.Token);
                    if (!cts.IsCancellationRequested && suggestions.Count > 0)
                    {
                        var current = (ResultList.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
                        int fallbackIndex = current.FindIndex(x => x.StartsWith("Search ") && x.Contains(" for '"));
                        if (fallbackIndex >= 0)
                            current.InsertRange(fallbackIndex, suggestions);
                        else
                            current.AddRange(suggestions);

                        ResultList.ItemsSource = current.Distinct().ToList();
                        ResultList.Visibility  = Visibility.Visible;
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex) { App.Log($"[WARN] web-suggestions: {ex.Message}"); }
                
                return;
            }

            // ── 3. App shortcuts ─────────────────────────────────────────────────
            var matches = _shortcuts.Keys
                .Where(k  => k.Contains(shortcutQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.StartsWith(shortcutQuery, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(k  => k)
                .Take(items.Count > 0 ? 6 : 7)
                .ToList();

            items.AddRange(matches);

            // ── 4. Web fallback (always shown at the bottom so the user can
            //       search the web even when app shortcuts matched)
            items.Add(BuildWebFallbackLabel(query));

            ResultList.ItemsSource = items;
            ResultList.Visibility  = Visibility.Visible;
            if (ResultList.Items.Count > 0)
                ResultList.SelectedIndex = 0;

            // ── 5. File search & suggestions (async, updates list when done) 
            CancelFileSearch();
            _fileCts = new System.Threading.CancellationTokenSource();
            var ctsMain = _fileCts;

            try
            {
                // Debounce to reduce disk I/O and network requests while typing
                await Task.Delay(350, ctsMain.Token);
                var fileResults = await SearchFilesAsync(query, ctsMain.Token);

                if (ctsMain.IsCancellationRequested) return;

                var current = (ResultList.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
                
                if (fileResults.Count > 0)
                {
                    foreach (var fr in fileResults)
                        if (!current.Contains(fr))
                            current.Add(fr);
                }

                // ── Web Search Suggestions ───────────────────────────────────────
                var suggestions = await FetchSearchSuggestionsAsync(shortcutQuery, ctsMain.Token);
                if (!ctsMain.IsCancellationRequested && suggestions.Count > 0)
                {
                    // Insert before the generic fallback
                    int fallbackIndex = current.FindIndex(x => x.StartsWith("Search ") && x.Contains(" for '"));
                    if (fallbackIndex >= 0)
                        current.InsertRange(fallbackIndex, suggestions);
                    else
                        current.AddRange(suggestions);
                }

                // ── Weather Info ─────────────────────────────────────────────────
                if (lowerQuery.StartsWith("weather ") || lowerQuery.StartsWith("wx "))
                {
                    string loc = lowerQuery.Substring(lowerQuery.IndexOf(' ') + 1).Trim();
                    if (!string.IsNullOrEmpty(loc))
                    {
                        string wx = await FetchWeatherAsync(loc, ctsMain.Token);
                        if (!string.IsNullOrEmpty(wx) && !ctsMain.IsCancellationRequested)
                        {
                            current.Insert(0, $"☁ {wx}");
                        }
                    }
                }

                if (!ctsMain.IsCancellationRequested)
                {
                    ResultList.ItemsSource = current.Distinct().ToList();
                    ResultList.Visibility  = Visibility.Visible;
                }
            }
            catch (TaskCanceledException) { /* query changed – silent */ }
            catch (Exception ex) { App.Log($"[WARN] file-search/suggestions: {ex.Message}"); }
        }

        /// <summary>Returns a human-friendly label for the last result item.</summary>
        private static string BuildWebFallbackLabel(string query)
        {
            string stripped = StripSearchPrefix(query, out string? engine);
            if (engine != null && _customEngines.TryGetValue(engine, out var custom))
                return $"Search {custom.name} for '{stripped}'";

            return engine switch
            {
                "y"  => $"Search Yandex for '{stripped}'",
                "tb" => $"Search TPB for '{stripped}'",
                "g"  => $"Search Google for '{stripped}'",
                "w"  => $"Search Winget for '{stripped}'",
                "sp" => $"Search Startpage for '{stripped}'",
                "b"  => $"Search Bing for '{stripped}'",
                "bs" => $"Search Brave Search for '{stripped}'",
                "p"  => $"Search Pinterest for '{stripped}'",
                _    => $"Search web for '{query}'",
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        // Keyboard / Mouse
        // ────────────────────────────────────────────────────────────────────────
        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (ResultList.SelectedIndex < ResultList.Items.Count - 1)
                        ResultList.SelectedIndex++;
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (ResultList.SelectedIndex > 0)
                        ResultList.SelectedIndex--;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ExecuteSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    HideWindow();
                    e.Handled = true;
                    break;
            }
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultList.SelectedItem != null && ResultList.IsVisible)
                ResultList.ScrollIntoView(ResultList.SelectedItem);
        }

        private void ResultList_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e) => ExecuteSelection();

        private void Grid_MouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        // Launch
        // ────────────────────────────────────────────────────────────────────────
        private void ExecuteSelection()
        {
            string query    = SearchBox.Text.Trim();
            string selected = (ResultList.SelectedItem as string) ?? "";
            App.Log($"ExecuteSelection  query='{query}'  selected='{selected}'");

            try
            {
                // ── Math result copy or use ──────────────────────────────────────
                if (selected.StartsWith("= "))
                {
                    // Nothing to launch — it's a calc answer; just display & close
                    App.Log($"  Math result: {selected}");
                    HideWindow();
                    return;
                }

                // ── System commands ─────────────────────────────────────────────
                if (selected.StartsWith("💻 System "))
                {
                    ExecuteSystemCommand(selected.Substring(10).ToLowerInvariant());
                    HideWindow();
                    return;
                }

                // ── Unit Conversion ─────────────────────────────────────────────
                if (selected.StartsWith("🔄 "))
                {
                    // Just display result, exit without launching.
                    HideWindow();
                    return;
                }

                // ── File-search result ────────────────────────────────────────────
                if (selected.StartsWith("📄 "))
                {
                    string filePath = selected[3..].Trim();
                    bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                    if (ctrl) Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    else if (shift) Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true, Verb = "runas" });
                    else Launch(filePath);
                    
                    HideWindow();
                    return;
                }

                string strippedQuery = StripSearchPrefix(query, out string? engine);

                // ── Web Search Suggestion (priority: suggestion text beats original query) ──
                if (selected.StartsWith("🔍 "))
                {
                    string q = selected[3..];
                    // Respect the active engine prefix; fall back to DuckDuckGo if none
                    if (engine != null && _customEngines.TryGetValue(engine, out var customSug))
                        Launch(customSug.urlTemplate.Replace("{query}", Uri.EscapeDataString(q)));
                    else if (engine == "y")
                        Launch($"https://yandex.com/search/?text={Uri.EscapeDataString(q)}");
                    else if (engine == "tb")
                        Launch($"https://tpb.party/search/{Uri.EscapeDataString(q)}/1/99/0");
                    else if (engine == "g")
                        Launch($"https://www.google.com/search?q={Uri.EscapeDataString(q)}");
                    else if (engine == "w")
                        Launch($"https://winget.ragerworks.com/search/all/{Uri.EscapeDataString(q)}/?limit=50");
                    else if (engine == "sp")
                        Launch($"https://www.startpage.com/search?q={Uri.EscapeDataString(q)}");
                    else if (engine == "b")
                        Launch($"https://www.bing.com/search?q={Uri.EscapeDataString(q)}");
                    else if (engine == "bs")
                        Launch($"https://search.brave.com/search?q={Uri.EscapeDataString(q)}");
                    else if (engine == "p")
                        Launch($"https://www.pinterest.com/search/pins/?q={Uri.EscapeDataString(q)}");
                    else
                        Launch($"https://duckduckgo.com/?q={Uri.EscapeDataString(q)}");
                    HideWindow();
                    return;
                }
                // ── Search Custom Engine ──────────────────────────────────────
                else if (engine != null && _customEngines.TryGetValue(engine, out var custom) && strippedQuery.Length > 0)
                {
                    Launch(custom.urlTemplate.Replace("{query}", Uri.EscapeDataString(strippedQuery)));
                }
                // ── Search prefix: y  → Yandex ────────────────────────────────
                else if (engine == "y" && strippedQuery.Length > 0)
                {
                    Launch($"https://yandex.com/search/?text={Uri.EscapeDataString(strippedQuery)}");
                }
                // ── Search prefix: tb → TPB ───────────────────────────────────
                else if (engine == "tb" && strippedQuery.Length > 0)
                {
                    Launch($"https://tpb.party/search/{Uri.EscapeDataString(strippedQuery)}/1/99/0");
                }
                // ── Search prefix: g  → Google ────────────────────────────────
                else if (engine == "g" && strippedQuery.Length > 0)
                {
                    Launch($"https://www.google.com/search?q={Uri.EscapeDataString(strippedQuery)}");
                }
                // ── Search prefix: w  → Winget ────────────────────────────────
                else if (engine == "w" && strippedQuery.Length > 0)
                {
                    Launch($"https://winget.ragerworks.com/search/all/{Uri.EscapeDataString(strippedQuery)}/?limit=50");
                }
                // ── Search prefix: sp → Startpage ─────────────────────────────
                else if (engine == "sp" && strippedQuery.Length > 0)
                {
                    Launch($"https://www.startpage.com/search?q={Uri.EscapeDataString(strippedQuery)}");
                }
                // ── Search prefix: b  → Bing ───────────────────────────────────
                else if (engine == "b" && strippedQuery.Length > 0)
                {
                    Launch($"https://www.bing.com/search?q={Uri.EscapeDataString(strippedQuery)}");
                }
                // ── Search prefix: bs → Brave Search ──────────────────────────
                else if (engine == "bs" && strippedQuery.Length > 0)
                {
                    Launch($"https://search.brave.com/search?q={Uri.EscapeDataString(strippedQuery)}");
                }
                // ── Search prefix: p  → Pinterest ─────────────────────────────
                else if (engine == "p" && strippedQuery.Length > 0)
                {
                    Launch($"https://www.pinterest.com/search/pins/?q={Uri.EscapeDataString(strippedQuery)}");
                }

                // ── Exact shortcut match on what the user typed ───────────────
                else if (_shortcuts.TryGetValue(query, out string? p1))
                {
                    Launch(p1);
                }
                // ── Web fallback labels ───────────────────────────────────────
                else if (selected.StartsWith("Search ") && selected.Contains(" for '"))
                {
                    int forIdx = selected.IndexOf(" for '");
                    string name = selected.Substring(7, forIdx - 7);
                    string q = selected.Substring(forIdx + 6, selected.Length - forIdx - 7);

                    if (name == "Yandex") Launch($"https://yandex.com/search/?text={Uri.EscapeDataString(q)}");
                    else if (name == "TPB") Launch($"https://tpb.party/search/{Uri.EscapeDataString(q)}/1/99/0");
                    else if (name == "Google") Launch($"https://www.google.com/search?q={Uri.EscapeDataString(q)}");
                    else if (name == "Winget") Launch($"https://winget.ragerworks.com/search/all/{Uri.EscapeDataString(q)}/?limit=50");
                    else if (name == "Startpage") Launch($"https://www.startpage.com/search?q={Uri.EscapeDataString(q)}");
                    else if (name == "Bing") Launch($"https://www.bing.com/search?q={Uri.EscapeDataString(q)}");
                    else if (name == "Brave Search") Launch($"https://search.brave.com/search?q={Uri.EscapeDataString(q)}");
                    else if (name == "Pinterest") Launch($"https://www.pinterest.com/search/pins/?q={Uri.EscapeDataString(q)}");
                    else if (name == "web") Launch($"https://duckduckgo.com/?q={Uri.EscapeDataString(q)}");
                    else
                    {
                        var match = _customEngines.Values.FirstOrDefault(v => v.name == name);
                        if (!string.IsNullOrEmpty(match.urlTemplate))
                            Launch(match.urlTemplate.Replace("{query}", Uri.EscapeDataString(q)));
                    }
                }
                // ── App from list ─────────────────────────────────────────────
                else if (!string.IsNullOrEmpty(selected) &&
                         _shortcuts.TryGetValue(selected, out string? p2))
                {
                    bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                    if (ctrl) Process.Start("explorer.exe", $"/select,\"{p2}\"");
                    else if (shift) Process.Start(new ProcessStartInfo(p2) { UseShellExecute = true, Verb = "runas" });
                    else Launch(p2);
                }
                // ── Weather Result ──────────────────────────────────────────────
                else if (selected.StartsWith("☁ "))
                {
                    // Just display weather info, no URL to open.
                    HideWindow();
                    return;
                }
                // ── Direct command / URL ──────────────────────────────────────
                else if (!string.IsNullOrEmpty(query))
                {
                    Launch(query);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ERROR] ExecuteSelection: {ex.Message}");
            }

            HideWindow();
        }

        private static void Launch(string target)
        {
            try
            {
                App.Log($"  Launch: {target}");
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { App.Log($"  [ERROR] Launch '{target}': {ex.Message}"); }
        }

        private static void ExecuteSystemCommand(string cmd)
        {
            try
            {
                App.Log($"ExecuteSystemCommand: {cmd}");
                switch (cmd)
                {
                    case "shutdown": Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                    case "restart": Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                    case "sleep": Process.Start(new ProcessStartInfo("rundll32", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true, UseShellExecute = false }); break;
                    case "lock": Process.Start(new ProcessStartInfo("rundll32", "user32.dll,LockWorkStation") { CreateNoWindow = true, UseShellExecute = false }); break;
                }
            }
            catch (Exception ex) { App.Log($"[ERROR] System Command failed: {ex.Message}"); }
        }

        // ────────────────────────────────────────────────────────────────────────
        // Search prefix helpers
        // ────────────────────────────────────────────────────────────────────────
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private void CancelFileSearch()
        {
            _fileCts?.Cancel();
            _fileCts?.Dispose();
            _fileCts = null;
        }

        private static async Task<List<string>> FetchSearchSuggestionsAsync(string query, System.Threading.CancellationToken ct)
        {
            var suggestions = new List<string>();
            try
            {
                // DuckDuckGo autocomplete API
                string url = $"https://duckduckgo.com/ac/?q={Uri.EscapeDataString(query)}";
                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return suggestions;
                
                string json = await response.Content.ReadAsStringAsync(ct);
                // DuckDuckGo returns [{"phrase":"foo"}, {"phrase":"bar"}]
                var array = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                if (array != null)
                {
                    foreach (var dict in array)
                    {
                        if (dict.TryGetValue("phrase", out string? phrase) && !string.IsNullOrEmpty(phrase))
                            suggestions.Add($"🔍 {phrase}");
                    }
                }
            }
            catch { }
            return suggestions.Take(3).ToList();
        }

        private static async Task<string> FetchWeatherAsync(string loc, System.Threading.CancellationToken ct)
        {
            try
            {
                // format=3 gives a nice short "Location: Condition +Temp"
                string url = $"https://wttr.in/{Uri.EscapeDataString(loc)}?format=3";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    string text = await response.Content.ReadAsStringAsync(ct);
                    return text.Trim();
                }
            }
            catch { }
            return "";
        }

        private static bool TryEvaluateUnitConversion(string input, out string result)
        {
            result = "";
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"^\s*([\d\.]+)\s*([a-zA-Z]+)\s+(?:to|in)\s+([a-zA-Z]+)\s*$");
                if (!match.Success) return false;

                if (!double.TryParse(match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double amount)) return false;
                string fromUnit = match.Groups[2].Value.ToLowerInvariant();
                string toUnit = match.Groups[3].Value.ToLowerInvariant();

                // Distance: base is meters
                var dist = new Dictionary<string, double> {
                    { "m", 1 }, { "km", 1000 }, { "cm", 0.01 }, { "mm", 0.001 },
                    { "mi", 1609.34 }, { "yd", 0.9144 }, { "ft", 0.3048 }, { "in", 0.0254 }
                };
                
                // Weight: base is kg
                var weight = new Dictionary<string, double> {
                    { "kg", 1 }, { "g", 0.001 }, { "mg", 0.000001 },
                    { "lb", 0.453592 }, { "lbs", 0.453592 }, { "oz", 0.0283495 }
                };

                if (dist.ContainsKey(fromUnit) && dist.ContainsKey(toUnit))
                {
                    double inMeters = amount * dist[fromUnit];
                    double converted = inMeters / dist[toUnit];
                    result = $"{amount} {fromUnit} = {converted:0.####} {toUnit}";
                    return true;
                }
                
                if (weight.ContainsKey(fromUnit) && weight.ContainsKey(toUnit))
                {
                    double inKg = amount * weight[fromUnit];
                    double converted = inKg / weight[toUnit];
                    result = $"{amount} {fromUnit} = {converted:0.####} {toUnit}";
                    return true;
                }

                // Temperature
                if ((fromUnit == "c" || fromUnit == "f") && (toUnit == "c" || toUnit == "f"))
                {
                    if (fromUnit == "c" && toUnit == "f") result = $"{amount} °C = {(amount * 9 / 5 + 32):0.##} °F";
                    else if (fromUnit == "f" && toUnit == "c") result = $"{amount} °F = {((amount - 32) * 5 / 9):0.##} °C";
                    else result = $"{amount} °{fromUnit.ToUpper()} = {amount} °{toUnit.ToUpper()}";
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string StripSearchPrefix(string query, out string? engine)
        {
            foreach (var prefix in _customEngines.Keys)
            {
                if (query.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
                    query.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                {
                    engine = prefix;
                    return query.Substring(prefix.Length + 1).Trim();
                }
            }

            if (query.StartsWith("y ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("y.", StringComparison.OrdinalIgnoreCase))
            { engine = "y";  return query[2..].Trim(); }
            if (query.StartsWith("tb ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("tb.", StringComparison.OrdinalIgnoreCase))
            { engine = "tb"; return query[3..].Trim(); }
            if (query.StartsWith("g ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("g.", StringComparison.OrdinalIgnoreCase))
            { engine = "g";  return query[2..].Trim(); }
            if (query.StartsWith("w ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("w.", StringComparison.OrdinalIgnoreCase))
            { engine = "w";  return query[2..].Trim(); }
            if (query.StartsWith("bs ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("bs.", StringComparison.OrdinalIgnoreCase))
            { engine = "bs"; return query[3..].Trim(); }
            if (query.StartsWith("sp ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("sp.", StringComparison.OrdinalIgnoreCase))
            { engine = "sp"; return query[3..].Trim(); }
            if (query.StartsWith("b ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("b.", StringComparison.OrdinalIgnoreCase))
            { engine = "b";  return query[2..].Trim(); }
            if (query.StartsWith("p ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("p.", StringComparison.OrdinalIgnoreCase))
            { engine = "p";  return query[2..].Trim(); }

            engine = null;
            return query;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Arithmetic evaluator  (supports + - * / ^ and parentheses)
        // Recursive-descent parser, no external libraries.
        // ────────────────────────────────────────────────────────────────────────
        private static bool TryEvaluateMath(string input, out string result)
        {
            result = "";
            // Only attempt if the string looks like a math expression
            if (!System.Text.RegularExpressions.Regex.IsMatch(input, @"^[\d\s\+\-\*\/\^\(\)\.]+$"))
                return false;
            // Must contain at least one operator to avoid launching numbers as commands
            if (!System.Text.RegularExpressions.Regex.IsMatch(input, @"[\+\-\*\/\^]"))
                return false;

            try
            {
                double val = new MathParser(input).Parse();
                if (double.IsNaN(val) || double.IsInfinity(val)) return false;
                // Format: show integer if result is whole and fits in a long,
                // otherwise use G10 to avoid scientific notation for typical values
                result = val == Math.Truncate(val) && val >= long.MinValue && val <= long.MaxValue
                    ? ((long)val).ToString()
                    : val.ToString("G10");
                return true;
            }
            catch { return false; }
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Simple recursive-descent math parser
    // Grammar:
    //   expr   = term   (('+' | '-') term)*
    //   term   = factor (('*' | '/') factor)*
    //   factor = base ('^' factor)?          (right-associative)
    //   base   = '-' base | '(' expr ')' | number
    // ────────────────────────────────────────────────────────────────────────────
    internal sealed class MathParser
    {
        private readonly string _input;
        private int _pos;

        public MathParser(string input) => _input = input.Replace(" ", "");

        public double Parse()
        {
            double result = ParseExpr();
            if (_pos != _input.Length)
                throw new FormatException($"Unexpected char at {_pos}: '{_input[_pos]}'");
            return result;
        }

        private double ParseExpr()
        {
            double left = ParseTerm();
            while (_pos < _input.Length && (_input[_pos] == '+' || _input[_pos] == '-'))
            {
                char op = _input[_pos++];
                double right = ParseTerm();
                left = op == '+' ? left + right : left - right;
            }
            return left;
        }

        private double ParseTerm()
        {
            double left = ParseFactor();
            while (_pos < _input.Length && (_input[_pos] == '*' || _input[_pos] == '/'))
            {
                char op = _input[_pos++];
                double right = ParseFactor();
                if (op == '/' && right == 0) throw new DivideByZeroException();
                left = op == '*' ? left * right : left / right;
            }
            return left;
        }

        private double ParseFactor()
        {
            double b = ParseBase();
            if (_pos < _input.Length && _input[_pos] == '^')
            {
                _pos++;
                double exp = ParseFactor(); // right-associative
                return Math.Pow(b, exp);
            }
            return b;
        }

        private double ParseBase()
        {
            if (_pos < _input.Length && _input[_pos] == '-')
            {
                _pos++;
                return -ParseBase();
            }
            if (_pos < _input.Length && _input[_pos] == '(')
            {
                _pos++;
                double val = ParseExpr();
                if (_pos >= _input.Length || _input[_pos] != ')')
                    throw new FormatException("Missing closing parenthesis");
                _pos++;
                return val;
            }
            return ParseNumber();
        }

        private double ParseNumber()
        {
            int start = _pos;
            while (_pos < _input.Length &&
                   (char.IsDigit(_input[_pos]) || _input[_pos] == '.'))
            {
                _pos++;
            }
            if (_pos == start)
                throw new FormatException($"Expected number at position {_pos}");
            return double.Parse(_input[start.._pos],
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Startup Registry Configuration
    // ────────────────────────────────────────────────────────────────────────
    public partial class MainWindow
    {
        private static bool IsAutoStartEnabled()
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("FastBar") != null;
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                string exePath = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue("FastBar", exePath);
                        App.Log("Enabled Registry AutoStart");
                    }
                    else
                    {
                        key.DeleteValue("FastBar", false);
                        App.Log("Disabled Registry AutoStart");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] SetAutoStart: {ex.Message}");
            }
        }
    }
}