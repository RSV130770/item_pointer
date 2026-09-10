using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LedMatrixControl
{
    internal static class UiProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => HandleUnexpectedError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleUnexpectedError(e.ExceptionObject as Exception);
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            var config = File.Exists("config.json")
                ? LedMatrixConfig.LoadFromFile("config.json")
                : new LedMatrixConfig();

            // Diagnostics run in the background so the login screen appears
            // immediately rather than the whole app looking frozen with no
            // window at all while ports are being probed. Individual probes
            // are already hard-timeout-bounded (see StartupDiagnostics), so
            // this can no longer hang indefinitely either way - this is
            // just about not blocking the very first thing the operator
            // sees.
            System.Threading.Tasks.Task.Run(() => RunStartupDiagnostics(config));

            Application.Run(new LoginForm(config));
        }

        /// <summary>
        /// Last line of defense: log anything unexpected instead of letting
        /// the touchscreen app crash out to the desktop mid-shift.
        /// </summary>
        private static void HandleUnexpectedError(Exception ex)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unexpected_errors.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* nowhere left to report this */ }
        }

        private static void RunStartupDiagnostics(LedMatrixConfig config)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_diagnostics.log");
            var logLines = new StringBuilder();
            logLines.AppendLine($"--- Startup check {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");

            var report = StartupDiagnostics.RunAll(config, line => logLines.AppendLine(line));

            try { File.AppendAllText(logPath, logLines.ToString()); }
            catch { /* best effort */ }

            if (report.ConfigWasModified)
            {
                try { config.SaveToFile("config.json"); }
                catch { /* if this fails, the in-memory config is still corrected for this run */ }
            }

            if (!report.AllOk)
            {
                MessageBox.Show(
                    report.Summary(),
                    "Startup check found problems",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
