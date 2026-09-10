using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LedMatrixControl
{
    public class DiagnosticResult
    {
        public string Name { get; set; } = "";
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
    }

    public class DiagnosticsReport
    {
        public List<DiagnosticResult> Results { get; } = new();
        public bool AllOk => Results.All(r => r.Ok);
        public bool ConfigWasModified { get; set; }

        public string Summary()
        {
            return string.Join(Environment.NewLine,
                Results.Select(r => $"[{(r.Ok ? "OK" : "FAIL")}] {r.Name}: {r.Message}"));
        }
    }

    /// <summary>
    /// Every startup health check lives here: which COM ports are present,
    /// whether the configured relay/scale ports are actually the right
    /// devices (probed, not just "does the port name exist"), auto-detecting
    /// and correcting a shifted port number, and whether the task/log
    /// folders and users file are reachable. Callers get a single report to
    /// log and/or show the user.
    /// </summary>
    public static class StartupDiagnostics
    {
        private static readonly Regex WeightLinePattern =
            new(@"^\s*([+\-])\s*([\d.,]+)\s*g\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DiagnosticsReport RunAll(LedMatrixConfig config, Action<string> onLog = null)
        {
            var report = new DiagnosticsReport();
            void Emit(DiagnosticResult r) { report.Results.Add(r); onLog?.Invoke($"[{(r.Ok ? "OK" : "FAIL")}] {r.Name}: {r.Message}"); }

            var availablePorts = SerialPort.GetPortNames();
            Emit(new DiagnosticResult
            {
                Name = "COM ports visible to Windows",
                Ok = availablePorts.Length > 0,
                Message = availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "No serial ports found at all."
            });

            CheckOrDetectRelay(config, availablePorts, report, Emit);
            CheckOrDetectScale(config, availablePorts, report, Emit);
            CheckFolder("Task folder", config.TaskFolderPath, Emit);
            CheckFolder("Log folder", config.LogFolderPath, Emit);
            CheckUsersFile(config, Emit);

            return report;
        }

        private static void CheckOrDetectRelay(LedMatrixConfig config, string[] availablePorts, DiagnosticsReport report, Action<DiagnosticResult> emit)
        {
            if (availablePorts.Contains(config.SerialPort.PortName) && ProbeRelay(config.SerialPort.PortName, config))
            {
                emit(new DiagnosticResult { Name = "Relay port", Ok = true, Message = $"{config.SerialPort.PortName} responded correctly." });
                return;
            }

            // Configured port missing or didn't respond as a relay board - try every other port.
            foreach (var candidate in availablePorts.Where(p => p != config.SerialPort.PortName))
            {
                if (ProbeRelay(candidate, config))
                {
                    var oldPort = config.SerialPort.PortName;
                    config.SerialPort.PortName = candidate;
                    report.ConfigWasModified = true;
                    emit(new DiagnosticResult
                    {
                        Name = "Relay port",
                        Ok = true,
                        Message = $"Configured port {oldPort} did not respond; auto-detected relay on {candidate} instead."
                    });
                    return;
                }
            }

            emit(new DiagnosticResult
            {
                Name = "Relay port",
                Ok = false,
                Message = $"Configured port {config.SerialPort.PortName} not found or not responding, and no other port responded as a relay board. Check cabling/power."
            });
        }

        private static void CheckOrDetectScale(LedMatrixConfig config, string[] availablePorts, DiagnosticsReport report, Action<DiagnosticResult> emit)
        {
            if (availablePorts.Contains(config.ScalePort.PortName) && ProbeScale(config.ScalePort.PortName, config))
            {
                emit(new DiagnosticResult { Name = "Scale port", Ok = true, Message = $"{config.ScalePort.PortName} sent recognizable weight data." });
                return;
            }

            foreach (var candidate in availablePorts.Where(p => p != config.ScalePort.PortName && p != config.SerialPort.PortName))
            {
                if (ProbeScale(candidate, config))
                {
                    var oldPort = config.ScalePort.PortName;
                    config.ScalePort.PortName = candidate;
                    report.ConfigWasModified = true;
                    emit(new DiagnosticResult
                    {
                        Name = "Scale port",
                        Ok = true,
                        Message = $"Configured port {oldPort} sent nothing recognizable; auto-detected scale on {candidate} instead."
                    });
                    return;
                }
            }

            emit(new DiagnosticResult
            {
                Name = "Scale port",
                Ok = false,
                Message = $"Configured port {config.ScalePort.PortName} not found or silent, and no other port sent recognizable weight data. " +
                          "Note: the scale only transmits when the weight is stable/changes, so an idle scale with nothing on it recently may not be detected - this can be a false alarm."
            });
        }

        /// <summary>
        /// Probes a port for the relay board by sending a harmless, idempotent
        /// write (turn channel 1 OFF on commutator address 1) and checking
        /// for the expected echo. Safe to repeat - OFF on an already-off
        /// relay has no effect.
        /// </summary>
        /// <summary>
        /// SerialPort.Open() has no built-in timeout and can hang
        /// indefinitely on some virtual/Bluetooth COM ports. Every probe
        /// runs on its own background thread with a hard wall-clock limit;
        /// if it doesn't finish in time, this returns false and moves on.
        /// The stuck background thread is abandoned (it's a background
        /// thread, so it won't keep the app alive) rather than waited on.
        /// </summary>
        private static bool RunWithHardTimeout(Func<bool> probe, int timeoutMs)
        {
            var task = Task.Run(() =>
            {
                try { return probe(); }
                catch { return false; }
            });

            if (task.Wait(timeoutMs))
                return task.Result;

            return false; // timed out - abandon it, don't wait any longer
        }

        private static bool ProbeRelay(string portName, LedMatrixConfig config) =>
            RunWithHardTimeout(() => ProbeRelayInner(portName, config), 2000);

        private static bool ProbeRelayInner(string portName, LedMatrixConfig config)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var port = new SerialPort(
                        portName,
                        config.SerialPort.BaudRate,
                        Enum.TryParse<Parity>(config.SerialPort.Parity, true, out var parity) ? parity : Parity.None,
                        config.SerialPort.DataBits,
                        Enum.TryParse<StopBits>(config.SerialPort.StopBits, true, out var stopBits) ? stopBits : StopBits.One)
                    {
                        ReadTimeout = 400,
                        WriteTimeout = 400
                    };
                    port.Open();
                    Thread.Sleep(50); // let USB-RS485 adapters settle before the first write

                    var frame = BuildWriteSingleRegisterFrame(config.RowCommutator.Address, 1, 0x0200); // channel 1 OFF
                    port.DiscardInBuffer();
                    port.Write(frame, 0, frame.Length);

                    var response = new byte[frame.Length];
                    int read = 0;
                    while (read < response.Length)
                        read += port.Read(response, read, response.Length - read);

                    if (response.SequenceEqual(frame))
                        return true;
                }
                catch
                {
                    // fall through to retry
                }

                if (attempt < 3)
                    Thread.Sleep(150);
            }
            return false;
        }

        /// <summary>
        /// Probes a port for the scale by listening briefly for any line
        /// matching the expected weight pattern. Since the scale only sends
        /// on a stable reading/change, this can miss a genuinely-connected
        /// idle scale - callers should treat a failed probe as inconclusive,
        /// not certain absence.
        /// </summary>
        private static bool ProbeScale(string portName, LedMatrixConfig config) =>
            RunWithHardTimeout(() => ProbeScaleInner(portName, config), 3000);

        private static bool ProbeScaleInner(string portName, LedMatrixConfig config)
        {
            try
            {
                using var port = new SerialPort(
                    portName,
                    config.ScalePort.BaudRate,
                    Enum.TryParse<Parity>(config.ScalePort.Parity, true, out var parity) ? parity : Parity.None,
                    config.ScalePort.DataBits,
                    Enum.TryParse<StopBits>(config.ScalePort.StopBits, true, out var stopBits) ? stopBits : StopBits.One)
                {
                    ReadTimeout = 200
                };
                port.Open();

                using var reader = new StreamReader(port.BaseStream);
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    string line;
                    try { line = reader.ReadLine(); }
                    catch (TimeoutException) { continue; }
                    catch (IOException) { break; }

                    if (line != null && WeightLinePattern.IsMatch(line))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] BuildWriteSingleRegisterFrame(byte slaveAddress, ushort registerAddress, ushort value)
        {
            var frame = new byte[8];
            frame[0] = slaveAddress;
            frame[1] = 0x06;
            frame[2] = (byte)(registerAddress >> 8);
            frame[3] = (byte)(registerAddress & 0xFF);
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)(value & 0xFF);
            ushort crc = ComputeCrc16(frame, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private static ushort ComputeCrc16(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    bool lsbSet = (crc & 0x0001) != 0;
                    crc >>= 1;
                    if (lsbSet) crc ^= 0xA001;
                }
            }
            return crc;
        }

        private static void CheckFolder(string name, string path, Action<DiagnosticResult> emit)
        {
            bool ok;
            string message;
            try
            {
                ok = Directory.Exists(path);
                message = ok ? path : $"{path} not reachable (will fall back to local folder where applicable).";
            }
            catch (Exception ex)
            {
                ok = false;
                message = $"{path} check failed: {ex.Message}";
            }
            emit(new DiagnosticResult { Name = name, Ok = ok, Message = message });
        }

        private static void CheckUsersFile(LedMatrixConfig config, Action<DiagnosticResult> emit)
        {
            var path = Path.IsPathRooted(config.UsersFilePath)
                ? config.UsersFilePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.UsersFilePath);

            bool ok = File.Exists(path);
            emit(new DiagnosticResult
            {
                Name = "Users file",
                Ok = ok,
                Message = ok ? path : $"{path} not found - nobody will be able to log in until this exists."
            });
        }
    }
}
