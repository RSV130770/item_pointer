using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class LoadTaskDialog : Form
    {
        private readonly FlowLayoutPanel _fileList = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Label _countDisplay = new() { Font = new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        private readonly Button _loadButton = new() { Text = "Load", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold), Enabled = false };

        private int _repeatCount = 1;
        private Button _selectedButton;

        public string SelectedFilePath { get; private set; }
        public int RepeatCount => _repeatCount;

        public LoadTaskDialog(List<FileInfo> availableFiles)
        {
            Text = "Select task";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(availableFiles);
        }

        private void BuildLayout(List<FileInfo> availableFiles)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(30) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

            var titleLabel = new Label
            {
                Text = "Select a task to load",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(titleLabel, 0, 0);

            foreach (var file in availableFiles)
            {
                var button = new Button
                {
                    Text = Path.GetFileNameWithoutExtension(file.Name),
                    Width = 700,
                    Height = 60,
                    Margin = new Padding(5),
                    Font = new Font(FontFamily.GenericSansSerif, 16),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Tag = file.FullName
                };
                button.Click += (s, e) => SelectFile(button);
                _fileList.Controls.Add(button);
            }
            root.Controls.Add(_fileList, 0, 1);

            var repeatPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            repeatPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            repeatPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            repeatPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            repeatPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            var repeatLabel = new Label
            {
                Text = "Repeat count:",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 16),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var minusButton = new Button { Text = "-", Dock = DockStyle.Fill, Margin = new Padding(5), Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold) };
            var plusButton = new Button { Text = "+", Dock = DockStyle.Fill, Margin = new Padding(5), Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold) };
            minusButton.Click += (s, e) => ChangeRepeatCount(-1);
            plusButton.Click += (s, e) => ChangeRepeatCount(1);

            repeatPanel.Controls.Add(repeatLabel, 0, 0);
            repeatPanel.Controls.Add(minusButton, 1, 0);
            repeatPanel.Controls.Add(_countDisplay, 2, 0);
            repeatPanel.Controls.Add(plusButton, 3, 0);
            root.Controls.Add(repeatPanel, 0, 2);
            UpdateCountDisplay();

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(15), Font = new Font(FontFamily.GenericSansSerif, 18) };
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _loadButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            buttonRow.Controls.Add(_loadButton, 0, 0);
            buttonRow.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttonRow, 0, 3);

            Controls.Add(root);
        }

        private void SelectFile(Button button)
        {
            if (_selectedButton != null)
            {
                _selectedButton.BackColor = SystemColors.Control;
                _selectedButton.ForeColor = SystemColors.ControlText;
            }
            _selectedButton = button;
            button.BackColor = Color.LimeGreen;
            button.ForeColor = Color.Black;

            SelectedFilePath = (string)button.Tag;
            _loadButton.Enabled = true;
        }

        private void ChangeRepeatCount(int delta)
        {
            _repeatCount = System.Math.Max(1, System.Math.Min(999, _repeatCount + delta));
            UpdateCountDisplay();
        }

        private void UpdateCountDisplay()
        {
            _countDisplay.Text = _repeatCount.ToString();
        }
    }
}
