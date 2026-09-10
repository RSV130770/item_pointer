using System;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

namespace LedMatrixControl
{
    /// <summary>
    /// Reads stable weight readings from the scale over RS232. The scale
    /// only transmits when the weight is stable, so any complete line
    /// matching the weight pattern is treated as a valid reading - no extra
    /// stability/debounce logic is needed on our side.
    ///
    /// Expected format (only the first line of each 3-line block matters):
    ///   "+ 2305.5g"
    ///   "      0g /PCS"
    ///   "      0PCS"
    /// The first line always starts with an explicit sign (+/-); the other
    /// two lines don't, which is how we tell them apart.
    /// </summary>
    public class ScaleReader : IDisposable
    {
        private static readonly Regex WeightLinePattern =
            new(@"^\s*([+\-])\s*([\d.,]+)\s*g\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly SerialPort _port;
        private Thread _readThread;
        private volatile bool _running;
        private readonly object _closeLock = new();
        private bool _closed;

        public event Action<double> StableWeightReceived;
        public event Action<string> Trace;
        public event Action<string> Disconnected;

        public ScaleReader(SerialPortConfig settings)
        {
            _port = new SerialPort(
                settings.PortName,
                settings.BaudRate,
                ParseParity(settings.Parity),
                settings.DataBits,
                ParseStopBits(settings.StopBits))
            {
                ReadTimeout = settings.ResponseTimeoutMs > 0 ? settings.ResponseTimeoutMs : 2000
            };
        }

        private static Parity ParseParity(string value) =>
            Enum.TryParse<Parity>(value, true, out var result) ? result : Parity.None;

        private static StopBits ParseStopBits(string value) =>
            Enum.TryParse<StopBits>(value, true, out var result) ? result : StopBits.One;

        public void Start()
        {
            _port.Open();
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();
        }

        private void ReadLoop()
        {
            try
            {
                using var reader = new StreamReader(_port.BaseStream);
                while (_running)
                {
                    string line;
                    try
                    {
                        line = reader.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }
                    catch (IOException ex)
                    {
                        if (_running)
                            Disconnected?.Invoke($"Scale port error: {ex.Message}");
                        return;
                    }

                    if (line == null)
                        continue;

                    Trace?.Invoke($"RX: {line}");

                    var match = WeightLinePattern.Match(line);
                    if (!match.Success)
                        continue; // not the weight line (probably the /PCS or count line)

                    var sign = match.Groups[1].Value == "-" ? -1 : 1;
                    var magnitude = TaskCsvLoader.ParseFlexibleDouble(match.Groups[2].Value);
                    StableWeightReceived?.Invoke(sign * magnitude);
                }
            }
            catch (Exception ex)
            {
                if (_running)
                    Disconnected?.Invoke($"Scale reader stopped unexpectedly: {ex.Message}");
            }
            finally
            {
                // This thread owns the port's lifecycle: it's the one that
                // wrapped it in a StreamReader, so it's the one that closes
                // it, here, once, after the reader itself has already been
                // disposed by the `using` above. ClosePortOnce() guards
                // against Dispose() on another thread racing to close the
                // same SerialStream concurrently (which corrupts its
                // internal state and throws NullReferenceException).
                ClosePortOnce();
            }
        }

        private void ClosePortOnce()
        {
            lock (_closeLock)
            {
                if (_closed)
                    return;
                _closed = true;
                try { if (_port.IsOpen) _port.Close(); } catch { /* best effort */ }
            }
        }

        public void Dispose()
        {
            _running = false;

            // Give the read thread a chance to notice and exit cleanly on
            // its own (it polls _running roughly every ReadTimeout via the
            // TimeoutException path). Only force-close as a last resort if
            // it doesn't finish in time.
            if (_readThread != null && _readThread.IsAlive)
                _readThread.Join(3000);

            ClosePortOnce();
            try { _port.Dispose(); } catch { /* best effort */ }
        }
    }
}
