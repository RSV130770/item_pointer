using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LedMatrixControl
{
    /// <summary>
    /// Imports the external box-list CSV format:
    ///   Row 1: mass coefficient (cell A), rest blank
    ///   Row 2: title/box identifier (cell A), rest blank
    ///   Row 3: header row - Name;Qty;Weight, kg;Length, mm
    ///   Row 4+: data rows
    ///
    /// Semicolon-delimited, comma as decimal separator (European format),
    /// UTF-8 with BOM, CRLF line endings - all handled automatically by
    /// File.ReadAllLines' default encoding detection.
    ///
    /// The label/index for every item is set equal to its Name (per
    /// requirement - these items don't have a separate short pseudonym,
    /// unlike LED-matrix-backed indices). Weight is converted from kg to
    /// grams and multiplied by the mass coefficient from cell A1.
    /// </summary>
    public static class ExternalTaskCsvImporter
    {
        public class ImportResult
        {
            public double MassCoefficient { get; set; }
            public string Title { get; set; } = "";
            public List<TaskItem> Items { get; set; } = new();
        }

        public static ImportResult Import(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 4)
                throw new InvalidDataException("File is too short - expected coefficient row, title row, header row, and at least one data row.");

            var result = new ImportResult
            {
                MassCoefficient = ParseFlexibleDouble(FirstField(lines[0])),
                Title = FirstField(lines[1])
            };

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
                var weightGrams = weightKg * 1000.0 * result.MassCoefficient;

                double? lengthMm = null;
                var lengthRaw = lengthCol >= 0 ? SafeGet(fields, lengthCol) : "";
                if (!string.IsNullOrWhiteSpace(lengthRaw))
                    lengthMm = ParseFlexibleDouble(lengthRaw);

                result.Items.Add(new TaskItem
                {
                    Index = name,   // label = name, per requirement
                    Name = name,
                    Quantity = qty,
                    Weight = weightGrams,
                    LengthMm = lengthMm
                });
            }

            return result;
        }

        private static string FirstField(string line) => SplitSemicolon(line).FirstOrDefault()?.Trim() ?? "";

        private static string SafeGet(List<string> fields, int index) =>
            index >= 0 && index < fields.Count ? fields[index] : "";

        private static List<string> SplitSemicolon(string line) => line.Split(';').ToList();

        /// <summary>Parses a number using comma as the decimal separator (this file's format), falling back to '.' if that's what's actually there.</summary>
        private static double ParseFlexibleDouble(string raw)
        {
            raw = (raw ?? "").Trim();
            var commaAsDecimal = raw.Replace(",", ".");
            if (double.TryParse(commaAsDecimal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
            throw new FormatException($"Could not parse number: \"{raw}\"");
        }
    }
}
