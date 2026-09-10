using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class SkipReasonForm : Form
    {
        private readonly TextBox _customReasonBox = new() { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 14) };

        public string SelectedReason { get; private set; } = "";
        /// <summary>True only for the LED-contact-issue preset: the caller should log this but keep the operator on the current item, since weight verification is still needed - not a true skip.</summary>
        public bool KeepOnCurrentItem { get; private set; }

        private static readonly string[] PresetReasons =
        {
            "Scale error / unstable",
            "Item damaged",
            "Wrong item in bin",
            "Item missing",
            "LED no contact (keep item, still weigh)",
            "Other (type below)"
        };

        public SkipReasonForm(string itemName)
        {
            Text = "Skip reason";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(itemName);
        }

        private void BuildLayout(string itemName)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

            var titleLabel = new Label
            {
                Text = $"Why skip \"{itemName}\"?",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(titleLabel, 0, 0);

            var columns = 2;
            var rowCount = (int)System.Math.Ceiling(PresetReasons.Length / (double)columns);
            var presetPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rowCount };
            for (int c = 0; c < columns; c++)
                presetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int i = 0; i < rowCount; i++)
                presetPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));

            for (int i = 0; i < PresetReasons.Length; i++)
            {
                var reason = PresetReasons[i];
                var button = new Button
                {
                    Text = reason,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(15, 8, 15, 8),
                    Font = new Font(FontFamily.GenericSansSerif, 15)
                };
                button.Click += (s, e) => OnPresetClicked(reason);
                presetPanel.Controls.Add(button, i % columns, i / columns);
            }
            root.Controls.Add(presetPanel, 0, 1);

            var customLabel = new Label
            {
                Text = "Custom reason:",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 12),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0)
            };
            var customRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            customRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            customRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            customRow.Controls.Add(customLabel, 0, 0);
            customRow.Controls.Add(_customReasonBox, 1, 0);
            root.Controls.Add(customRow, 0, 2);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var confirmButton = new Button { Text = "Confirm skip", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold) };
            var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 16) };
            confirmButton.Click += (s, e) => OnConfirmClicked();
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonRow.Controls.Add(confirmButton, 0, 0);
            buttonRow.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 3);

            Controls.Add(root);
        }

        private void OnPresetClicked(string reason)
        {
            if (reason.StartsWith("Other"))
            {
                _customReasonBox.Focus();
                return;
            }
            SelectedReason = reason;
            KeepOnCurrentItem = reason.StartsWith("LED no contact");
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnConfirmClicked()
        {
            var custom = _customReasonBox.Text.Trim();
            if (!string.IsNullOrEmpty(custom))
            {
                SelectedReason = custom;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(this, "Pick a reason or type a custom one.", "Reason required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
