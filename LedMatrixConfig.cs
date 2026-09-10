using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LedMatrixControl
{
    public class RowCommutatorConfig
    {
        public byte Address { get; set; }
    }

    public class ColumnCommutatorConfig
    {
        public byte Address { get; set; }
        public int StartColumn { get; set; }
    }

    public class SerialPortConfig
    {
        public string PortName { get; set; } = "COM3";
        public int BaudRate { get; set; } = 9600;
        public string Parity { get; set; } = "None";
        public int DataBits { get; set; } = 8;
        public string StopBits { get; set; } = "One";
        public int ResponseTimeoutMs { get; set; } = 300;
        public int InterCommandDelayMs { get; set; } = 10;
        public int RetryCount { get; set; } = 3;
    }

    public class LedMatrixConfig
    {
        public RowCommutatorConfig RowCommutator { get; set; } = new();
        public List<ColumnCommutatorConfig> ColumnCommutators { get; set; } = new();
        public int Rows { get; set; } = 16;
        public int ColumnsPerCommutator { get; set; } = 16;
        public int AllChannelsValue { get; set; } = 255;
        public SerialPortConfig SerialPort { get; set; } = new();
        public SerialPortConfig ScalePort { get; set; } = new() { PortName = "COM4", BaudRate = 9600 };
        public string TaskFolderPath { get; set; } = @"\\server\share\tasks";
        public string SystemTaskFileName { get; set; } = "system_task.csv";
        public string LogFolderPath { get; set; } = @"\\server\share\tasks\logs";
        public string UsersFilePath { get; set; } = "users.json";
        public double DefaultPrecisionPercent { get; set; } = 2.0;
        public double MinimumValidWeightGrams { get; set; } = 5.0;

        /// <summary>
        /// Total addressable columns across all configured column commutators.
        /// </summary>
        public int TotalColumns => ColumnCommutators.Count * ColumnsPerCommutator;

        public static LedMatrixConfig LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var config = JsonSerializer.Deserialize<LedMatrixConfig>(json, options);
            if (config == null)
                throw new InvalidDataException($"Failed to parse config file: {path}");
            return config;
        }

        public void SaveToFile(string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(path, JsonSerializer.Serialize(this, options));
        }
    }
}
