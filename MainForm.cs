#pragma warning disable CS8602
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ClipTool;

public partial class MainForm : Form
{
    // === State ===
    private readonly List<ClipItem> _history = new();
    private string _lastClipText = "";
    private bool _isPasting;
    private bool _historyOpen;

    // === Components ===
    private NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _clipTimer;
    private readonly TesseractHelper _ocr;

    // === Hotkey display names ===
    private string _hotkeyHistoryName = "";
    private string _hotkeyCaptureName = "";
    private readonly ToolStripMenuItem _menuHistory;
    private readonly ToolStripMenuItem _menuCapture;
    private readonly ToolStripMenuItem _menuAutoStart;

    // === Constants ===
    private int _hotkeyIdHistory = 1;
    private int _hotkeyIdCapture = 2;
    private const int MaxHistory = 300;
    private static readonly uint _taskbarCreatedMsg =
        NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipTool");
    private static readonly string DataPath = Path.Combine(DataDir, "history.json");
    private static readonly string LogPath = Path.Combine(DataDir, "debug.log");
    private static readonly string AutoStartRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartRegName = "ClipTool";

    public MainForm()
    {
        _ocr = new TesseractHelper();
        Directory.CreateDirectory(DataDir);

        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;

        // Tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "ClipTool — Detecting hotkeys...",
            Visible = true,
        };

        var trayMenu = new ContextMenuStrip();
        _menuHistory = new ToolStripMenuItem("📋 Clipboard History", null, (_, _) => ShowHistory());
        _menuCapture = new ToolStripMenuItem("📷 Screenshot OCR", null, (_, _) => StartCapture());
        _menuAutoStart = new ToolStripMenuItem("🔁 Auto Start on Boot", null, (_, _) => ToggleAutoStart());
        trayMenu.Items.Add(_menuHistory);
        trayMenu.Items.Add(_menuCapture);
        trayMenu.Items.Add(_menuAutoStart);
        trayMenu.Items.Add("-");
        trayMenu.Items.Add("🧹 Clear History", null, (_, _) => ClearHistory());
        trayMenu.Items.Add("❌ Exit", null, (_, _) =>
        {
            Program.UserExited = true;
            _clipTimer?.Stop();
            SaveHistory();
            _trayIcon.Visible = false;
            Application.Exit();
        });
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => ShowHistory();

        // Clipboard polling
        _clipTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _clipTimer.Tick += CheckClipboard!;

        // Defer Init to run after constructor completes (safe WinForms pattern)
        if (!IsHandleCreated) CreateHandle();
        BeginInvoke(Init);
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (id == _hotkeyIdHistory) ShowHistory();
            else if (id == _hotkeyIdCapture) _ = StartCaptureAsync();
        }
        else if (m.Msg == _taskbarCreatedMsg)
        {
            RebuildTrayIcon();
        }
        base.WndProc(ref m);
    }

    private void RebuildTrayIcon()
    {
        Log("Explorer restart — rebuilding tray icon + hotkeys");

        // Hotkeys are lost after Explorer restart, re-register
        NativeMethods.UnregisterHotKey(Handle, _hotkeyIdHistory);
        NativeMethods.UnregisterHotKey(Handle, _hotkeyIdCapture);
        FindAndRegisterHotkeys();

        // NotifyIcon handle is invalid after Explorer restart, recreate
        var oldIcon = _trayIcon;
        var newIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = oldIcon.Text,
            ContextMenuStrip = oldIcon.ContextMenuStrip,
            Visible = true,
        };
        newIcon.DoubleClick += (_, _) => ShowHistory();
        _trayIcon = newIcon;
        oldIcon.Dispose();
    }

    // ==================== Initialization ====================

    private void Init()
    {
        FindAndRegisterHotkeys();
        LoadHistory();
        SyncAutoStartMenuState();
        HealAutoStartPath();
        _clipTimer.Start();
        _ = _ocr.DataReady.ContinueWith(t =>
        {
            if (t.IsFaulted) Log($"OCR download failed: {t.Exception?.Message}");
            else Log("OCR ready");
        });
        Log($"Initialization complete, Handle={Handle}");
    }

    // ==================== Hotkeys ====================

    private void FindAndRegisterHotkeys()
    {
        // Strategy: scan which Win+Shift+letter combos are available
        var avail = new List<(char ch, int vk)>();

        for (int vk = 0x41; vk <= 0x5A; vk++) // A-Z
        {
            NativeMethods.UnregisterHotKey(Handle, 99);
            if (NativeMethods.RegisterHotKey(Handle, 99,
                    NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)vk))
            {
                avail.Add(((char)vk, vk));
            }
            NativeMethods.UnregisterHotKey(Handle, 99);
        }

        Log($"Available Win+Shift+Letter: {new string(avail.Select(a => a.ch).ToArray())}");

        if (avail.Count >= 2)
        {
            NativeMethods.RegisterHotKey(Handle, _hotkeyIdHistory,
                NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)avail[0].vk);
            NativeMethods.RegisterHotKey(Handle, _hotkeyIdCapture,
                NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)avail[1].vk);
            _hotkeyHistoryName = $"Win+Shift+{avail[0].ch}";
            _hotkeyCaptureName = $"Win+Shift+{avail[1].ch}";
            UpdateTrayText();
            UpdateTrayMenu();
            _trayIcon.ShowBalloonTip(5000, "ClipTool Ready",
                $"History {_hotkeyHistoryName}  |  OCR {_hotkeyCaptureName}",
                ToolTipIcon.Info);
            Log($"Hotkeys: History={_hotkeyHistoryName}, OCR={_hotkeyCaptureName}");
            return;
        }

        // No letters available → fall back to F keys
        for (int vk = 0x70; vk <= 0x7A; vk++) // F1-F11
        {
            NativeMethods.UnregisterHotKey(Handle, 98);
            if (NativeMethods.RegisterHotKey(Handle, 98,
                    NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)vk))
            {
                NativeMethods.RegisterHotKey(Handle, _hotkeyIdHistory,
                    NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)vk);
                _hotkeyHistoryName = $"Win+Shift+F{vk - 0x70 + 1}";
                break;
            }
            NativeMethods.UnregisterHotKey(Handle, 98);
        }
        for (int vk = 0x70; vk <= 0x7A; vk++)
        {
            NativeMethods.UnregisterHotKey(Handle, 97);
            if (vk != GetLastRegisteredVk() &&
                NativeMethods.RegisterHotKey(Handle, 97,
                    NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)vk))
            {
                NativeMethods.RegisterHotKey(Handle, _hotkeyIdCapture,
                    NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)vk);
                _hotkeyCaptureName = $"Win+Shift+F{vk - 0x70 + 1}";
                UpdateTrayText();
                UpdateTrayMenu();
                Log($"Hotkeys: History={_hotkeyHistoryName}, OCR={_hotkeyCaptureName}");
                return;
            }
            NativeMethods.UnregisterHotKey(Handle, 97);
        }
        Log("WARNING: No available hotkey combinations found!");
    }

    private int GetLastRegisteredVk() => 0; // simplified

    private void UpdateTrayText()
    {
        if (!string.IsNullOrEmpty(_hotkeyHistoryName) && !string.IsNullOrEmpty(_hotkeyCaptureName))
        {
            _trayIcon.Text = $"ClipTool | History {_hotkeyHistoryName}  |  OCR {_hotkeyCaptureName}";
        }
    }

    private void UpdateTrayMenu()
    {
        var hist = !string.IsNullOrEmpty(_hotkeyHistoryName) ? $"  ({_hotkeyHistoryName})" : "";
        var ocr = !string.IsNullOrEmpty(_hotkeyCaptureName) ? $"  ({_hotkeyCaptureName})" : "";
        _menuHistory.Text = $"📋 Clipboard History{hist}";
        _menuCapture.Text = $"📷 Screenshot OCR{ocr}";
    }

    // ==================== Clipboard Monitor ====================

    private void CheckClipboard(object? sender, EventArgs e)
    {
        if (_isPasting) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (!string.IsNullOrEmpty(text) && text != _lastClipText)
                {
                    _lastClipText = text;
                    if (_history.Count > 0 && _history[0].Text == text)
                        return;

                    _history.Insert(0, new ClipItem
                    {
                        Text = text,
                        Time = DateTime.Now,
                        Type = ClipType.Text,
                    });
                    TrimHistory();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard error: {ex.Message}");
        }
    }

    // ==================== Clipboard History ====================

    public void ShowHistory()
    {
        if (_historyOpen)
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f is HistoryForm)
                {
                    NativeMethods.ShowWindow(f.Handle, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(f.Handle);
                    return;
                }
            }
        }

        _historyOpen = true;
        var form = new HistoryForm(_history);
        form.OnItemSelected += OnHistoryItemSelected;
        form.OnItemDeleted += OnHistoryItemDeleted;
        form.FormClosed += (_, _) => _historyOpen = false;
        form.Show();
        NativeMethods.SetForegroundWindow(form.Handle);
    }

    private void OnHistoryItemSelected(string text)
    {
        _isPasting = true;
        try
        {
            Clipboard.SetText(text);
            _lastClipText = text;
            Thread.Sleep(30);
            SendKeys.SendWait("^V");
        }
        catch { }

        Task.Delay(600).ContinueWith(_ => _isPasting = false);
    }

    private void OnHistoryItemDeleted()
    {
        SaveHistory();
    }

    private void ClearHistory()
    {
        var result = MessageBox.Show(
            "Clear all clipboard history?",
            "Clear History",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _history.Clear();
            _lastClipText = "";
            SaveHistory();
        }
    }

    // ==================== Screenshot OCR ====================

    private async Task StartCaptureAsync()
    {
        try
        {
            await _ocr.DataReady;

            // Verify OCR data files exist before showing capture UI
            if (!_ocr.HasRequiredData())
            {
                Log("OCR data missing — download failed");
                _trayIcon.ShowBalloonTip(5000, "OCR 不可用",
                    "语言数据下载失败，请检查网络连接后重试",
                    ToolTipIcon.Error);
                return;
            }

            var form = new CaptureForm(_ocr);
            var result = form.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrEmpty(form.OcrResult))
            {
                var ocrText = form.OcrResult;

                _isPasting = true;
                try
                {
                    Clipboard.SetText(ocrText);
                    _lastClipText = ocrText;
                }
                catch { }

                _history.Insert(0, new ClipItem
                {
                    Text = ocrText,
                    Time = DateTime.Now,
                    Type = ClipType.Ocr,
                });
                TrimHistory();

                _trayIcon.ShowBalloonTip(3000, "Screenshot OCR",
                    $"Recognized and copied ({ocrText.Length} chars)", ToolTipIcon.Info);

                await Task.Delay(200);
                try { SendKeys.SendWait("^V"); }
                catch { }

                await Task.Delay(600);
                _isPasting = false;
            }
        }
        catch (Exception ex)
        {
            Log($"Screenshot OCR crash: {ex}");
            _isPasting = false;
        }
    }

    // ==================== Persistence ====================

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save error: {ex.Message}");
        }
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                var json = File.ReadAllText(DataPath);
                var items = JsonSerializer.Deserialize<List<ClipItem>>(json);
                if (items != null)
                {
                    _history.Clear();
                    _history.AddRange(items);
                    if (_history.Count > 0)
                        _lastClipText = _history[0].Text;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Load error: {ex.Message}");
        }
    }

    private void TrimHistory()
    {
        if (_history.Count > MaxHistory)
            _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
    }

    private void StartCapture() => _ = StartCaptureAsync();

    // ==================== Auto Start on Boot ====================

    /// <summary>Sync menu checked state from registry</summary>
    private void SyncAutoStartMenuState()
    {
        _menuAutoStart.Checked = IsAutoStartEnabled();
    }

    /// <summary>Keep the auto-start entry pointing at the currently-running exe.
    /// Self-heals stale paths if the app was moved or rebuilt.</summary>
    private void HealAutoStartPath()
    {
        if (!IsAutoStartEnabled()) return;
        try
        {
            var current = $"\"{Application.ExecutablePath}\"";
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegPath, writable: true);
            if (key == null) return;
            if (key.GetValue(AutoStartRegName) as string == current) return;
            key.SetValue(AutoStartRegName, current);
            Log($"Auto-start path healed: {current}");
        }
        catch (Exception ex)
        {
            Log($"Failed to heal auto-start path: {ex.Message}");
        }
    }

    /// <summary>Check whether auto-start is currently registered</summary>
    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegPath);
            return key?.GetValue(AutoStartRegName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Toggle auto-start on/off via registry</summary>
    private void ToggleAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegPath, writable: true);
            if (key == null) return;

            if (IsAutoStartEnabled())
            {
                key.DeleteValue(AutoStartRegName, throwOnMissingValue: false);
                Log("Auto start disabled");
            }
            else
            {
                var exePath = Application.ExecutablePath;
                key.SetValue(AutoStartRegName, $"\"{exePath}\"");
                Log($"Auto start enabled: {exePath}");
            }

            _menuAutoStart.Checked = IsAutoStartEnabled();
        }
        catch (Exception ex)
        {
            Log($"Failed to set auto start: {ex.Message}");
        }
    }

    // ==================== Logging ====================

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Debug.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); }
        catch { }
    }
}
