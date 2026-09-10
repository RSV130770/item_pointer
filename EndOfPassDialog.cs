using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class EndOfPassDialog : Form
    {
        public enum Choice { Continue, PickAnotherList, Cancel }

        public Choice Result { get; private set; } = Choice.Cancel;

        public EndOfPassDialog(string taskName, int currentPass, int totalPasses, bool morePassesRemain)
        {
            Text = "Pass complete";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(taskName, currentPass, totalPasses, morePassesRemain);
        }

        private void BuildLayout(string taskName, int currentPass, int totalPasses, bool morePassesRemain)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            var titleLabel = new Label
            {
                Text = morePassesRemain ? "Pass complete!" : "Task complete!",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 32, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DarkGreen
            };
            root.Controls.Add(titleLabel, 0, 0);

            var summaryLabel = new Label
            {
                Text = totalPasses > 1
                    ? $"\"{taskName}\" - finished pass {currentPass} of {totalPasses}."
                    : $"\"{taskName}\" - finished.",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(summaryLabel, 0, 1);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(60) };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var primaryButton = new Button
            {
                Text = morePassesRemain ? $"Continue (pass {currentPass + 1} of {totalPasses})" : "Pick another list",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold)
            };
            var cancelButton = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold),
                BackColor = Color.MistyRose
            };

            primaryButton.Click += (s, e) =>
            {
                Result = morePassesRemain ? Choice.Continue : Choice.PickAnotherList;
                Close();
            };
            cancelButton.Click += (s, e) =>
            {
                Result = Choice.Cancel;
                Close();
            };

            buttonRow.Controls.Add(primaryButton, 0, 0);
            buttonRow.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 2);

            Controls.Add(root);
        }
    }
}
