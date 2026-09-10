using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class LoginForm : Form
    {
        private readonly LedMatrixConfig _config;
        private readonly UserStore _users;

        private readonly Label _pinDisplay = new()
        {
            Text = "",
            Font = new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 80
        };
        private readonly Label _statusLabel = new()
        {
            Text = "Enter PIN",
            Font = new Font(FontFamily.GenericSansSerif, 14),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = Color.DimGray
        };

        private string _pin = "";
        private const int MaxPinLength = 8;

        public LoginForm(LedMatrixConfig config)
        {
            _config = config;
            _users = new UserStore(ResolveUsersPath(config));

            Text = "Sign in";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            BuildLayout();
        }

        private static string ResolveUsersPath(LedMatrixConfig config)
        {
            var path = config.UsersFilePath;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return path;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _statusLabel.Dock = DockStyle.Fill;
            _pinDisplay.Dock = DockStyle.Fill;
            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(_pinDisplay, 0, 1);

            var keypad = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4
            };
            for (int i = 0; i < 3; i++) keypad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int i = 0; i < 4; i++) keypad.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            string[] labels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "OK" };
            foreach (var label in labels)
            {
                var button = new Button
                {
                    Text = label,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(10),
                    Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold)
                };
                button.Click += (s, e) => OnKeypadPress(label);
                int index = Array.IndexOf(labels, label);
                keypad.Controls.Add(button, index % 3, index / 3);
            }

            root.Controls.Add(keypad, 0, 2);
            Controls.Add(root);
        }

        private void OnKeypadPress(string key)
        {
            switch (key)
            {
                case "CLR":
                    _pin = "";
                    break;
                case "OK":
                    TrySubmit();
                    return;
                default:
                    if (_pin.Length < MaxPinLength)
                        _pin += key;
                    break;
            }
            _pinDisplay.Text = new string('*', _pin.Length);
        }

        private void TrySubmit()
        {
            if (_users.TryAuthenticate(_pin, out var user))
            {
                var taskForm = new TaskForm(_config, user);
                taskForm.LogoutRequested += () =>
                {
                    _pin = "";
                    _pinDisplay.Text = "";
                    _statusLabel.Text = "Enter PIN";
                    _statusLabel.ForeColor = Color.DimGray;
                    Show();
                };
                taskForm.FormClosed += (s, e) => Close();
                Hide();
                taskForm.Show();
            }
            else
            {
                _statusLabel.Text = "PIN not recognized, try again";
                _statusLabel.ForeColor = Color.Red;
                _pin = "";
                _pinDisplay.Text = "";
            }
        }
    }
}
