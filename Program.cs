using System.Diagnostics;

namespace ClipTool;

static class Program
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipTool", "crash.log");

    /// <summary>Set to true when user explicitly clicks Exit — suppresses auto-restart.</summary>
    internal static bool UserExited;

    [STAThread]
    static void Main()
    {
        // 全局未捕获异常处理
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            LogCrash("ThreadException", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain", e.ExceptionObject as Exception);
        };

        ApplicationConfiguration.Initialize();

        // Watchdog loop: auto-restart unless user clicked Exit
        while (true)
        {
            UserExited = false;
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogCrash("UnhandledLoop", ex);
            }

            if (UserExited) break;

            // Brief delay before restart
            try { Thread.Sleep(3000); } catch { }
            Debug.WriteLine("[Watchdog] Restarting ClipTool...");
        }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}";
        Debug.WriteLine(line);
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(CrashLogPath, line + Environment.NewLine);

            // 通知用户
            MessageBox.Show(
                $"ClipTool crashed and is restarting.\n\n" +
                $"Details written to:\n{CrashLogPath}\n\n" +
                $"Source: {source}\nError: {ex?.Message}",
                "ClipTool Crash",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }

        // 自动重启
        try
        {
            Process.Start(Application.ExecutablePath);
        }
        catch { }
    }
}
