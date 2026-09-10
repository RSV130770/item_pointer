using System;
using System.Collections.Generic;
using System.Linq;

namespace LedMatrixControl
{
    /// <summary>
    /// Translates logical row/column addressing into relay writes.
    ///
    /// Tracks which channels are currently ON per commutator, and only
    /// sends the writes actually needed to reach the desired state (turn
    /// off what shouldn't be on, turn on what should be) instead of
    /// blindly clearing and re-setting every channel on every call. This
    /// cuts a typical SetLed() from ~50 serial transactions (a full sweep
    /// of every row + column channel) down to as few as 2-4.
    ///
    /// The tracked state is only as good as what's been written through
    /// this instance - after a fresh connection (or reconnect), call
    /// ResetToKnownState() once to establish a verified all-off baseline,
    /// since the physical board could be in any state left over from
    /// before a disconnect and there's no way to read it back (the board
    /// doesn't support reliable state read-back - see IRelayCommutator).
    /// </summary>
    public class LedMatrix
    {
        private readonly IRelayCommutator _hw;
        private readonly LedMatrixConfig _config;
        private readonly Dictionary<byte, HashSet<int>> _onChannels = new();

        public event Action<string> Warning;

        public LedMatrix(IRelayCommutator hardware, LedMatrixConfig config)
        {
            _hw = hardware;
            _config = config;
        }

        /// <summary>
        /// Safe connectivity probe for periodic health checks: re-asserts
        /// whatever channel 1 on the row commutator is *currently tracked*
        /// as being (ON or OFF), rather than blindly writing a fixed value.
        /// This is idempotent from LedMatrix's own point of view - it can
        /// never desync the tracked state from physical reality the way a
        /// raw IRelayCommutator.SelectChannel() call from outside this
        /// class would (that bypasses tracking entirely and was the cause
        /// of a real bug: the periodic health check silently turning a
        /// channel off behind LedMatrix's back, after which LedMatrix
        /// believed that channel was still on and skipped writing it the
        /// next time it was actually needed).
        /// </summary>
        public void ProbeHealth()
        {
            var addr = _config.RowCommutator.Address;
            bool trackedOn = _onChannels.TryGetValue(addr, out var set) && set.Contains(1);
            _hw.SelectChannel(addr, 1, turnOn: trackedOn);
        }

        /// <summary>
        /// Full, unconditional clear of every channel on every commutator,
        /// regardless of tracked state. Call this once right after
        /// (re)connecting, since the physical relays could be in any state
        /// left over from before - the fast delta-based methods below only
        /// stay correct once the tracked state is known to be accurate.
        /// </summary>
        public void ResetToKnownState()
        {
            _onChannels.Clear();

            for (int ch = 1; ch <= _config.Rows; ch++)
                TrySelectChannel(_config.RowCommutator.Address, ch, turnOn: false);
            _onChannels[_config.RowCommutator.Address] = new HashSet<int>();

            foreach (var colCommutator in _config.ColumnCommutators)
            {
                // Give the bus a moment before addressing a *different*
                // physical device than the one we were just talking to.
                System.Threading.Thread.Sleep(100);

                for (int ch = 1; ch <= _config.ColumnsPerCommutator; ch++)
                    TrySelectChannel(colCommutator.Address, ch, turnOn: false);
                _onChannels[colCommutator.Address] = new HashSet<int>();
            }
        }

        /// <summary>
        /// Used only by ResetToKnownState(): this is a bulk sweep of many
        /// writes just to establish a known baseline, not a real
        /// operational command. A single flaky write among 48+ (e.g. one
        /// commutator's first response after an address switch) shouldn't
        /// abort the whole sweep or get treated by the caller as "the
        /// entire relay connection is broken" - that was blocking normal
        /// task workflow even though the hardware demonstrably worked
        /// (confirmed by manually testing a full row). Reports via
        /// Warning and moves on instead. The real operational methods
        /// (SetLed/SetFullRow/SetFullColumn/AllOff, via ApplyDesiredState)
        /// still throw normally - failures during actual task work should
        /// still be caught and trigger the reactive recovery flow.
        /// </summary>
        private void TrySelectChannel(byte commutatorAddress, int channel, bool turnOn)
        {
            try
            {
                _hw.SelectChannel(commutatorAddress, channel, turnOn);
            }
            catch (Exception ex)
            {
                Warning?.Invoke($"Reset: commutator {commutatorAddress} channel {channel} did not respond ({ex.Message}) - continuing anyway.");
            }
        }

        /// <summary>Light a single LED at (row, col); everything else turns off.</summary>
        public void SetLed(int row, int col)
        {
            ValidateRow(row);
            var colCommutator = ResolveColumnCommutator(col, out int localCol);

            ApplyDesiredState(new Dictionary<byte, HashSet<int>>
            {
                [_config.RowCommutator.Address] = new HashSet<int> { row + 1 },
                [colCommutator.Address] = new HashSet<int> { localCol + 1 }
            });
        }

        /// <summary>Light an entire row: the row relay plus every column relay on every commutator.</summary>
        public void SetFullRow(int row)
        {
            ValidateRow(row);

            var desired = new Dictionary<byte, HashSet<int>>
            {
                [_config.RowCommutator.Address] = new HashSet<int> { row + 1 }
            };
            foreach (var colCommutator in _config.ColumnCommutators)
                desired[colCommutator.Address] = new HashSet<int>(Enumerable.Range(1, _config.ColumnsPerCommutator));

            ApplyDesiredState(desired);
        }

        /// <summary>Light an entire column: every row relay plus the one column relay for this column.</summary>
        public void SetFullColumn(int col)
        {
            var colCommutator = ResolveColumnCommutator(col, out int localCol);

            ApplyDesiredState(new Dictionary<byte, HashSet<int>>
            {
                [_config.RowCommutator.Address] = new HashSet<int>(Enumerable.Range(1, _config.Rows)),
                [colCommutator.Address] = new HashSet<int> { localCol + 1 }
            });
        }

        /// <summary>Turns everything off (only writes to channels actually tracked as on).</summary>
        public void AllOff()
        {
            ApplyDesiredState(new Dictionary<byte, HashSet<int>>());
        }

        /// <summary>
        /// Brings every known commutator to the given desired ON-channel
        /// set, writing only the deltas against tracked state. Commutators
        /// not present in <paramref name="desired"/> are treated as
        /// "everything off" for that commutator.
        /// </summary>
        private void ApplyDesiredState(Dictionary<byte, HashSet<int>> desired)
        {
            var allCommutators = new List<byte> { _config.RowCommutator.Address };
            allCommutators.AddRange(_config.ColumnCommutators.Select(c => c.Address));

            byte? previousAddr = null;
            foreach (var addr in allCommutators)
            {
                // Same settle delay as ResetToKnownState, for the same
                // reason: a newly-addressed commutator's first write can
                // repeatably time out right after a different device on
                // the shared bus was just active.
                if (previousAddr.HasValue && previousAddr.Value != addr)
                    System.Threading.Thread.Sleep(100);
                previousAddr = addr;

                if (!desired.TryGetValue(addr, out var want))
                    want = new HashSet<int>();

                if (!_onChannels.TryGetValue(addr, out var current))
                {
                    current = new HashSet<int>();
                    _onChannels[addr] = current;
                }

                // Update tracked state immediately after each successful
                // write (not after the whole loop), so a mid-loop failure
                // still leaves the tracked state matching whatever was
                // actually toggled before the error.
                foreach (var ch in current.Except(want).ToList())
                {
                    _hw.SelectChannel(addr, ch, turnOn: false);
                    current.Remove(ch);
                }
                foreach (var ch in want.Except(current).ToList())
                {
                    _hw.SelectChannel(addr, ch, turnOn: true);
                    current.Add(ch);
                }
            }
        }

        private ColumnCommutatorConfig ResolveColumnCommutator(int col, out int localCol)
        {
            foreach (var c in _config.ColumnCommutators)
            {
                if (col >= c.StartColumn && col < c.StartColumn + _config.ColumnsPerCommutator)
                {
                    localCol = col - c.StartColumn;
                    return c;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(col), col, "Column is outside all configured column commutators.");
        }

        private void ValidateRow(int row)
        {
            if (row < 0 || row >= _config.Rows)
                throw new ArgumentOutOfRangeException(nameof(row), row, $"Row must be between 0 and {_config.Rows - 1}.");
        }
    }
}
