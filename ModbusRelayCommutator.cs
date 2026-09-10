using System;
using System.IO.Ports;
using System.Threading;

namespace LedMatrixControl
{
    /// <summary>
    /// Real hardware implementation of IRelayCommutator for R421B16-style
    /// boards. Confirmed constraints from testing:
    ///  - Only FC 0x06 (Write Single Register) works; FC 0x10 (Write
    ///    Multiple Registers) times out and is never used.
    ///  - Every write is its own transaction: send, wait for the echo or
    ///    timeout, then pause before the next write. Never pipeline writes.
    ///  - Reads (FC 0x03) do not reflect real relay state on this board
    ///    (register 1 always returns 0x00F1, the rest return 0), so this
    ///    class does not attempt to verify state - see IRelayCommutator.
    /// </summary>
    public class ModbusRelayCommutator : IRelayCommutator, IDisposable
    {
        private const byte FunctionWriteSingleRegister = 0x06;
        private const ushort ActionOpen = 0x0100;   // relay ON
        private const ushort ActionClose = 0x0200;  // relay OFF
        private const ushort ActionToggle = 0x0300;

        private readonly SerialPort _port;
        private readonly SerialPortConfig _settings;
        private readonly object _lock = new();

        /// <summary>Fires with a human-readable line for every TX/RX, for debugging.</summary>
        public event Action<string> Trace;

        public ModbusRelayCommutator(SerialPortConfig settings)
        {
            _settings = settings;
            _port = new SerialPort(
                settings.PortName,
                settings.BaudRate,
                ParseParity(settings.Parity),
                settings.DataBits,
                ParseStopBits(settings.StopBits))
            {
                ReadTimeout = settings.ResponseTimeoutMs,
                WriteTimeout = settings.ResponseTimeoutMs
            };
        }

        public void SelectChannel(byte commutatorAddress, int channel, bool turnOn)
        {
            var action = turnOn ? ActionOpen : ActionClose;
            var frame = BuildWriteSingleRegisterFrame(commutatorAddress, (ushort)channel, action);

            lock (_lock)
            {
                if (!_port.IsOpen)
                    _port.Open();

                int attempt = 0;
                while (true)
                {
                    attempt++;
                    try
                    {
                        _port.DiscardInBuffer();
                        _port.Write(frame, 0, frame.Length);
                        Trace?.Invoke($"TX ({commutatorAddress}/{channel}/{(turnOn ? "ON" : "OFF")}): {ToHex(frame)}");

                        // Expect an echo of the same frame back (standard for FC 0x06).
                        var response = new byte[frame.Length];
                        int read = 0;
                        while (read < response.Length)
                        {
                            read += _port.Read(response, read, response.Length - read);
                        }
                        Trace?.Invoke($"RX: {ToHex(response)}");

                        break; // success
                    }
                    catch (TimeoutException)
                    {
                        if (attempt >= _settings.RetryCount)
                            throw new TimeoutException(
                                $"No response from commutator {commutatorAddress}, channel {channel} after {attempt} attempt(s).");
                        // retry
                    }
                    finally
                    {
                        Thread.Sleep(_settings.InterCommandDelayMs);
                    }
                }
            }
        }

        private static byte[] BuildWriteSingleRegisterFrame(byte slaveAddress, ushort registerAddress, ushort value)
        {
            var frame = new byte[8];
            frame[0] = slaveAddress;
            frame[1] = FunctionWriteSingleRegister;
            frame[2] = (byte)(registerAddress >> 8);
            frame[3] = (byte)(registerAddress & 0xFF);
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)(value & 0xFF);

            ushort crc = ComputeCrc16(frame, 6);
            frame[6] = (byte)(crc & 0xFF);       // CRC low byte first
            frame[7] = (byte)(crc >> 8);         // CRC high byte

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
                    if (lsbSet)
                        crc ^= 0xA001;
                }
            }
            return crc;
        }

        private static string ToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");

        private static Parity ParseParity(string value) =>
            Enum.TryParse<Parity>(value, true, out var result) ? result : Parity.None;

        private static StopBits ParseStopBits(string value) =>
            Enum.TryParse<StopBits>(value, true, out var result) ? result : StopBits.One;

        public void Dispose()
        {
            if (_port.IsOpen)
                _port.Close();
            _port.Dispose();
        }
    }
}
