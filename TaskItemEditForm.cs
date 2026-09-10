using System;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class TaskItemEditForm : Form
    {
        private readonly TextBox _indexBox = new() { Font = new Font(FontFamily.GenericSansSerif, 16), Dock = DockStyle.Fill };
        private readonly Button _pickButton = new() { Text = "Pick from LED matrix", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly TextBox _nameBox = new() { Font = new Font(FontFamily.GenericSansSerif, 16), Dock = DockStyle.Fill };
        private readonly TextBox _weightBox = new() { Font = new Font(FontFamily.GenericSansSerif, 16), Dock = DockStyle.Fill };
        private readonly Button _useScaleButton = new() { Text = "Use current scale reading", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly TextBox _quantityBox = new() { Font = new Font(FontFamily.GenericSansSerif, 16), Dock = DockStyle.Fill };
        private readonly TextBox _lengthBox = new() { Font = new Font(FontFamily.GenericSansSerif, 16), Dock = DockStyle.Fill };
        private readonly Label _errorLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 12), ForeColor = Color.Red, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _hintLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 11), ForeColor = Color.DimGray, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _liveWeightLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkGreen };
        private readonly System.Windows.Forms.Timer _liveWeightTimer = new() { Interval = 300 };

        private readonly Func<double> _getCurrentScaleWeight;
        private readonly Func<string> _pickLabelFromMatrix;
        private bool _userEditedWeight;
        private bool _settingWeightProgrammatically;

        public TaskItem Result { get; private set; }
        /// <summary>The TRUE weight (matching live scale units) typed/suggested in the dialog. The caller converts this to whatever internal representation it needs (e.g. dividing by a mass coefficient) - this dialog only ever deals in true/measured grams, same units as the live scale readout, never a coefficient-adjusted value.</summary>
        public double EnteredWeight { get; private set; }

        /// <summary>
        /// Used for both Add and Edit - label and weight are always
        /// editable here now (previously Edit locked both).
        /// getCurrentScaleWeight, if provided, is polled continuously
        /// (every 300ms) while this dialog is open and shown live in a
        /// label - not just read once at dialog-open time, which would go
        /// stale if the operator places the item on the scale *after*
        /// opening this dialog (exactly the scenario that made "Use
        /// current scale reading" appear to always return zero before).
        /// displayWeight, when editing an existing item, must already be
        /// in TRUE weight units (same as the live scale) - the caller is
        /// responsible for that conversion (e.g. existing.Weight *
        /// massCoefficient), since this dialog has no idea a coefficient
        /// exists. Passing a raw nominal value here would silently show
        /// the wrong number and, if saved unchanged, double-apply the
        /// coefficient on the next load.
        /// The caller is responsible for deciding what EnteredWeight
        /// actually means once the label is known (e.g. an existing
        /// index's registered weight should normally win over whatever's
        /// typed here - see TaskForm.AddItem/EditCurrentItem).
        /// pickLabelFromMatrix, if provided, wires up a button that opens
        /// the LED matrix as a nested modal picker.
        /// </summary>
        public TaskItemEditForm(TaskItem existing, bool isNewItem, double displayWeight = 0, Func<double> getCurrentScaleWeight = null, Func<string> pickLabelFromMatrix = null)
        {
            _getCurrentScaleWeight = getCurrentScaleWeight;
            _pickLabelFromMatrix = pickLabelFromMatrix;

            Text = isNewItem ? "Add item" : "Edit item";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout();

            if (existing != null)
            {
                _indexBox.Text = existing.Index;
                _nameBox.Text = existing.Name;
                _weightBox.Text = displayWeight > 0 ? displayWeight.ToString("0.0") : "";
                _quantityBox.Text = existing.Quantity.ToString();
                _lengthBox.Text = existing.LengthMm?.ToString() ?? "";

                // Editing an existing item with a real registered weight:
                // protect it from being immediately overwritten by the
                // first auto-follow tick. Click "Use current scale
                // reading" to explicitly switch to live tracking.
                if (displayWeight > 0)
                    _userEditedWeight = true;
            }

            if (_getCurrentScaleWeight != null)
            {
                _liveWeightTimer.Tick += (s, e) => UpdateLiveWeightLabel();
                _liveWeightTimer.Start();
                UpdateLiveWeightLabel();
            }
            FormClosed += (s, e) => _liveWeightTimer.Stop();

            _weightBox.TextChanged += (s, e) =>
            {
                if (!_settingWeightProgrammatically)
                    _userEditedWeight = true;
            };
        }

        private void UpdateLiveWeightLabel()
        {
            var current = _getCurrentScaleWeight();
            _liveWeightLabel.Text = $"Current scale reading: {current:0.0} g";

            // Auto-follow the live reading by default so the weight field
            // is always populated with something sensible - only stop if
            // the operator has deliberately typed a different value
            // themselves. This closes the gap where a live label alone
            // was purely informational and never actually fed the saved
            // weight unless "Use current scale reading" was clicked.
            if (!_userEditedWeight)
            {
                _settingWeightProgrammatically = true;
                _weightBox.Text = current.ToString("0.###");
                _settingWeightProgrammatically = false;
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 9, Padding = new Padding(30) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

            root.Controls.Add(new Label { Text = "Label (index):", Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            root.Controls.Add(_indexBox, 1, 0);
            _pickButton.Dock = DockStyle.Fill;
            _pickButton.Margin = new Padding(5);
            _pickButton.Click += (s, e) => OnPickFromMatrixClicked();
            _pickButton.Visible = _pickLabelFromMatrix != null;
            root.Controls.Add(_pickButton, 2, 0);

            root.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            root.Controls.Add(_nameBox, 1, 1);
            root.SetColumnSpan(_nameBox, 2);

            _liveWeightLabel.Visible = _getCurrentScaleWeight != null;
            root.Controls.Add(_liveWeightLabel, 0, 2);
            root.SetColumnSpan(_liveWeightLabel, 3);

            root.Controls.Add(new Label { Text = "Unit weight (g):", Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14), TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            root.Controls.Add(_weightBox, 1, 3);
            _useScaleButton.Dock = DockStyle.Fill;
            _useScaleButton.Margin = new Padding(5);
            _useScaleButton.Click += (s, e) => { _userEditedWeight = false; if (_getCurrentScaleWeight != null) UpdateLiveWeightLabel(); };
            _useScaleButton.Visible = _getCurrentScaleWeight != null;
            root.Controls.Add(_useScaleButton, 2, 3);

            root.Controls.Add(new Label { Text = "Quantity:", Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14), TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            root.Controls.Add(_quantityBox, 1, 4);
            root.SetColumnSpan(_quantityBox, 2);

            root.Controls.Add(new Label { Text = "Length (mm, optional):", Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14), TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            root.Controls.Add(_lengthBox, 1, 5);
            root.SetColumnSpan(_lengthBox, 2);

            _hintLabel.Text = "Note: if this label already exists elsewhere, its established weight is used instead of what's typed above - edit an existing index's weight via \"Update weight\" on the main screen.";
            root.Controls.Add(_hintLabel, 0, 6);
            root.SetColumnSpan(_hintLabel, 3);

            root.Controls.Add(_errorLabel, 0, 7);
            root.SetColumnSpan(_errorLabel, 3);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var saveButton = new Button { Text = "Save", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold) };
            var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 18) };
            saveButton.Click += (s, e) => OnSaveClicked();
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonRow.Controls.Add(saveButton, 0, 0);
            buttonRow.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 8);
            root.SetColumnSpan(buttonRow, 3);

            Controls.Add(root);
        }

        private void OnPickFromMatrixClicked()
        {
            var picked = _pickLabelFromMatrix?.Invoke();
            if (!string.IsNullOrEmpty(picked))
                _indexBox.Text = picked;
        }

        private void OnSaveClicked()
        {
            var index = _indexBox.Text.Trim();
            var name = _nameBox.Text.Trim();

            if (string.IsNullOrEmpty(index)) { ShowError("Label is required."); return; }
            if (string.IsNullOrEmpty(name)) { ShowError("Name is required."); return; }
            if (!int.TryParse(_quantityBox.Text.Trim(), out var quantity) || quantity < 0) { ShowError("Quantity must be a non-negative whole number."); return; }

            double? length = null;
            var lengthText = _lengthBox.Text.Trim();
            if (!string.IsNullOrEmpty(lengthText))
            {
                if (!double.TryParse(lengthText, out var lengthValue) || lengthValue < 0) { ShowError("Length must be a non-negative number, or left blank."); return; }
                length = lengthValue;
            }

            var weightText = _weightBox.Text.Trim();
            if (!string.IsNullOrEmpty(weightText))
            {
                if (!double.TryParse(weightText, out var w) || w < 0) { ShowError("Weight must be a non-negative number, or left blank."); return; }
                EnteredWeight = w;
            }

            Result = new TaskItem { Index = index, Name = name, Quantity = quantity, LengthMm = length };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowError(string message)
        {
            _errorLabel.Text = message;
        }
    }
}
