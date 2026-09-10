using System.Windows.Forms;

namespace LedMatrixControl
{
    public class LabelPromptForm : Form
    {
        private readonly TextBox _input;

        public string ResultLabel => _input.Text.Trim();

        public LabelPromptForm(string title, string currentLabel)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 240;
            Height = 130;

            var label = new Label { Text = "Label (max 5 chars):", AutoSize = true, Left = 12, Top = 12 };
            _input = new TextBox { Left = 12, Top = 34, Width = 200, MaxLength = IndexRegistry.MaxLabelLength, Text = currentLabel };
            var okButton = new Button { Text = "OK", Left = 50, Top = 65, Width = 60, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 120, Top = 65, Width = 60, DialogResult = DialogResult.Cancel };

            Controls.Add(label);
            Controls.Add(_input);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }
    }
}
