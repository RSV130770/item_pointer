using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LedMatrixControl
{
    /// <summary>
    /// A whole loaded/saved task file: the mass coefficient and title live
    /// at the file level (not per-item), alongside the item list.
    /// </summary>
    public class TaskFile
    {
        public double MassCoefficient { get; set; } = 1.0;
        public string Title { get; set; } = "";
        public List<TaskItem> Items { get; set; } = new();
    }

    /// <summary>
    /// The one task file format used everywhere - loading from the task
    /// folder, saving edits, and the local system task list all use this:
    ///   Row 1: mass coefficient (cell A), rest blank
    ///   Row 2: title (cell A), rest blank
    ///   Row 3: header row - Name;Qty;Weight, kg;Length, mm
    ///   Row 4+: data rows
    /// Semicolon-delimited, comma as decimal separator, UTF-8 with BOM,
    /// CRLF line endings. Label/index is always set equal to Name - there
    /// is no separate short pseudonym column in this format. Weight is
    /// converted kg&lt;-&gt;grams and multiplied/divided by the mass
    /// coefficient on load/save respectively, so round-tripping an
    /// unedited file reproduces the same kg values.
    /// </summary>
    public static class TaskCsvLoader
    {
        public static TaskFile Load(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3)
                throw new InvalidDataException("File is too short - expected a coefficient row, a title row, and a header row.");

            var taskFile = new TaskFile
            {
                MassCoefficient = ParseFlexibleDouble(FirstField(lines[0])),
                Title = FirstField(lines[1])
            };
            if (taskFile.MassCoefficient <= 0)
                taskFile.MassCoefficient = 1.0; // safety net against a blank/zero coefficient cell

            var headers = SplitSemicolon(lines[2]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            int nameCol = headers.FindIndex(h => h.StartsWith("name"));
            int qtyCol = headers.FindIndex(h => h.StartsWith("qty"));
            int weightCol = headers.FindIndex(h => h.StartsWith("weight"));
            int lengthCol = headers.FindIndex(h => h.StartsWith("length"));

            if (nameCol < 0 || qtyCol < 0 || weightCol < 0)
                throw new InvalidDataException("Header row must contain Name, Qty, and Weight columns.");

            for (int i = 3; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var fields = SplitSemicolon(lines[i]);
                var name = SafeGet(fields, nameCol).Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                var qty = (int)ParseFlexibleDouble(SafeGet(fields, qtyCol));
                var weightKg = ParseFlexibleDouble(SafeGet(fields, weightCol));
                var weightGrams = weightKg * 1000.0; // unit conversion only - coefficient is applied at comparison time, not stored into item.Weight

                double? lengthMm = null;
                var lengthRaw = lengthCol >= 0 ? SafeGet(fields, lengthCol) : "";
                if (!string.IsNullOrWhiteSpace(lengthRaw))
                    lengthMm = ParseFlexibleDouble(lengthRaw);

                taskFile.Items.Add(new TaskItem
                {
                    Index = name,   // label = name - no separate pseudonym in this format
                    Name = name,
                    Quantity = qty,
                    Weight = weightGrams,
                    LengthMm = lengthMm
                });
            }

            return taskFile;
        }

        public static void Save(TaskFile taskFile, string path)
        {
            var coefficient = taskFile.MassCoefficient > 0 ? taskFile.MassCoefficient : 1.0;
            var lines = new List<string>
            {
                FormatDecimal(coefficient) + ";;;;",
                (taskFile.Title ?? "") + ";;;;",
                "Name;Qty;Weight, kg;Length, mm;"
            };

            foreach (var item in taskFile.Items)
            {
                var weightKg = item.Weight / 1000.0; // reverse of the plain unit conversion in Load() - coefficient is not part of item.Weight
                lines.Add(string.Join(";",
                    item.Name,
                    item.Quantity.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(weightKg),
                    item.LengthMm.HasValue ? FormatDecimal(item.LengthMm.Value) : "") + ";");
            }

            // UTF-8 with BOM, matching the original format's encoding.
            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private static string FormatDecimal(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture).Replace(".", ",");

        private static string FirstField(string line) => SplitSemicolon(line).FirstOrDefault()?.Trim() ?? "";

        /// <summary>Parses a double accepting either '.' or ',' as the decimal separator.</summary>
        public static double ParseFlexibleDouble(string raw)
        {
            raw = (raw ?? "").Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            // Fall back: treat comma as decimal separator.
            var normalized = raw.Replace(".", "").Replace(",", ".");
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            throw new FormatException($"Could not parse number: \"{raw}\"");
        }

        private static string SafeGet(List<string> fields, int index) =>
            index >= 0 && index < fields.Count ? fields[index] : "";

        private static List<string> SplitSemicolon(string line) => line.Split(';').ToList();
    }
}
