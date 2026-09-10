using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LedMatrixControl
{
    public class IndexEntry
    {
        public string Label { get; set; } = "";
        public double Weight { get; set; }
        /// <summary>Null if this index has no physical LED - a "further index" that's purely a weight lookup.</summary>
        public int? Row { get; set; }
        public int? Col { get; set; }
    }

    /// <summary>
    /// The master registry of indices (bin labels): each has a weight, and
    /// optionally a physical LED position. An index without a position is
    /// perfectly valid - it's just not wired to any LED, and looking it up
    /// for LED addressing simply returns "not found" without that being an
    /// error condition anywhere in the app.
    ///
    /// Stored as index.csv. Writes are atomic (write to a temp file, then
    /// swap it in, keeping a .bak of the previous version) so a crash or
    /// power loss mid-save can never leave index.csv truncated or corrupt -
    /// worst case you lose the very last edit, never the whole file.
    /// </summary>
    public class IndexRegistry
    {
        public const int MaxLabelLength = 5;

        private readonly string _path;
        private readonly Dictionary<string, IndexEntry> _byLabel = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _hasUnsavedChanges;

        /// <summary>Absolute path this registry actually reads/writes - log this anywhere there's doubt about which file is in play.</summary>
        public string FilePath => _path;

        public IndexRegistry(string path)
        {
            _path = System.IO.Path.GetFullPath(path);
            Load();
        }

        public IReadOnlyCollection<IndexEntry> GetAll()
        {
            lock (_lock) { return _byLabel.Values.ToList(); }
        }

        /// <summary>Label shown on a grid cell, or "" if that cell has no label.</summary>
        public string Get(int row, int col)
        {
            lock (_lock)
            {
                var entry = _byLabel.Values.FirstOrDefault(e => e.Row == row && e.Col == col);
                return entry?.Label ?? "";
            }
        }

        /// <summary>Full entry for a label, or null if it doesn't exist.</summary>
        public IndexEntry GetEntry(string label)
        {
            lock (_lock)
            {
                return _byLabel.TryGetValue((label ?? "").Trim(), out var entry)
                    ? new IndexEntry { Label = entry.Label, Weight = entry.Weight, Row = entry.Row, Col = entry.Col }
                    : null;
            }
        }

        /// <summary>For LED addressing. Returns false if the label doesn't exist, or exists but has no physical position - neither case is an error.</summary>
        public bool TryFind(string label, out int row, out int col)
        {
            lock (_lock)
            {
                if (_byLabel.TryGetValue((label ?? "").Trim(), out var entry) && entry.Row.HasValue && entry.Col.HasValue)
                {
                    row = entry.Row.Value;
                    col = entry.Col.Value;
                    return true;
                }
            }
            row = -1;
            col = -1;
            return false;
        }

        // Common Cyrillic/Latin lookalike letters - visually identical on
        // screen but different Unicode characters, so ordinary string
        // comparison correctly treats them as different labels even
        // though a human reading the screen can't tell them apart. This
        // is a frequent, hard-to-spot source of "the label looks right
        // but doesn't match" bugs (e.g. one label typed on a keyboard as
        // Latin "C3", another coming from an imported file as Cyrillic
        // "С3" - pixel-identical, entirely different strings).
        private static readonly Dictionary<char, char> CyrillicToLatinLookalikes = new()
        {
            ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M',
            ['Н'] = 'H', ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T',
            ['У'] = 'Y', ['Х'] = 'X',
            ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c',
            ['у'] = 'y', ['х'] = 'x',
        };

        private static string NormalizeLookalikes(string s) =>
            new(s.Select(c => CyrillicToLatinLookalikes.TryGetValue(c, out var latin) ? latin : c).ToArray());

        /// <summary>
        /// Diagnostic helper: if `label` doesn't exist but a
        /// homoglyph-equivalent one does (e.g. Cyrillic vs Latin
        /// lookalikes), returns that actual stored label so callers can
        /// surface a "did you mean...?" hint instead of a silent mismatch.
        /// </summary>
        public bool TryFindLookalike(string label, out string actualStoredLabel)
        {
            var target = (label ?? "").Trim();
            var normalizedTarget = NormalizeLookalikes(target);

            lock (_lock)
            {
                foreach (var key in _byLabel.Keys)
                {
                    if (!string.Equals(key, target, StringComparison.Ordinal) &&
                        string.Equals(NormalizeLookalikes(key), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        actualStoredLabel = key;
                        return true;
                    }
                }
            }
            actualStoredLabel = null;
            return false;
        }

        /// <summary>For weight lookups from task rows. Works regardless of whether the index has a physical position.</summary>
        public bool TryGetWeight(string label, out double weight)
        {
            lock (_lock)
            {
                if (_byLabel.TryGetValue((label ?? "").Trim(), out var entry))
                {
                    weight = entry.Weight;
                    return true;
                }
            }
            weight = 0;
            return false;
        }

        /// <summary>
        /// Creates or updates an index. row/col are optional - pass null
        /// for a weight-only index with no LED. Returns false without
        /// saving if the label is already used by a different entry.
        /// </summary>
        public bool Set(string label, double weight, int? row, int? col) =>
            Set(label, weight, row, col, out _);

        /// <summary>
        /// Same as Set(), but reports exactly why a rejection happened
        /// instead of a bare false - needed to tell a genuine duplicate
        /// apart from an actual bug when something unexpected is rejected.
        /// </summary>
        public bool Set(string label, double weight, int? row, int? col, out string rejectionReason)
        {
            rejectionReason = null;
            label = (label ?? "").Trim();
            // Only truncate for LED-backed entries - the 5-char limit
            // exists so the name fits on a physical grid button, which
            // doesn't apply to label-only entries. Truncating those too
            // would risk silently colliding two different long names that
            // happen to share the same first 5 characters.
            if (row.HasValue && col.HasValue && label.Length > MaxLabelLength)
                label = label.Substring(0, MaxLabelLength);
            if (string.IsNullOrEmpty(label))
            {
                rejectionReason = "Label is empty after trimming.";
                return false;
            }

            lock (_lock)
            {
                // Reject only if this exact label is already assigned to a
                // genuinely different position - not just because some
                // other (possibly stale) label happens to sit at the
                // target position (that's a rename, handled below).
                if (row.HasValue && col.HasValue &&
                    _byLabel.TryGetValue(label, out var existingByLabel) &&
                    (existingByLabel.Row != row || existingByLabel.Col != col))
                {
                    rejectionReason = $"Label \"{label}\" is already assigned to position ({existingByLabel.Row},{existingByLabel.Col}) with weight {existingByLabel.Weight}g. Requested position was ({row},{col}).";
                    return false;
                }

                // If a different label currently occupies this exact
                // position, this is a rename: drop the old key so it
                // doesn't linger as an orphaned duplicate at the same spot.
                if (row.HasValue && col.HasValue)
                {
                    var oldOccupant = _byLabel.Values.FirstOrDefault(e =>
                        e.Row == row && e.Col == col && !string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase));
                    if (oldOccupant != null)
                        _byLabel.Remove(oldOccupant.Label);
                }

                _byLabel[label] = new IndexEntry { Label = label, Weight = weight, Row = row, Col = col };
                _hasUnsavedChanges = true;
                return true;
            }
        }

        public void Delete(string label)
        {
            lock (_lock)
            {
                _byLabel.Remove((label ?? "").Trim());
                _hasUnsavedChanges = true;
            }
        }

        /// <summary>True if Set()/Delete() have been called since the last SaveChanges() or DiscardChanges().</summary>
        public bool HasUnsavedChanges
        {
            get { lock (_lock) { return _hasUnsavedChanges; } }
        }

        /// <summary>Persists pending edits to disk (atomic write, see Save()).</summary>
        public void SaveChanges()
        {
            lock (_lock)
            {
                Save();
                _hasUnsavedChanges = false;
            }
        }

        /// <summary>Throws away pending edits, reverting to whatever is currently on disk.</summary>
        public void DiscardChanges()
        {
            lock (_lock)
            {
                _byLabel.Clear();
                Load();
                _hasUnsavedChanges = false;
            }
        }

        private void Load()
        {
            if (!File.Exists(_path))
                return;

            try
            {
                var lines = File.ReadAllLines(_path);
                if (lines.Length == 0)
                    return;

                var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToList();
                int labelCol = headers.IndexOf("label");
                int weightCol = headers.IndexOf("weight");
                int rowCol = headers.IndexOf("row");
                int colCol = headers.IndexOf("col");

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;
                    var fields = lines[i].Split(',');

                    var label = labelCol >= 0 && labelCol < fields.Length ? fields[labelCol].Trim() : "";
                    if (string.IsNullOrEmpty(label))
                        continue;

                    var weight = weightCol >= 0 && weightCol < fields.Length
                        ? TaskCsvLoader.ParseFlexibleDouble(fields[weightCol])
                        : 0;

                    int? row = null, col = null;
                    if (rowCol >= 0 && rowCol < fields.Length && int.TryParse(fields[rowCol].Trim(), out var r))
                        row = r;
                    if (colCol >= 0 && colCol < fields.Length && int.TryParse(fields[colCol].Trim(), out var c))
                        col = c;

                    _byLabel[label] = new IndexEntry { Label = label, Weight = weight, Row = row, Col = col };
                }
            }
            catch
            {
                // Corrupt/unreadable file: start empty rather than crash.
                // The .bak file (if one exists from a prior successful
                // save) is left untouched on disk for manual recovery.
            }
        }

        private void Save()
        {
            var lines = new List<string> { "label,weight,row,col" };
            foreach (var entry in _byLabel.Values.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Join(",",
                    entry.Label,
                    entry.Weight.ToString(CultureInfo.InvariantCulture),
                    entry.Row?.ToString(CultureInfo.InvariantCulture) ?? "",
                    entry.Col?.ToString(CultureInfo.InvariantCulture) ?? ""));
            }

            // Atomic swap: write to a temp file first, then replace the
            // real file in one filesystem operation (keeping a .bak of
            // what was there before). If anything throws before the
            // replace happens, the original index.csv is never touched.
            var tempPath = _path + ".tmp";
            var backupPath = _path + ".bak";

            File.WriteAllLines(tempPath, lines);

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
    }
}
