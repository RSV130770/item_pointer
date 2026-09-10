using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class RelayControlForm : Form
    {
        private readonly TextBox _portNameBox = new() { Text = "COM3", Width = 80 };
        private readonly Button _connectButton = new() { Text = "Connect", Width = 90 };
        private readonly Button _allOffButton = new() { Text = "All off", Width = 90 };
        private readonly TextBox _searchBox = new() { Width = 60, MaxLength = IndexRegistry.MaxLabelLength };
        private readonly Button _searchButton = new() { Text = "Find label", Width = 90 };
        private readonly Button _openTaskButton = new() { Text = "Open task window", Width = 140 };
        private readonly Button _openConfigButton = new() { Text = "Config editor", Width = 120 };
        private readonly TextBox _logBox = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Dock = DockStyle.Bottom,
            Height = 140
        };

        private const int CellWidth = 60;
        private const int CellHeight = 44;
        private readonly TableLayoutPanel _gridRoot = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 50, 0, 0) };
        private readonly Panel _colHeaderClip = new() { Dock = DockStyle.Fill };
        private readonly Panel _colHeaderContent = new();
        private readonly Panel _rowHeaderClip = new() { Dock = DockStyle.Fill };
        private readonly Panel _rowHeaderContent = new();
        private readonly Panel _ledBody = new() { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly System.Windows.Forms.Timer _headerSyncTimer = new() { Interval = 30 };

        private LedMatrixConfig _config;
        private IRelayCommutator _hardware = new MockRelayCommutator();
        private LedMatrix _matrix;
        private Button[,] _ledButtons;
        private Button[] _rowHeaderButtons;
        private Button[] _colHeaderButtons;
        private IndexRegistry _labels;

        private bool _usesExternalHardware;
        private bool _pickerMode;
        private string _baseTitle = "LED matrix manual control";

        /// <summary>Set when this window is used as a picker (see the picker-mode constructor) and a labeled cell was clicked.</summary>
        public string SelectedLabel { get; private set; }

        public RelayControlForm()
        {
            Text = "LED matrix manual control";
            Width = 1000;
            Height = 700;

            _config = LoadConfigOrDefault();
            _portNameBox.Text = _config.SerialPort.PortName;
            _matrix = new LedMatrix(_hardware, _config);
            _labels = new IndexRegistry(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.csv"));

            BuildTopBar();
            BuildGrid(_config.Rows, _config.TotalColumns > 0 ? _config.TotalColumns : 32);

            Controls.Add(_gridRoot);
            Controls.Add(_logBox);
            Log($"Index registry file: {_labels.FilePath}");
        }

        /// <summary>
        /// Reuses an already-open relay connection (e.g. the one TaskForm
        /// is holding) instead of opening the COM port a second time, which
        /// would fail since only one connection can hold a serial port at
        /// once. Also reuses the SAME IndexRegistry instance TaskForm has
        /// (rather than creating a second one) - two independent instances
        /// pointed at what should be the same file can end up with stale,
        /// out-of-sync in-memory copies of each other's edits, which is
        /// exactly what caused false "duplicate label" rejections here.
        /// The port/connect controls are hidden since there's nothing for
        /// them to do here.
        /// </summary>
        public RelayControlForm(LedMatrixConfig config, LedMatrix existingMatrix, IndexRegistry sharedLabels)
        {
            Text = "LED matrix manual control (shared connection)";
            _baseTitle = "LED matrix manual control (shared connection)";
            Width = 1000;
            Height = 700;

            _config = config;
            _matrix = existingMatrix;
            _labels = sharedLabels;
            _usesExternalHardware = true;

            BuildTopBar();
            BuildGrid(_config.Rows, _config.TotalColumns > 0 ? _config.TotalColumns : 32);

            Controls.Add(_gridRoot);
            Controls.Add(_logBox);

            Log("Using the relay connection and index registry already open elsewhere in the app.");
            Log($"Index registry file: {_labels.FilePath}");
        }

        /// <summary>
        /// Picker mode: intended to be opened with ShowDialog() from
        /// another dialog (nested modal - fully supported by WinForms).
        /// Clicking a cell that already has a label still switches the
        /// relay as normal (useful physical confirmation of which index
        /// you're picking), and additionally sets SelectedLabel and closes
        /// with DialogResult.OK. Clicking an unlabeled cell just switches
        /// the relay, same as always - there's nothing to pick there.
        /// </summary>
        public RelayControlForm(LedMatrixConfig config, LedMatrix existingMatrix, IndexRegistry sharedLabels, bool pickerMode)
            : this(config, existingMatrix, sharedLabels)
        {
            _pickerMode = pickerMode;
            if (pickerMode)
            {
                Text = "Pick an index - click a labeled LED";
                _baseTitle = Text;
            }
        }

        private LedMatrixConfig LoadConfigOrDefault()
        {
            const string path = "config.json";
            try
            {
                if (File.Exists(path))
                {
                    Log($"Loaded config from {Path.GetFullPath(path)}");
                    return LedMatrixConfig.LoadFromFile(path);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load {path}: {ex.Message}. Using built-in default.");
            }

            Log($"{path} not found next to the executable. Using built-in default (16x32, addr 2/3).");
            return new LedMatrixConfig
            {
                RowCommutator = new RowCommutatorConfig { Address = 1 },
                ColumnCommutators = new List<ColumnCommutatorConfig>
                {
                    new() { Address = 2, StartColumn = 0 },
                    new() { Address = 3, StartColumn = 16 }
                },
                Rows = 16,
                ColumnsPerCommutator = 16,
                AllChannelsValue = 255
            };
        }

        private void BuildTopBar()
        {
            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8)
            };
            var portLabel = new Label { Text = "Port:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) };
            topBar.Controls.Add(portLabel);
            topBar.Controls.Add(_portNameBox);
            topBar.Controls.Add(_connectButton);
            topBar.Controls.Add(new Label { Text = "Label:", AutoSize = true, Margin = new Padding(20, 8, 4, 0) });
            topBar.Controls.Add(_searchBox);
            topBar.Controls.Add(_searchButton);
            topBar.Controls.Add(_openTaskButton);
            topBar.Controls.Add(_openConfigButton);

            if (_usesExternalHardware)
            {
                portLabel.Visible = false;
                _portNameBox.Visible = false;
                _connectButton.Visible = false;
                _openTaskButton.Visible = false; // avoid opening a second TaskForm that would fight over the same port
            }

            _searchButton.Click += async (s, e) => await SetLedByLabel(_searchBox.Text);
            _openTaskButton.Click += (s, e) =>
            {
                // Technician bypass: opens the task window directly without
                // going through the PIN login screen, for testing.
                var technicianUser = new AppUser { Id = "tech", Name = "Technician (bypass)" };
                new TaskForm(_config, technicianUser).Show();
            };
            _openConfigButton.Click += (s, e) => new ConfigEditorForm("config.json").Show();

            _connectButton.Click += OnConnectClicked;

            Controls.Add(topBar);
        }

        private void BuildGrid(int rows, int cols)
        {
            _ledButtons = new Button[rows, cols];
            _rowHeaderButtons = new Button[rows];
            _colHeaderButtons = new Button[cols];

            // 2x2 layout: fixed corner, column-header strip (top-right),
            // row-header strip (bottom-left), and the scrollable LED body
            // (bottom-right). Only the body actually scrolls - the two
            // header strips have their own content panels manually
            // repositioned to track the body's scroll offset, so they
            // never scroll out of view themselves.
            _gridRoot.ColumnCount = 2;
            _gridRoot.RowCount = 2;
            _gridRoot.ColumnStyles.Clear();
            _gridRoot.RowStyles.Clear();
            _gridRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CellWidth));
            _gridRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _gridRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, CellHeight));
            _gridRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _allOffButton.Dock = DockStyle.Fill;
            _allOffButton.Font = new Font(Font.FontFamily, 7);
            _allOffButton.Click -= OnAllOffClicked;
            _allOffButton.Click += OnAllOffClicked;
            _gridRoot.Controls.Add(_allOffButton, 0, 0);

            _colHeaderClip.Controls.Clear();
            _colHeaderContent.Controls.Clear();
            _colHeaderContent.Size = new Size(cols * CellWidth, CellHeight);
            _colHeaderContent.Location = new Point(0, 0);
            _colHeaderClip.Controls.Add(_colHeaderContent);
            _gridRoot.Controls.Add(_colHeaderClip, 1, 0);

            _rowHeaderClip.Controls.Clear();
            _rowHeaderContent.Controls.Clear();
            _rowHeaderContent.Size = new Size(CellWidth, rows * CellHeight);
            _rowHeaderContent.Location = new Point(0, 0);
            _rowHeaderClip.Controls.Add(_rowHeaderContent);
            _gridRoot.Controls.Add(_rowHeaderClip, 0, 1);

            _ledBody.Controls.Clear();
            _gridRoot.Controls.Add(_ledBody, 1, 1);

            for (int col = 0; col < cols; col++)
            {
                int capturedCol = col;
                var colHeader = new Button
                {
                    Text = col.ToString(),
                    Location = new Point(col * CellWidth + 1, 1),
                    Size = new Size(CellWidth - 2, CellHeight - 2),
                    Font = new Font(Font.FontFamily, 7)
                };
                colHeader.Click += async (s, e) => await RunHardwareAction(() => _matrix.SetFullColumn(capturedCol), $"full column {capturedCol}", HighlightColumn(capturedCol));
                _colHeaderButtons[col] = colHeader;
                _colHeaderContent.Controls.Add(colHeader);
            }

            for (int row = 0; row < rows; row++)
            {
                int capturedRow = row;
                var rowHeader = new Button
                {
                    Text = row.ToString(),
                    Location = new Point(1, row * CellHeight + 1),
                    Size = new Size(CellWidth - 2, CellHeight - 2),
                    Font = new Font(Font.FontFamily, 7)
                };
                rowHeader.Click += async (s, e) => await RunHardwareAction(() => _matrix.SetFullRow(capturedRow), $"full row {capturedRow}", HighlightRow(capturedRow));
                _rowHeaderButtons[row] = rowHeader;
                _rowHeaderContent.Controls.Add(rowHeader);

                for (int col = 0; col < cols; col++)
                {
                    int capturedCol = col;
                    var ledButton = new Button
                    {
                        Location = new Point(col * CellWidth + 1, row * CellHeight + 1),
                        Size = new Size(CellWidth - 2, CellHeight - 2),
                        BackColor = SystemColors.Control,
                        Text = _labels.Get(capturedRow, capturedCol),
                        Font = new Font(Font.FontFamily, 8)
                    };
                    ledButton.Click += async (s, e) =>
                    {
                        await RunHardwareAction(() => _matrix.SetLed(capturedRow, capturedCol), $"LED ({capturedRow},{capturedCol})", HighlightSingle(capturedRow, capturedCol));

                        if (_pickerMode)
                        {
                            var label = _labels.Get(capturedRow, capturedCol);
                            if (!string.IsNullOrEmpty(label))
                            {
                                SelectedLabel = label;
                                DialogResult = DialogResult.OK;
                                Close();
                            }
                        }
                    };
                    ledButton.MouseUp += async (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            await RunHardwareAction(() => _matrix.SetLed(capturedRow, capturedCol), $"LED ({capturedRow},{capturedCol})", HighlightSingle(capturedRow, capturedCol));
                            EditLabel(capturedRow, capturedCol, ledButton);
                        }
                    };
                    _ledButtons[row, col] = ledButton;
                    _ledBody.Controls.Add(ledButton);
                }
            }

            // Event-based sync (Scroll/MouseWheel) turned out unreliable
            // for this Panel - neither fired consistently enough to keep
            // the header strips tracking the body, including via direct
            // scrollbar drag. A short polling timer sidesteps the whole
            // "which event actually fires" question entirely: it just
            // checks the real scroll position often enough that any drift
            // is imperceptible, regardless of how the user scrolled.
            _headerSyncTimer.Tick -= HeaderSyncTimerTick;
            _headerSyncTimer.Tick += HeaderSyncTimerTick;
            _headerSyncTimer.Start();
        }

        private void HeaderSyncTimerTick(object sender, EventArgs e)
        {
            SyncHeadersToBodyScroll();
        }

        private void SyncHeadersToBodyScroll()
        {
            var newLeft = _ledBody.AutoScrollPosition.X;
            var newTop = _ledBody.AutoScrollPosition.Y;
            if (_colHeaderContent.Left != newLeft)
                _colHeaderContent.Left = newLeft;
            if (_rowHeaderContent.Top != newTop)
                _rowHeaderContent.Top = newTop;
        }

        /// <summary>
        /// Finds the cell with the given label (case-insensitive) and turns
        /// on that LED, same as clicking it directly. Returns false if no
        /// cell currently has that label.
        /// </summary>
        public async Task<bool> SetLedByLabel(string label)
        {
            if (!_labels.TryFind(label, out int row, out int col))
            {
                Log($"Label \"{label}\" not found");
                return false;
            }

            await RunHardwareAction(() => _matrix.SetLed(row, col), $"LED by label \"{label}\" -> ({row},{col})", HighlightSingle(row, col));
            return true;
        }

        private void EditLabel(int row, int col, Button button)
        {
            var currentLabel = _labels.Get(row, col);

            using var prompt = new LabelPromptForm($"Label for ({row},{col})", currentLabel);
            if (prompt.ShowDialog(this) != DialogResult.OK)
                return;

            if (string.IsNullOrWhiteSpace(prompt.ResultLabel))
            {
                if (!string.IsNullOrEmpty(currentLabel))
                {
                    _labels.Delete(currentLabel);
                    button.Text = "";
                    Log($"Removed index \"{currentLabel}\" from ({row},{col}) (pending - close this window to save or discard)");
                }
                UpdateTitleBar();
                return;
            }

            // Preserve whatever weight already exists for this label (set
            // via TaskForm's "Update weight" button); brand new labels
            // start at 0 until a weight is set from there.
            var existingWeight = _labels.GetEntry(prompt.ResultLabel)?.Weight ?? 0;

            if (_labels.Set(prompt.ResultLabel, existingWeight, row, col, out var rejectionReason))
            {
                button.Text = _labels.Get(row, col);
                Log($"Index \"{button.Text}\" set for ({row},{col}) (pending - close this window to save or discard)");
                UpdateTitleBar();
            }
            else
            {
                MessageBox.Show(this, rejectionReason, "Duplicate label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log($"Rejected label \"{prompt.ResultLabel}\" for ({row},{col}): {rejectionReason}");
            }
        }

        private Action HighlightSingle(int row, int col)
        {
            return () =>
            {
                ClearHighlights();
                _ledButtons[row, col].BackColor = Color.LimeGreen;
            };
        }

        private Action HighlightRow(int row)
        {
            return () =>
            {
                ClearHighlights();
                for (int c = 0; c < _ledButtons.GetLength(1); c++)
                    _ledButtons[row, c].BackColor = Color.LimeGreen;
            };
        }

        private Action HighlightColumn(int col)
        {
            return () =>
            {
                ClearHighlights();
                for (int r = 0; r < _ledButtons.GetLength(0); r++)
                    _ledButtons[r, col].BackColor = Color.LimeGreen;
            };
        }

        private void ClearHighlights()
        {
            foreach (var btn in _ledButtons)
                btn.BackColor = SystemColors.Control;
        }

        private async void OnAllOffClicked(object sender, EventArgs e)
        {
            await RunHardwareAction(() => _matrix.AllOff(), "all off", ClearHighlights);
        }

        private void OnConnectClicked(object sender, EventArgs e)
        {
            try
            {
                if (_hardware is IDisposable disposable)
                    disposable.Dispose();

                _config.SerialPort.PortName = _portNameBox.Text;
                var modbus = new ModbusRelayCommutator(_config.SerialPort);
                modbus.Trace += line => { if (IsHandleCreated) BeginInvoke((Action)(() => Log(line))); };
                _hardware = modbus;

                _matrix = new LedMatrix(_hardware, _config);
                Log($"Connected ({_portNameBox.Text})");
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
            }
        }

        private async Task RunHardwareAction(Action action, string description, Action onSuccessUiUpdate)
        {
            SetInputsEnabled(false);
            try
            {
                await Task.Run(action);
                onSuccessUiUpdate();
                Log($"OK: {description}");
            }
            catch (Exception ex)
            {
                Log($"FAILED: {description} - {ex.Message}");
            }
            finally
            {
                SetInputsEnabled(true);
            }
        }

        private void SetInputsEnabled(bool enabled)
        {
            _gridRoot.Enabled = enabled;
            _allOffButton.Enabled = enabled;
            _connectButton.Enabled = enabled;
        }

        private void UpdateTitleBar()
        {
            Text = _labels.HasUnsavedChanges ? $"{_baseTitle} - unsaved changes*" : _baseTitle;
        }

        private void Log(string message)
        {
            _logBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_labels != null && _labels.HasUnsavedChanges)
            {
                var result = MessageBox.Show(this,
                    "You have unsaved label/weight changes. Save them to index.csv before closing?",
                    "Unsaved changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                switch (result)
                {
                    case DialogResult.Yes:
                        _labels.SaveChanges();
                        Log("Saved pending index.csv changes on close.");
                        break;
                    case DialogResult.No:
                        _labels.DiscardChanges();
                        Log("Discarded pending index.csv changes on close.");
                        break;
                    case DialogResult.Cancel:
                        e.Cancel = true; // stay open, let the user keep editing
                        break;
                }
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _headerSyncTimer.Stop();
            _headerSyncTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
