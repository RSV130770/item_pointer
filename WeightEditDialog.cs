using System;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class WeightEditDialog : Form
    {
        private readonly TextBox _weightBox = new() { Font = new Font(FontFamily.GenericSansSerif, 24), Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Center };
        private readonly Label _liveWeightLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DarkGreen };
        private readonly Button _useScaleButton = new() { Text = "Use current scale reading", Font = new Font(FontFamily.GenericSansSerif, 12), Dock = DockStyle.Fill };
        private readonly Label _errorLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 12), ForeColor = Color.Red, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        private readonly System.Windows.Forms.Timer _liveWeightTimer = new() { Interval = 300 };

        private readonly Func<double> _getCurrentScaleWeight;
        private bool _userEditedWeight = true; // protect the pre-filled existing weight until the operator opts into live tracking

        public double ResultWeight { get; private set; }

        /// <summary>
        /// getCurrentScaleWeight, if provided, is polled continuously
        /// (every 300ms) while this dialog is open and shown live - same
        /// treatment as TaskItemEditForm's weight field. currentWeight
        /// (the pre-fill) and ResultWeight are both TRUE weight, in the
        /// same units as the live scale readout - the caller is
        /// responsible for converting to/from whatever internal
        /// representation it uses (e.g. dividing by a mass coefficient
        /// before storing). The pre-filled currentWeight is protected
        /// from being overwritten until "Use current scale reading" is
        /// clicked, at which point it switches to live-follow.
        /// </summary>
        public WeightEditDialog(string indexLabel, string itemName, double currentWeight, Func<double> getCurrentScaleWeight = null)
        {
            Text = "Update weight";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            _getCurrentScaleWeight = getCurrentScaleWeight;
            _weightBox.Text = currentWeight.ToString("0.0");
            _weightBox.TextChanged += (s, e) => { if (!_settingWeightProgrammatically) _userEditedWeight = true; };

            BuildLayout(indexLabel, itemName);

            if (_getCurrentScaleWeight != null)
            {
                _liveWeightTimer.Tick += (s, e) => UpdateLiveWeightLabel();
                _liveWeightTimer.Start();
                UpdateLiveWeightLabel();
            }
            FormClosed += (s, e) => _liveWeightTimer.Stop();
        }

        private bool _settingWeightProgrammatically;

        private void UpdateLiveWeightLabel()
        {
            var current = _getCurrentScaleWeight();
            _liveWeightLabel.Text = $"Current scale reading: {current:0.0} g";

            if (!_userEditedWeight)
            {
                _settingWeightProgrammatically = true;
                _weightBox.Text = current.ToString("0.0");
                _settingWeightProgrammatically = false;
            }
        }

        private void BuildLayout(string indexLabel, string itemName)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(60) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleLabel = new Label
            {
                Text = $"Weight for index \"{indexLabel}\"",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(titleLabel, 0, 0);

            var subtitleLabel = new Label
            {
                Text = itemName,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 14),
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(subtitleLabel, 0, 1);

            _liveWeightLabel.Visible = _getCurrentScaleWeight != null;
            root.Controls.Add(_liveWeightLabel, 0, 2);

            root.Controls.Add(_weightBox, 0, 3);

            _useScaleButton.Margin = new Padding(40, 5, 40, 5);
            _useScaleButton.Click += (s, e) => { _userEditedWeight = false; if (_getCurrentScaleWeight != null) UpdateLiveWeightLabel(); };
            _useScaleButton.Visible = _getCurrentScaleWeight != null;
            root.Controls.Add(_useScaleButton, 0, 4);

            root.Controls.Add(_errorLabel, 0, 5);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var saveButton = new Button { Text = "Save", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold) };
            var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 18) };
            saveButton.Click += (s, e) => OnSaveClicked();
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonRow.Controls.Add(saveButton, 0, 0);
            buttonRow.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 6);

            Controls.Add(root);
        }

        private void OnSaveClicked()
        {
            if (!double.TryParse(_weightBox.Text.Trim(), out var weight) || weight < 0)
            {
                _errorLabel.Text = "Weight must be a non-negative number.";
                return;
            }

            ResultWeight = weight;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
