namespace LedMatrixControl
{
    /// <summary>
    /// Abstract hardware boundary. A single call writes one action to one
    /// channel's register on the target commutator (R421B16-style board).
    ///
    /// Deliberately write-only: this board does not give reliable relay
    /// state back over Modbus. Reading holding registers (FC 0x03) returns
    /// a fixed junk value (0x00F1) on register 1 and 0 on the rest, with no
    /// relation to actual relay state. Do not add a read/verify method
    /// assuming it will reflect reality - track intended state in the
    /// calling code if you need it.
    /// </summary>
    public interface IRelayCommutator
    {
        /// <param name="commutatorAddress">Modbus address of the board.</param>
        /// <param name="channel">1-16 selects a single relay's register.
        /// Use the configured "all channels" value to address every relay
        /// on that commutator at once (board-specific).</param>
        /// <param name="turnOn">True writes the ON action (0x0100), false
        /// writes the OFF action (0x0200).</param>
        void SelectChannel(byte commutatorAddress, int channel, bool turnOn);
    }
}
