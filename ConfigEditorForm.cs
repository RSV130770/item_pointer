using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class ConfigEditorForm : Form
    {
        private readonly string _configPath;
        private LedMatrixConfig _config;

        private readonly TextBox _rowAddr = new() { Width = 200 };
        private readonly TextBox _relayPort = new() { Width = 200 };
        private readonly TextBox _relayBaud = new() { Width = 200 };
        private readonly TextBox _scalePort = new() { Width = 200 };
        private readonly TextBox _scaleBaud = new() { Width = 200 };
        private readonly TextBox _columnCommutators = new() { Width = 200 }; // "2:0,3:16"
        private readonly TextBox _rows = new() { Width = 200 };
        private readonly TextBox _colsPerCommutator = new() { Width = 200 };
        private readonly TextBox _taskFolder = new() { Width = 300 };
        private readonly TextBox _logFolder = new() { Width = 300 };
        private readonly TextBox _usersFile = new() { Width = 300 };
        private readonly TextBox _precisionPercent = new() { Width = 200 };
        private readonly TextBox _minWeight = new() { Width = 200 };
        private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.DarkGreen };

        public ConfigEditorForm(string configPath)
        {
            _configPath = configPath;
            Text = "Config editor";
            Width = 560;
            Height = 560;

            _config = System.IO.File.Exists(_configPath)
                ? LedMatrixConfig.LoadFromFile(_configPath)
                : new LedMatrixConfig();

            BuildLayout();
            PopulateFromConfig();
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(15) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(layout, "Row commutator address:", _rowAddr);
            AddRow(layout, "Relay serial port:", _relayPort);
            AddRow(layout, "Relay baud rate:", _relayBaud);
            AddRow(layout, "Scale serial port:", _scalePort);
            AddRow(layout, "Scale baud rate:", _scaleBaud);
            AddRow(layout, "Column commutators (addr:startCol,...):", _columnCommutators);
            AddRow(layout, "Rows:", _rows);
            AddRow(layout, "Columns per commutator:", _colsPerCommutator);
            AddRow(layout, "Task folder (network):", _taskFolder);
            AddRow(layout, "Log folder (network):", _logFolder);
            AddRow(layout, "Users file:", _usersFile);
            AddRow(layout, "Default precision, % of expected weight:", _precisionPercent);
            AddRow(layout, "Minimum valid weight (g):", _minWeight);

            var saveButton = new Button { Text = "Save", Width = 100, Height = 32 };
            saveButton.Click += (s, e) => Save();
            layout.Controls.Add(saveButton);
            layout.SetColumnSpan(saveButton, 2);

            layout.Controls.Add(_statusLabel);
            layout.SetColumnSpan(_statusLabel, 2);

            Controls.Add(layout);
        }

        private static void AddRow(TableLayoutPanel layout, string labelText, Control input)
        {
            layout.Controls.Add(new Label { Text = labelText, AutoSize = true, Margin = new Padding(3, 8, 10, 3) });
            layout.Controls.Add(input);
        }

        private void PopulateFromConfig()
        {
            _rowAddr.Text = _config.RowCommutator.Address.ToString();
            _relayPort.Text = _config.SerialPort.PortName;
            _relayBaud.Text = _config.SerialPort.BaudRate.ToString();
            _scalePort.Text = _config.ScalePort.PortName;
            _scaleBaud.Text = _config.ScalePort.BaudRate.ToString();
            _columnCommutators.Text = string.Join(",", _config.ColumnCommutators.Select(c => $"{c.Address}:{c.StartColumn}"));
            _rows.Text = _config.Rows.ToString();
            _colsPerCommutator.Text = _config.ColumnsPerCommutator.ToString();
            _taskFolder.Text = _config.TaskFolderPath;
            _logFolder.Text = _config.LogFolderPath;
            _usersFile.Text = _config.UsersFilePath;
            _precisionPercent.Text = _config.DefaultPrecisionPercent.ToString();
            _minWeight.Text = _config.MinimumValidWeightGrams.ToString();
        }

        private void Save()
        {
            try
            {
                _config.RowCommutator.Address = byte.Parse(_rowAddr.Text);
                _config.SerialPort.PortName = _relayPort.Text.Trim();
                _config.SerialPort.BaudRate = int.Parse(_relayBaud.Text);
                _config.ScalePort.PortName = _scalePort.Text.Trim();
                _config.ScalePort.BaudRate = int.Parse(_scaleBaud.Text);
                _config.ColumnCommutators = ParseColumnCommutators(_columnCommutators.Text);
                _config.Rows = int.Parse(_rows.Text);
                _config.ColumnsPerCommutator = int.Parse(_colsPerCommutator.Text);
                _config.TaskFolderPath = _taskFolder.Text.Trim();
                _config.LogFolderPath = _logFolder.Text.Trim();
                _config.UsersFilePath = _usersFile.Text.Trim();
                _config.DefaultPrecisionPercent = double.Parse(_precisionPercent.Text);
                _config.MinimumValidWeightGrams = double.Parse(_minWeight.Text);

                _config.SaveToFile(_configPath);
                _statusLabel.ForeColor = Color.DarkGreen;
                _statusLabel.Text = $"Saved to {_configPath} at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _statusLabel.ForeColor = Color.Red;
                _statusLabel.Text = $"Save failed: {ex.Message}";
            }
        }

        private static List<ColumnCommutatorConfig> ParseColumnCommutators(string text)
        {
            var result = new List<ColumnCommutatorConfig>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split(':');
                if (pieces.Length != 2)
                    throw new FormatException($"Invalid column commutator entry: \"{part}\". Expected addr:startCol.");
                result.Add(new ColumnCommutatorConfig
                {
                    Address = byte.Parse(pieces[0].Trim()),
                    StartColumn = int.Parse(pieces[1].Trim())
                });
            }
            return result;
        }
    }
}
