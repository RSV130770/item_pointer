using System;

namespace LedMatrixControl
{
    /// <summary>
    /// Fire-and-forget mock: logs every write instead of touching a serial
    /// port. Swap for a real ModbusRelayCommutator once the wire protocol
    /// (frame format, CRC, action codes) is confirmed on the actual board.
    /// </summary>
    public class MockRelayCommutator : IRelayCommutator
    {
        public void SelectChannel(byte commutatorAddress, int channel, bool turnOn)
        {
            Console.WriteLine($"[MOCK] commutator {commutatorAddress} -> channel {channel} -> {(turnOn ? "ON" : "OFF")}");
        }
    }
}
