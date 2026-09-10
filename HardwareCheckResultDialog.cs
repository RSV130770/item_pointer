using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class HardwareCheckResultDialog : Form
    {
        public HardwareCheckResultDialog(DiagnosticsReport report)
        {
            Text = "Hardware recheck result";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout(report);
        }

        private void BuildLayout(DiagnosticsReport report)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(30) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

            var overallOk = report != null && report.AllOk;
            var titleLabel = new Label
            {
                Text = report == null ? "No diagnostics available" : (overallOk ? "All checks passed" : "Some checks failed"),
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 26, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = overallOk ? Color.DarkGreen : Color.DarkRed
            };
            root.Controls.Add(titleLabel, 0, 0);

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            if (report != null)
            {
                foreach (var result in report.Results)
                    list.Controls.Add(BuildResultRow(result));
            }
            root.Controls.Add(list, 0, 1);

            var closeButton = new Button
            {
                Text = "Close",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold)
            };
            closeButton.Click += (s, e) => Close();
            var buttonWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(200, 10, 200, 10) };
            buttonWrap.Controls.Add(closeButton);
            root.Controls.Add(buttonWrap, 0, 2);

            Controls.Add(root);
        }

        private Panel BuildResultRow(DiagnosticResult result)
        {
            var row = new Panel { Width = 950, Height = 70, Margin = new Padding(0, 5, 0, 5), BorderStyle = BorderStyle.FixedSingle };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var badge = new Label
            {
                Text = result.Ok ? "OK" : "FAIL",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = result.Ok ? Color.SeaGreen : Color.Firebrick
            };
            layout.Controls.Add(badge, 0, 0);

            var nameLabel = new Label
            {
                Text = result.Name,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            layout.Controls.Add(nameLabel, 1, 0);

            var messageLabel = new Label
            {
                Text = result.Message,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 11),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoEllipsis = true
            };
            layout.Controls.Add(messageLabel, 2, 0);

            row.Controls.Add(layout);
            return row;
        }
    }
}
