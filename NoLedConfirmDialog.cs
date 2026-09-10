using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class NoLedConfirmDialog : Form
    {
        public enum Choice { ContinueWeightOnly, SkipToNext }

        public Choice Result { get; private set; } = Choice.ContinueWeightOnly;

        public NoLedConfirmDialog(string indexLabel, string itemName)
        {
            Text = "No LED for this item";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(indexLabel, itemName);
        }

        private void BuildLayout(string indexLabel, string itemName)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            var titleLabel = new Label
            {
                Text = "No LED assigned",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 30, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DarkOrange
            };
            root.Controls.Add(titleLabel, 0, 0);

            var summaryLabel = new Label
            {
                Text = $"\"{indexLabel}\" ({itemName}) has no physical LED position on record.\nHow do you want to proceed?",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(summaryLabel, 0, 1);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(60) };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var continueButton = new Button
            {
                Text = "Continue (weight-only)",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold)
            };
            var skipButton = new Button
            {
                Text = "Skip to next item",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold),
                BackColor = Color.MistyRose
            };

            continueButton.Click += (s, e) => { Result = Choice.ContinueWeightOnly; Close(); };
            skipButton.Click += (s, e) => { Result = Choice.SkipToNext; Close(); };

            buttonRow.Controls.Add(continueButton, 0, 0);
            buttonRow.Controls.Add(skipButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 2);

            Controls.Add(root);
        }
    }
}
