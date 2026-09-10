using System;
using System.IO;
using System.Text;

namespace LedMatrixControl
{
    /// <summary>
    /// Appends one human-readable line per operator action to a plain text
    /// log. Always writes to a local file next to the executable first
    /// (never blocked by network issues), then makes a best-effort attempt
    /// to mirror the same line to the network log folder. A network
    /// failure never loses the local record.
    /// </summary>
    public class OperationsLog
    {
        private readonly string _localPath;
        private readonly string _networkFolder;
        private readonly object _lock = new();
        private string _lastNetworkError;

        /// <summary>
        /// Fires when the network mirror fails, or when it recovers (with
        /// null) after a failure. Callers should log this - previously the
        /// failure reason was silently swallowed, which made "logs aren't
        /// reaching a folder that looks reachable" impossible to diagnose.
        /// </summary>
        public event Action<string> NetworkWriteFailed;

        public OperationsLog(string localFolder, string networkFolder)
        {
            var fileName = $"operations_log_{DateTime.Now:yyyyMMdd}.txt";
            _localPath = Path.Combine(localFolder, fileName);
            _networkFolder = networkFolder;
        }

        public void Log(string userId, string userName, string action, string details)
        {
            var line = BuildLine(userId, userName, action, details);

            lock (_lock)
            {
                AppendLocal(line);
                TryAppendNetwork(line);
            }
        }

        private static string BuildLine(string userId, string userName, string action, string details)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  {userName} ({userId})  |  {action}";
            if (!string.IsNullOrWhiteSpace(details))
                line += $"  |  {details}";
            return line;
        }

        private void AppendLocal(string line)
        {
            try
            {
                File.AppendAllText(_localPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // If even the local log can't be written, there's nowhere
                // safe left to report it - swallow rather than crash the
                // operator's workflow.
            }
        }

        private void TryAppendNetwork(string line)
        {
            string error = null;
            try
            {
                if (!Directory.Exists(_networkFolder))
                {
                    error = $"Network log folder not reachable: {_networkFolder}";
                }
                else
                {
                    var fileName = Path.GetFileName(_localPath);
                    var networkPath = Path.Combine(_networkFolder, fileName);
                    File.AppendAllText(networkPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // Common real causes here: the account running the app has
                // read-only access to the share, the share requires
                // different credentials than the current Windows session,
                // or the folder path in config.json doesn't actually match
                // what looks reachable in Explorer (e.g. mapped drive
                // letter vs UNC path resolving differently for this
                // process). The exception message below will say which.
                error = $"Failed to write to network log ({_networkFolder}): {ex.Message}";
            }

            // Only report on state changes (fail, or a different failure,
            // or recovery) - not on every single line, which would spam
            // the UI log while the network stays down.
            if (error != _lastNetworkError)
            {
                _lastNetworkError = error;
                NetworkWriteFailed?.Invoke(error);
            }
        }
    }
}
