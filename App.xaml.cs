using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

// Suppress the WinForms ambiguity for 'Application' — this file only uses WPF.
// The partial class base is defined by App.xaml which sets it to System.Windows.Application.
namespace FastBar
{
    public partial class App : System.Windows.Application
    {
        private static readonly string LogFile =
            Path.Combine(AppContext.BaseDirectory, "fastbar_debug.log");

        internal static void Log(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(LogFile, line + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(line);
            }
            catch { }
        }

        private static System.Threading.Mutex? _mutex;
        private const string MutexName = "Global\\FastBar_SingleInstanceMutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            // Only allow 1 instance
            _mutex = new System.Threading.Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // App is already running in background. Exit gracefully.
                Environment.Exit(0);
                return;
            }

            Log("========================================");
            Log($"FastBar starting  |  .NET {Environment.Version}  |  OS {Environment.OSVersion}");
            Log($"BaseDirectory: {AppContext.BaseDirectory}");

            // Catch unhandled exceptions on the UI dispatcher thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Catch unhandled exceptions on background threads
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log($"[FATAL/UI] {e.Exception}");
            e.Handled = true; // keep the app alive so the log gets flushed
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log($"[FATAL/BG] IsTerminating={e.IsTerminating}  ExceptionObject={e.ExceptionObject}");
        }
    }
}
