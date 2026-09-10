using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class TaskCompleteDialog : Form
    {
        public TaskCompleteDialog(string taskName, int itemCount)
        {
            Text = "Task complete";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(taskName, itemCount);
        }

        private void BuildLayout(string taskName, int itemCount)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            var titleLabel = new Label
            {
                Text = "Task complete!",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 32, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DarkGreen
            };
            root.Controls.Add(titleLabel, 0, 0);

            var summaryLabel = new Label
            {
                Text = $"\"{taskName}\" - {itemCount} item(s) processed.",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };
            root.Controls.Add(summaryLabel, 0, 1);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(60) };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var repeatButton = new Button
            {
                Text = "Repeat task",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold)
            };
            var doneButton = new Button
            {
                Text = "Done",
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold)
            };
            repeatButton.Click += (s, e) => { DialogResult = DialogResult.Retry; Close(); };
            doneButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            buttonRow.Controls.Add(repeatButton, 0, 0);
            buttonRow.Controls.Add(doneButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 2);

            Controls.Add(root);
        }
    }
}
