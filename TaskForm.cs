using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace LedMatrixControl
{
    public class TaskForm : Form
    {
        private readonly LedMatrixConfig _config;
        private readonly AppUser _user;
        private readonly IndexRegistry _labels;
        private readonly OperationsLog _log;

        private IRelayCommutator _relay;
        private LedMatrix _matrix;
        private ScaleReader _scale;
        private readonly System.Windows.Forms.Timer _healthCheckTimer = new() { Interval = 8000 };
        private bool _lastHardwareOk = true;
        private bool _relayOk;
        private DiagnosticsReport _lastDiagnosticsReport;
        private bool _scaleOk;

        public event Action LogoutRequested;

        // -- UI, sized for a big touchscreen --
        private readonly Label _userLabel = new() { Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button _logoutButton = new() { Text = "Log out", Font = new Font(FontFamily.GenericSansSerif, 14) };
        private readonly Button _loadButton = new() { Text = "Load latest task", Font = new Font(FontFamily.GenericSansSerif, 14) };
        private readonly Button _importButton = new() { Text = "Open CSV file...", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly Label _taskNameLabel = new() { Text = "(no task loaded)", Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button _skipButton = new() { Text = "Skip item", Font = new Font(FontFamily.GenericSansSerif, 14), BackColor = Color.MistyRose };
        private readonly Button _addButton = new() { Text = "Add item", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly Button _editButton = new() { Text = "Edit item", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly Button _deleteButton = new() { Text = "Delete item", Font = new Font(FontFamily.GenericSansSerif, 12), BackColor = Color.MistyRose };
        private readonly Button _weightButton = new() { Text = "Update weight", Font = new Font(FontFamily.GenericSansSerif, 12) };

        private readonly Label _hardwareStatusLabel = new() { Text = "Checking hardware...", Font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button _recheckButton = new() { Text = "Recheck hardware", Font = new Font(FontFamily.GenericSansSerif, 12) };
        private readonly Button _openMatrixButton = new() { Text = "LED matrix (tech)", Font = new Font(FontFamily.GenericSansSerif, 12) };

        private readonly Label _currentItemLabel = new() { Text = "--", Font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label _scaleWeightCaption = new() { Text = "SCALE WEIGHT", Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray };
        private readonly Label _currentWeightLabel = new() { Text = "-- g", Font = new Font(FontFamily.GenericSansSerif, 52, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label _expectedWeightCaption = new() { Text = "EXPECTED WEIGHT", Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray };
        private readonly Label _expectedWeightValueLabel = new() { Text = "--", Font = new Font(FontFamily.GenericSansSerif, 52, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label _quantityCaption = new() { Text = "QUANTITY", Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray };
        private readonly Label _quantityValueLabel = new() { Text = "--", Font = new Font(FontFamily.GenericSansSerif, 52, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label _statusLabel = new() { Text = "", Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

        private readonly ListView _listView = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, GridLines = true, Font = new Font(FontFamily.GenericSansSerif, 11) };
        private readonly TextBox _logBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericSansSerif, 9) };

        private List<TaskItem> _items = new();
        private int _currentIndex = -1;
        private string _currentTaskFilePath;
        private int _totalRepeats = 1;
        private double _currentMassCoefficient = 1.0;
        private string _currentTaskTitle = "";
        private int _currentPass = 1;
        private bool _awaitingRemoval;
        private double _lastKnownWeight;

        public TaskForm(LedMatrixConfig config, AppUser user)
        {
            _config = config;
            _user = user;
            _labels = new IndexRegistry(ResolveNextToConfig("index.csv"));
            Log($"Index registry file: {_labels.FilePath}");
            _log = new OperationsLog(AppDomain.CurrentDomain.BaseDirectory, config.LogFolderPath);
            _log.NetworkWriteFailed += error =>
            {
                var message = error == null
                    ? "Network log folder recovered - mirroring resumed."
                    : $"Network log mirror problem: {error}";
                // InvokeRequired (not IsHandleCreated) correctly handles
                // the very first call, which happens synchronously in this
                // constructor before the window handle exists yet: from
                // the same thread that's building the form, InvokeRequired
                // is false and this just calls Log() directly instead of
                // silently dropping the message the way an IsHandleCreated
                // check would.
                if (InvokeRequired)
                    BeginInvoke((Action)(() => Log(message)));
                else
                    Log(message);
            };

            Text = "Picking task";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;

            BuildLayout();
            WireEvents();

            _log.Log(_user.Id, _user.Name, "Login", "");

            // Hardware connection (StartupDiagnostics port probing, which
            // can legitimately take many seconds on a machine with several
            // COM ports) must NOT run synchronously in the constructor -
            // that blocks the login button's click handler and the whole
            // app appears frozen until it finishes. Run it after the
            // window is already visible instead.
            Shown += async (s, e) =>
            {
                await Task.Run(() => ConnectHardwareSilently());

                // OpenLoadTaskDialog() already falls back to the system
                // task on its own (no real task files found, or the
                // picker is cancelled) - no need to pre-load it here too.
                // Pre-loading it unconditionally used to cause it to load
                // every single time, even when a real task file already
                // existed in the folder and was about to be picked
                // anyway. AddItem() also has its own defensive fallback
                // for the edge case where nothing ends up loaded at all.
                await OpenLoadTaskDialog();
            };
        }

        private static string ResolveNextToConfig(string fileName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // top bar
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // hardware status bar
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 285));  // current item panel
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // list
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // log (small, technician-visible)

            var topBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            _userLabel.Dock = DockStyle.Fill;
            _taskNameLabel.Dock = DockStyle.Fill;
            _loadButton.Dock = DockStyle.Fill;
            _importButton.Dock = DockStyle.Fill;
            _logoutButton.Dock = DockStyle.Fill;
            _loadButton.Margin = new Padding(5);
            _importButton.Margin = new Padding(5);
            _logoutButton.Margin = new Padding(5);
            topBar.Controls.Add(_userLabel, 0, 0);
            topBar.Controls.Add(_taskNameLabel, 1, 0);
            topBar.Controls.Add(_loadButton, 2, 0);
            topBar.Controls.Add(_importButton, 3, 0);
            topBar.Controls.Add(_logoutButton, 4, 0);
            root.Controls.Add(topBar, 0, 0);

            var statusBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            _hardwareStatusLabel.Dock = DockStyle.Fill;
            _hardwareStatusLabel.Padding = new Padding(10, 0, 0, 0);
            _recheckButton.Dock = DockStyle.Fill;
            _recheckButton.Margin = new Padding(5);
            _openMatrixButton.Dock = DockStyle.Fill;
            _openMatrixButton.Margin = new Padding(5);
            statusBar.Controls.Add(_hardwareStatusLabel, 0, 0);
            statusBar.Controls.Add(_recheckButton, 1, 0);
            statusBar.Controls.Add(_openMatrixButton, 2, 0);
            root.Controls.Add(statusBar, 0, 1);

            var currentPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            currentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            currentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

            var infoStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            infoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            infoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            _currentItemLabel.Dock = DockStyle.Fill;
            _statusLabel.Dock = DockStyle.Fill;
            infoStack.Controls.Add(_currentItemLabel, 0, 0);
            infoStack.Controls.Add(_statusLabel, 0, 1);
            currentPanel.Controls.Add(infoStack, 0, 0);

            var rightStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            // Bordered group: scale weight, expected weight, and quantity
            // shown together side by side, each with a small caption above
            // its (bigger) value - previously expected weight and
            // quantity were crammed into one small line of text.
            var weightGroup = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            var weightGroupLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            weightGroupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            weightGroupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            weightGroupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            weightGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            weightGroupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _scaleWeightCaption.Dock = DockStyle.Fill;
            _currentWeightLabel.Dock = DockStyle.Fill;
            _expectedWeightCaption.Dock = DockStyle.Fill;
            _expectedWeightValueLabel.Dock = DockStyle.Fill;
            _quantityCaption.Dock = DockStyle.Fill;
            _quantityValueLabel.Dock = DockStyle.Fill;

            weightGroupLayout.Controls.Add(_scaleWeightCaption, 0, 0);
            weightGroupLayout.Controls.Add(_currentWeightLabel, 0, 1);
            weightGroupLayout.Controls.Add(_expectedWeightCaption, 1, 0);
            weightGroupLayout.Controls.Add(_expectedWeightValueLabel, 1, 1);
            weightGroupLayout.Controls.Add(_quantityCaption, 2, 0);
            weightGroupLayout.Controls.Add(_quantityValueLabel, 2, 1);
            weightGroup.Controls.Add(weightGroupLayout);
            rightStack.Controls.Add(weightGroup, 0, 0);

            _skipButton.Dock = DockStyle.Fill;
            _skipButton.Margin = new Padding(15, 5, 15, 5);
            rightStack.Controls.Add(_skipButton, 0, 1);

            var editRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            _addButton.Dock = DockStyle.Fill;
            _editButton.Dock = DockStyle.Fill;
            _deleteButton.Dock = DockStyle.Fill;
            _weightButton.Dock = DockStyle.Fill;
            _addButton.Margin = new Padding(5);
            _editButton.Margin = new Padding(5);
            _deleteButton.Margin = new Padding(5);
            _weightButton.Margin = new Padding(5);
            editRow.Controls.Add(_addButton, 0, 0);
            editRow.Controls.Add(_editButton, 1, 0);
            editRow.Controls.Add(_deleteButton, 2, 0);
            editRow.Controls.Add(_weightButton, 3, 0);
            rightStack.Controls.Add(editRow, 0, 2);

            currentPanel.Controls.Add(rightStack, 1, 0);

            root.Controls.Add(currentPanel, 0, 2);

            _listView.Columns.Add("#", 40);
            _listView.Columns.Add("Label", 70);
            _listView.Columns.Add("Name", 260);
            _listView.Columns.Add("Unit weight", 100);
            _listView.Columns.Add("Qty", 70);
            _listView.Columns.Add("Expected total", 120);
            _listView.Columns.Add("Length (mm)", 100);
            root.Controls.Add(_listView, 0, 3);

            root.Controls.Add(_logBox, 0, 4);

            Controls.Add(root);
        }

        private void WireEvents()
        {
            _userLabel.Text = $"Operator: {_user.Name}";
            _logoutButton.Click += (s, e) => DoLogout();
            _loadButton.Click += async (s, e) => await OpenLoadTaskDialog();
            _importButton.Click += async (s, e) => await ImportExternalCsv();
            _skipButton.Click += async (s, e) => await SkipCurrent();
            _recheckButton.Click += async (s, e) => await RecheckHardware();
            _healthCheckTimer.Tick += (s, e) =>
            {
                if (_healthCheckRunning)
                    return; // previous check still in flight (e.g. a slow full reconnect) - skip this tick
                _healthCheckRunning = true;
                Task.Run(() =>
                {
                    try { HealthCheckTick(); }
                    finally { _healthCheckRunning = false; }
                });
            };
            _openMatrixButton.Click += (s, e) => OpenMatrixView();
            _addButton.Click += async (s, e) => await AddItem();
            _editButton.Click += async (s, e) => await EditCurrentItem();
            _deleteButton.Click += async (s, e) => await DeleteCurrentItem();
            _weightButton.Click += (s, e) => UpdateCurrentWeight();
            _listView.SelectedIndexChanged += async (s, e) => await LocateClickedItem();
        }

        /// <summary>
        /// Clicking a row in the task list lights that item's LED for
        /// quick reference/locate purposes - it does NOT change the
        /// current item, pass tracking, or weight-verification state.
        /// The normal AdvanceTo-driven progression continues exactly as
        /// before regardless of what gets clicked here. The list's native
        /// blue selection highlight (whatever was clicked) and the green
        /// BackColor highlight (the true current item, set in AdvanceTo)
        /// are independent visual channels, so there's nothing to
        /// reconcile between them.
        /// </summary>
        private async Task LocateClickedItem()
        {
            if (_listView.SelectedIndices.Count == 0)
                return;

            var clickedIndex = _listView.SelectedIndices[0];
            if (clickedIndex < 0 || clickedIndex >= _items.Count)
                return;

            var item = _items[clickedIndex];
            if (string.IsNullOrWhiteSpace(item.Index))
            {
                Log($"\"{item.Name}\" has no LED to locate.");
                return;
            }

            var found = await SetLedByLabel(item.Index);
            Log(found
                ? $"Located: {item.Index} ({item.Name}) - LED lit for reference (current item unchanged)."
                : $"Could not locate \"{item.Index}\" - no LED on record for it.");
        }

        private void OpenMatrixView()
        {
            if (_matrix == null)
            {
                // Likely just a brief gap from the background health-check
                // tearing down an old (probably still-fine) connection
                // before reconnecting - try once, right now, rather than
                // refusing outright over a transient timing window.
                Log("LED matrix requested while relay wasn't connected - attempting a fresh connection now.");
                ConnectHardwareSilently();
            }

            if (_matrix == null)
            {
                MessageBox.Show(this, "Relay hardware is not connected right now - fix that first (see status bar / Recheck hardware).",
                    "No relay connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Pause background recovery while this window has the shared
            // connection open - otherwise a reconnect cycle could dispose
            // the connection this window is actively using mid-session.
            StopAutoRecheck();
            var matrixForm = new RelayControlForm(_config, _matrix, _labels);
            matrixForm.FormClosed += (s, e) =>
            {
                if (!_relayOk || !_scaleOk)
                    StartAutoRecheck();
            };
            matrixForm.Show();
        }

        private void ConnectHardwareSilently() => ConnectHardware(verbose: true);

        private void ConnectHardware(bool verbose)
        {
            DisposeHardware();

            // Windows (and especially USB-serial adapters) can need a
            // short moment to actually release a COM port after Close();
            // probing again immediately can spuriously fail even though
            // the hardware itself is completely fine. This was very
            // likely why the background recovery check "almost always
            // failed" - it was re-probing the exact port it had just
            // closed, with no gap at all.
            System.Threading.Thread.Sleep(300);

            var report = StartupDiagnostics.RunAll(_config, verbose ? Log : (Action<string>)null);
            _lastDiagnosticsReport = report;
            if (report.ConfigWasModified)
            {
                try { _config.SaveToFile("config.json"); }
                catch (Exception ex) { Log($"Could not persist auto-corrected config: {ex.Message}"); }
            }

            var relayOk = report.Results.FirstOrDefault(r => r.Name == "Relay port")?.Ok ?? false;
            var scaleOk = report.Results.FirstOrDefault(r => r.Name == "Scale port")?.Ok ?? false;

            if (relayOk)
            {
                try
                {
                    _relay = new ModbusRelayCommutator(_config.SerialPort);
                    _matrix = new LedMatrix(_relay, _config);
                    _matrix.Warning += msg => Log($"Relay: {msg}"); // informational only - never blocks the connection or the task workflow
                    _matrix.ResetToKnownState(); // verified all-off baseline; physical relays could be in any state left over from before
                }
                catch (Exception ex)
                {
                    relayOk = false;
                    if (verbose) Log($"Relay connect failed: {ex.Message}");
                }
            }

            if (scaleOk)
            {
                try
                {
                    _scale = new ScaleReader(_config.ScalePort);
                    _scale.StableWeightReceived += grams =>
                    {
                        if (IsHandleCreated) BeginInvoke((Action)(() => OnWeightReceived(grams)));
                    };
                    _scale.Disconnected += message =>
                    {
                        if (IsHandleCreated) BeginInvoke((Action)(() => OnScaleDisconnected(message)));
                    };
                    _scale.Start();
                }
                catch (Exception ex)
                {
                    scaleOk = false;
                    if (verbose) Log($"Scale connect failed: {ex.Message}");
                }
            }

            _relayOk = relayOk;
            _scaleOk = scaleOk;

            var nowOk = relayOk && scaleOk;
            if (!verbose && nowOk != _lastHardwareOk)
            {
                Log(nowOk
                    ? "Hardware recovered - relay and scale both OK."
                    : $"Still down - Relay: {(relayOk ? "OK" : "PROBLEM")}, Scale: {(scaleOk ? "OK" : "PROBLEM")}");
            }
            _lastHardwareOk = nowOk;

            UpdateHardwareStatus(relayOk, scaleOk);

            // Purely reactive: only keep retrying while something is
            // actually known to be broken (a real write failed, or the
            // scale reported its own disconnect). No proactive/periodic
            // probing while everything's working - that was the source of
            // a real bug (a synthetic probe write interfering with
            // LedMatrix's own tracked state) and isn't needed anyway: the
            // scale reports its own problems, and the relay's health is
            // discovered the moment a real write to it fails.
            if (nowOk)
                StopAutoRecheck();
            else
                StartAutoRecheck();
        }

        /// <summary>
        /// Runs only while in recovery mode (something is known to be
        /// broken). Each tick attempts a full reconnect/re-detection pass,
        /// same as the manual "Recheck hardware" button, until both devices
        /// are healthy again - at which point ConnectHardware stops this
        /// timer itself.
        /// </summary>
        private volatile bool _healthCheckRunning;

        private void HealthCheckTick()
        {
            ConnectHardware(verbose: false);
        }

        private void StartAutoRecheck()
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)StartAutoRecheck);
                return;
            }
            if (_healthCheckTimer.Enabled)
                return;
            _healthCheckTimer.Enabled = true;
            Log("Hardware problem detected - retrying every 8s until it recovers.");
        }

        private void StopAutoRecheck()
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)StopAutoRecheck);
                return;
            }
            _healthCheckTimer.Enabled = false;
        }

        private async Task RecheckHardware()
        {
            _recheckButton.Enabled = false;
            _hardwareStatusLabel.Text = "Rechecking...";
            _hardwareStatusLabel.ForeColor = Color.DimGray;

            try
            {
                // Log() and UpdateHardwareStatus() are both thread-safe now
                // (they marshal to the UI thread themselves), so the actual
                // probing can run in the background without freezing the
                // window - the operator can keep using the rest of the
                // screen while this runs.
                await Task.Run(() => ConnectHardwareSilently());
            }
            finally
            {
                _recheckButton.Enabled = true;
            }

            ShowRecheckResultDialog();
        }

        private void ShowRecheckResultDialog()
        {
            using var dialog = new HardwareCheckResultDialog(_lastDiagnosticsReport);
            dialog.ShowDialog(this);
        }

        private void OnScaleDisconnected(string message)
        {
            Log(message);
            _lastHardwareOk = false;
            _scaleOk = false;
            UpdateHardwareStatus(_relayOk, _scaleOk);
            StartAutoRecheck();
        }

        /// <summary>
        /// Called whenever a relay write fails during active use (not just
        /// at connect time) - e.g. the cable gets unplugged mid-task. Never
        /// lets that exception surface to the operator as a crash/error
        /// dialog: just flags the status banner and lets the auto-recheck
        /// loop quietly reconnect once the hardware is back.
        /// </summary>
        private void OnRelayError(string message)
        {
            Log($"Relay error: {message}");
            _lastHardwareOk = false;
            _relayOk = false;
            UpdateHardwareStatus(_relayOk, _scaleOk);
            StartAutoRecheck();
        }

        private void UpdateHardwareStatus(bool relayOk, bool scaleOk)
        {
            if (_hardwareStatusLabel.IsHandleCreated && _hardwareStatusLabel.InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateHardwareStatus(relayOk, scaleOk)));
                return;
            }

            if (relayOk && scaleOk)
            {
                _hardwareStatusLabel.Text = "Relay: OK   Scale: OK";
                _hardwareStatusLabel.ForeColor = Color.Green;
            }
            else
            {
                _hardwareStatusLabel.Text = $"Relay: {(relayOk ? "OK" : "PROBLEM")}   Scale: {(scaleOk ? "OK" : "PROBLEM")} - press Recheck hardware";
                _hardwareStatusLabel.ForeColor = Color.Red;
            }
        }

        private async Task<bool> SetLedByLabel(string label)
        {
            if (_matrix == null)
                return false;

            if (!_labels.TryFind(label, out int row, out int col))
            {
                // If it looks like it should exist but doesn't, check for
                // a Cyrillic/Latin homoglyph mismatch - a very easy thing
                // to create by hand (typing "C3" on a keyboard when the
                // task file actually has Cyrillic "С3") and otherwise
                // completely invisible on screen. Fall back to using it
                // directly so the LED actually lights, rather than just
                // reporting the mismatch and leaving the operator stuck.
                if (_labels.TryFindLookalike(label, out var lookalike) && _labels.TryFind(lookalike, out row, out col))
                {
                    Log($"Label \"{label}\" not found exactly, but used similar-looking label \"{lookalike}\" instead - likely a Cyrillic/Latin character mismatch (they look identical but are different characters). Consider fixing the label to match.");
                }
                else
                {
                    return false;
                }
            }

            try
            {
                await Task.Run(() => _matrix.SetLed(row, col));
                return true;
            }
            catch (Exception ex)
            {
                OnRelayError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Browses to a task CSV file anywhere on disk (the regular picker
        /// only scans the configured task folder) and loads it the same
        /// way as any other task - TaskCsvLoader handles this format
        /// natively now. Keeps a one-off confirmation for items that will
        /// end up outside LED-managed storage, since a manually-browsed
        /// file is more likely to be unfamiliar data than a routine load
        /// from the known task folder.
        /// </summary>
        private async Task ImportExternalCsv()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Select a task CSV file to open"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                var preview = await Task.Run(() => TaskCsvLoader.Load(dialog.FileName));

                // Identify items that will end up outside LED-managed
                // storage: either the name is too long to ever be a
                // physical grid label, or there's simply no existing LED
                // entry for it yet. Confirm with the operator before
                // committing, rather than silently loading items that
                // will never light up during a task.
                var outsideLed = new List<string>();
                foreach (var item in preview.Items)
                {
                    var entry = _labels.GetEntry(item.Index);
                    var hasPosition = entry != null && entry.Row.HasValue && entry.Col.HasValue;
                    var tooLongForLed = item.Index.Length > IndexRegistry.MaxLabelLength;
                    if (!hasPosition || tooLongForLed)
                        outsideLed.Add(item.Index);
                }

                if (outsideLed.Count > 0)
                {
                    var previewText = string.Join(", ", outsideLed.Take(15));
                    if (outsideLed.Count > 15)
                        previewText += $", ... and {outsideLed.Count - 15} more";

                    var message =
                        $"{outsideLed.Count} of {preview.Items.Count} item(s) will be stored OUTSIDE LED-managed storage " +
                        $"(no physical LED position - either the name is longer than {IndexRegistry.MaxLabelLength} characters, " +
                        "or there's no matching LED entry yet):\n\n" +
                        $"{previewText}\n\n" +
                        "These items still work fully for weight verification, just without a light-up indicator on the matrix. Continue?";

                    var confirm = MessageBox.Show(this, message, "Items outside LED storage",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes)
                    {
                        Log("Open cancelled - items outside LED storage were not confirmed.");
                        return;
                    }
                }

                await LoadTaskFile(dialog.FileName, 1);
            }
            catch (Exception ex)
            {
                Log($"Failed to open file: {ex.Message}");
                MessageBox.Show(this, $"Failed to open file: {ex.Message}", "Open error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task OpenLoadTaskDialog()
        {
            _loadButton.Enabled = false;
            string folderStatusMessage = null;
            try
            {
                var (folder, files) = await Task.Run(() =>
                {
                    var (resolvedFolder, statusMessage) = ResolveTaskFolder();
                    if (statusMessage != null)
                        folderStatusMessage = statusMessage;

                    var csvFiles = new DirectoryInfo(resolvedFolder)
                        .GetFiles("*.csv")
                        .Where(f => !f.Name.Equals("index.csv", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .ToList();

                    return (resolvedFolder, csvFiles);
                });

                if (folderStatusMessage != null)
                    Log(folderStatusMessage);

                if (files.Count == 0)
                {
                    Log($"No task CSV files found in {folder} - falling back to the local system task list.");
                    await EnsureSystemTaskLoaded();
                    return;
                }

                using var dialog = new LoadTaskDialog(files);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    await EnsureSystemTaskLoaded();
                    return;
                }

                await LoadTaskFile(dialog.SelectedFilePath, dialog.RepeatCount);
            }
            finally
            {
                _loadButton.Enabled = true;
            }
        }

        /// <summary>
        /// Loads a specific task file and resets repeat-pass tracking.
        /// Weight for each row is resolved from index.csv, not the task
        /// file itself.
        /// </summary>
        private async Task LoadTaskFile(string path, int repeatCount)
        {
            try
            {
                Log($"Loading: {path}");

                var taskFile = await Task.Run(() =>
                {
                    var file = TaskCsvLoader.Load(path);
                    foreach (var item in file.Items)
                    {
                        // The file's own weight is authoritative on load
                        // (this format carries weight per row) - but if
                        // this label already has a real weight on record
                        // (e.g. set via the LED matrix), that wins for
                        // consistency across every task referencing it.
                        if (_labels.TryGetWeight(item.Index, out var existingWeight) && existingWeight > 0)
                        {
                            item.Weight = existingWeight;
                        }
                        else
                        {
                            var existingEntry = _labels.GetEntry(item.Index);
                            _labels.Set(item.Index, item.Weight, existingEntry?.Row, existingEntry?.Col);
                        }
                    }
                    return file;
                });
                _labels.SaveChanges();

                _items = taskFile.Items;
                _currentMassCoefficient = taskFile.MassCoefficient;
                _currentTaskTitle = taskFile.Title;
                _currentTaskFilePath = path;
                _totalRepeats = repeatCount;
                _currentPass = 1;
                UpdateTaskNameLabel();
                Log($"Loaded {_items.Count} rows from {path} (mass coefficient {_currentMassCoefficient:0.###}, repeat count: {repeatCount})");
                _log.Log(_user.Id, _user.Name, "TaskLoaded", $"{Path.GetFileName(path)} | repeatCount={repeatCount}");

                PopulateListView();
                _currentIndex = -1;

                if (_items.Count == 0)
                {
                    // Nothing to advance to yet - leave it loaded-but-empty
                    // rather than immediately triggering the "task
                    // complete" dialog (0 items always looks "complete").
                    // Ready for "Add item" to populate it.
                    _currentItemLabel.Text = "(empty - use Add item)";
                    _expectedWeightValueLabel.Text = "--"; _quantityValueLabel.Text = "--";
                    _statusLabel.Text = "";
                    _skipButton.Enabled = false;
                    return;
                }

                await AdvanceTo(0);
            }
            catch (Exception ex)
            {
                Log($"Failed to load task file: {ex.Message}");
            }
        }

        private void UpdateTaskNameLabel()
        {
            var baseName = Path.GetFileNameWithoutExtension(_currentTaskFilePath ?? "");
            _taskNameLabel.Text = _totalRepeats > 1
                ? $"{baseName}  (pass {_currentPass} of {_totalRepeats})"
                : baseName;
            Text = $"Picking task - {baseName}";
        }

        private (string Folder, string Message) ResolveTaskFolder()
        {
            var localFolder = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                if (Directory.Exists(_config.TaskFolderPath))
                    return (_config.TaskFolderPath, null);
                return (localFolder, $"Network task folder not reachable: {_config.TaskFolderPath}. Falling back to {localFolder}");
            }
            catch (Exception ex)
            {
                return (localFolder, $"Network task folder check failed: {ex.Message}. Falling back to {localFolder}");
            }
        }

        private void PopulateListView()
        {
            _listView.Items.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var row = new ListViewItem(new[]
                {
                    (i + 1).ToString(),
                    item.Index,
                    item.Name,
                    item.Weight.ToString("0"),
                    item.Quantity.ToString(),
                    ExpectedWeightFor(item).ToString("0"),
                    item.LengthMm?.ToString("0.###") ?? ""
                });
                _listView.Items.Add(row);
            }
        }

        private async Task AdvanceTo(int index)
        {
            if (_currentIndex >= 0 && _currentIndex < _listView.Items.Count)
                _listView.Items[_currentIndex].BackColor = SystemColors.Window;

            _awaitingRemoval = false;
            _currentIndex = index;

            if (_currentIndex >= _items.Count)
            {
                var isSystemTask = !string.IsNullOrEmpty(_currentTaskFilePath) &&
                    Path.GetFileName(_currentTaskFilePath).Equals(_config.SystemTaskFileName, StringComparison.OrdinalIgnoreCase);

                if (isSystemTask)
                {
                    if (_items.Count == 0)
                    {
                        // Nothing to loop - just sit idle-but-loaded, no completion dialog.
                        _currentItemLabel.Text = "(empty - use Add item)";
                        _expectedWeightValueLabel.Text = "--"; _quantityValueLabel.Text = "--";
                        _statusLabel.Text = "";
                        _skipButton.Enabled = false;
                        return;
                    }

                    // The system task is a perpetual local list, not a
                    // task with a defined end - loop back to the start
                    // silently instead of ever showing "task complete".
                    Log("System task reached the end - looping back to the start.");
                    _currentIndex = -1;
                    await AdvanceTo(0);
                    return;
                }

                _currentItemLabel.Text = _currentPass < _totalRepeats ? "Pass complete" : "Task complete";
                _expectedWeightValueLabel.Text = "--"; _quantityValueLabel.Text = "--";
                _statusLabel.Text = "";
                _skipButton.Enabled = false;

                var morePassesRemain = _currentPass < _totalRepeats;
                Log(morePassesRemain
                    ? $"Pass {_currentPass} of {_totalRepeats} complete."
                    : "Task complete (all passes done).");
                _log.Log(_user.Id, _user.Name, "PassComplete", $"{_currentPass} of {_totalRepeats}");

                await HandleEndOfPass(morePassesRemain);
                return;
            }

            _skipButton.Enabled = true;
            var item = _items[_currentIndex];
            _listView.Items[_currentIndex].BackColor = Color.LimeGreen;
            _listView.Items[_currentIndex].EnsureVisible();
            _currentItemLabel.Text = $"{item.Name}  [{item.Index}]";
            _expectedWeightValueLabel.Text = $"{ExpectedWeightFor(item):0} g"; _quantityValueLabel.Text = $"x{item.Quantity}";
            _statusLabel.Text = "";

            if (string.IsNullOrWhiteSpace(item.Index))
            {
                // No LED label for this row: just clear every LED and rely
                // on the scale check alone - there's nothing to look up.
                if (_matrix != null)
                {
                    try { await Task.Run(() => _matrix.AllOff()); }
                    catch (Exception ex) { OnRelayError(ex.Message); }
                }
            }
            else
            {
                var found = await SetLedByLabel(item.Index);
                if (!found)
                {
                    Log($"Index \"{item.Index}\" has no physical LED assigned - continuing with scale check only.");

                    using var noLedDialog = new NoLedConfirmDialog(item.Index, item.Name);
                    noLedDialog.ShowDialog(this);

                    if (noLedDialog.Result == NoLedConfirmDialog.Choice.SkipToNext)
                    {
                        Log($"Row {_currentIndex + 1} skipped by operator (no LED assigned).");
                        _log.Log(_user.Id, _user.Name, "RowSkipped", $"{item.Index} | {item.Name} | reason=no LED assigned");
                        await AdvanceTo(_currentIndex + 1);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Shown after every pass completes. Continue (only offered if
        /// more passes remain) moves to the next pass. Pick another list
        /// (only offered once every configured pass is done) opens the
        /// task picker again. Cancel always exits back to an idle,
        /// no-task-loaded state regardless of how many passes remain.
        /// </summary>
        private async Task HandleEndOfPass(bool morePassesRemain)
        {
            if (_items.Count == 0)
                return; // nothing was ever loaded - don't prompt on startup

            var taskName = Path.GetFileNameWithoutExtension(_currentTaskFilePath ?? "");
            using var dialog = new EndOfPassDialog(taskName, _currentPass, _totalRepeats, morePassesRemain);
            dialog.ShowDialog(this);

            _log.Log(_user.Id, _user.Name, "EndOfPassChoice", dialog.Result.ToString());

            switch (dialog.Result)
            {
                case EndOfPassDialog.Choice.Continue:
                    _currentPass++;
                    UpdateTaskNameLabel();
                    _currentIndex = -1;
                    await AdvanceTo(0);
                    break;

                case EndOfPassDialog.Choice.PickAnotherList:
                    ResetToIdle();
                    await OpenLoadTaskDialog();
                    break;

                case EndOfPassDialog.Choice.Cancel:
                    ResetToIdle();
                    Log("Task cancelled by operator - falling back to the local system task list.");
                    await EnsureSystemTaskLoaded();
                    break;
            }
        }

        /// <summary>Clears the currently loaded task and returns the screen to its no-task-loaded state.</summary>
        /// <summary>
        /// Loads the local system task list (next to the executable) as a
        /// fallback whenever the operator would otherwise land on a blank
        /// "no task loaded" screen - e.g. cancelling the task picker, or
        /// choosing Cancel at end-of-pass. Creates an empty one (headers
        /// only) if it doesn't exist yet, so it's immediately usable via
        /// "Add item" rather than requiring a pre-existing file.
        /// </summary>
        private async Task EnsureSystemTaskLoaded()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.SystemTaskFileName);

            if (!File.Exists(path))
            {
                try
                {
                    TaskCsvLoader.Save(new TaskFile { MassCoefficient = 1.0, Title = "System task", Items = new List<TaskItem>() }, path);
                    Log($"Created empty system task list: {path}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to create system task list: {ex.Message}");
                    return;
                }
            }

            await LoadTaskFile(path, 1);
        }

        private void ResetToIdle()
        {
            _items = new List<TaskItem>();
            _currentIndex = -1;
            _totalRepeats = 1;
            _currentPass = 1;
            _currentTaskFilePath = null;
            _taskNameLabel.Text = "(no task loaded)";
            Text = "Picking task";
            _listView.Items.Clear();
            _currentItemLabel.Text = "--";
            _expectedWeightValueLabel.Text = "--"; _quantityValueLabel.Text = "--";
            _statusLabel.Text = "";
            _skipButton.Enabled = false;
        }

        /// <summary>
        /// The true expected total weight for this item, for comparison
        /// against a real scale reading. item.Weight itself stays as the
        /// nominal (pre-coefficient) value matching the task file's kg
        /// column directly - the mass coefficient is applied here, at
        /// comparison/display time, not baked into the stored value.
        /// </summary>
        /// <summary>
        /// Converts a true weight reading (from the scale, or typed by the
        /// operator as an actual measured value) into the nominal
        /// representation item.Weight stores - the inverse of what
        /// ExpectedWeightFor() applies. Any weight coming from a real
        /// measurement needs this before being stored, so it stays
        /// consistent with weights loaded from the file's kg column.
        /// </summary>
        private double ToNominalWeight(double trueWeightGrams) =>
            _currentMassCoefficient > 0 ? trueWeightGrams / _currentMassCoefficient : trueWeightGrams;

        private double ExpectedWeightFor(TaskItem item) =>
            item.Weight * item.Quantity * _currentMassCoefficient;

        private double CurrentPrecision()
        {
            var item = _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;

            // Explicit per-row override (grams) always wins.
            if (item?.Precision.HasValue == true)
                return item.Precision.Value;

            // Otherwise, tolerance scales with the expected weight - a
            // fixed gram tolerance doesn't make sense across items of very
            // different weights (a 2g item and a 500g item shouldn't both
            // get +-2g).
            var expected = item != null ? ExpectedWeightFor(item) : 0;
            return expected * (_config.DefaultPrecisionPercent / 100.0);
        }

        private async void OnWeightReceived(double grams)
        {
            try
            {
                _currentWeightLabel.Text = $"{grams:0} g";
                _lastKnownWeight = grams;

                if (_awaitingRemoval)
                {
                    if (Math.Abs(grams) < _config.MinimumValidWeightGrams)
                    {
                        // Item lifted off - now actually move to the next row.
                        _awaitingRemoval = false;
                        Log($"Row {_currentIndex + 1} item removed - advancing.");
                        await AdvanceTo(_currentIndex + 1);
                    }
                    else
                    {
                        _statusLabel.Text = "OK - remove item to continue";
                        _statusLabel.ForeColor = Color.Green;
                    }
                    return;
                }

                if (Math.Abs(grams) < _config.MinimumValidWeightGrams)
                {
                    _statusLabel.Text = "(scale near zero)";
                    _statusLabel.ForeColor = Color.Gray;
                    return;
                }

                if (_currentIndex < 0 || _currentIndex >= _items.Count)
                    return;

                var item = _items[_currentIndex];
                var expected = ExpectedWeightFor(item);
                var precision = CurrentPrecision();
                var diff = grams - expected;

                if (Math.Abs(diff) <= precision)
                {
                    _statusLabel.Text = "OK - remove item to continue";
                    _statusLabel.ForeColor = Color.Green;
                    Log($"Row {_currentIndex + 1} OK: {grams:0}g (expected {expected:0}g +/-{precision:0}g)");
                    _log.Log(_user.Id, _user.Name, "RowCompleted", $"{item.Index} | {item.Name} | actual={grams:0}g expected={expected:0}g");
                    _awaitingRemoval = true; // don't advance yet - wait for the item to actually be taken off the scale
                }
                else
                {
                    _statusLabel.Text = $"diff: {diff:+0;-0} g";
                    _statusLabel.ForeColor = Color.OrangeRed;
                }
            }
            catch (Exception ex)
            {
                // async void - nothing else can catch this. Never let a
                // weight-processing hiccup take the whole app down.
                Log($"Unexpected error handling weight reading: {ex.Message}");
            }
        }

        private async Task SkipCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count)
                return;

            var item = _items[_currentIndex];
            using var reasonForm = new SkipReasonForm(item.Name);
            if (reasonForm.ShowDialog(this) != DialogResult.OK)
                return;

            if (reasonForm.KeepOnCurrentItem)
            {
                // A hardware/contact issue, not an item problem - note it
                // and stay put. Weight verification for this item still
                // matters, so this deliberately does NOT advance like a
                // real skip does.
                Log($"Row {_currentIndex + 1} LED CONTACT ISSUE noted: {item.Index} | {item.Name} - staying on this item, weight check still active.");
                _log.Log(_user.Id, _user.Name, "LedContactIssue", $"{item.Index} | {item.Name}");
                return;
            }

            Log($"Row {_currentIndex + 1} SKIPPED: {item.Index} | {item.Name} | reason: {reasonForm.SelectedReason}");
            _log.Log(_user.Id, _user.Name, "RowSkipped", $"{item.Index} | {item.Name} | reason={reasonForm.SelectedReason}");
            await AdvanceTo(_currentIndex + 1);
        }

        private async Task AddItem()
        {
            if (string.IsNullOrEmpty(_currentTaskFilePath))
                await EnsureSystemTaskLoaded();

            using var editForm = new TaskItemEditForm(null, isNewItem: true, getCurrentScaleWeight: () => _lastKnownWeight, pickLabelFromMatrix: PickLabelFromMatrix);
            if (editForm.ShowDialog(this) != DialogResult.OK)
                return;

            var newItem = editForm.Result;
            if (_items.Any(i => string.Equals(i.Index, newItem.Index, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"Label \"{newItem.Index}\" is already used by another row in this task.",
                    "Duplicate label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_labels.TryGetWeight(newItem.Index, out var weight) && weight > 0)
            {
                // Existing index with a real registered weight (e.g.
                // picked from the matrix, or typed a label that already
                // exists) - use it; edit it via "Update weight" instead.
                newItem.Weight = weight;
            }
            else
            {
                // Either a brand new index, or an existing one that's
                // still at the 0 placeholder (e.g. created via the LED
                // matrix's right-click label editor, which has no weight
                // field of its own) - use what's in the dialog, and
                // set/update the registry entry with it, preserving any
                // physical position that label already has.
                newItem.Weight = ToNominalWeight(editForm.EnteredWeight);
                var existingEntry = _labels.GetEntry(newItem.Index);
                if (_labels.Set(newItem.Index, newItem.Weight, existingEntry?.Row, existingEntry?.Col))
                {
                    _labels.SaveChanges(); // a single deliberate action, same rationale as "Update weight" - persist immediately
                    Log($"Set weight for index \"{newItem.Index}\": {editForm.EnteredWeight:0}g true -> {newItem.Weight:0}g nominal (index.csv)");
                }
                else
                {
                    Log($"Could not create index \"{newItem.Index}\" - unexpected conflict.");
                }
            }

            _items.Add(newItem);
            PopulateListView();
            SaveEditedTask();

            Log($"Row ADDED: {newItem.Index} | {newItem.Name} | weight={newItem.Weight} qty={newItem.Quantity}");
            _log.Log(_user.Id, _user.Name, "RowAdded", $"{newItem.Index} | {newItem.Name} | weight={newItem.Weight} qty={newItem.Quantity}");

            // If nothing was in progress (e.g. task was empty/complete), start on the new row.
            if (_currentIndex < 0 || _currentIndex >= _items.Count - 1)
                await AdvanceTo(_items.Count - 1);
        }

        /// <summary>
        /// Opens the LED matrix as a nested modal picker (fully supported
        /// by WinForms - a dialog can open another dialog from a button
        /// click). Clicking a labeled cell there selects it and returns
        /// here; clicking an unlabeled one just switches the relay as
        /// normal, since there's nothing to pick.
        /// </summary>
        private string PickLabelFromMatrix()
        {
            if (_matrix == null)
            {
                MessageBox.Show(this, "Relay hardware is not connected right now.", "No relay connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            // Avoid the background health-check swapping the shared
            // connection out from under the picker mid-session.
            StopAutoRecheck();
            try
            {
                using var picker = new RelayControlForm(_config, _matrix, _labels, pickerMode: true);
                var result = picker.ShowDialog(this);
                return result == DialogResult.OK ? picker.SelectedLabel : null;
            }
            finally
            {
                if (!_relayOk || !_scaleOk)
                    StartAutoRecheck();
            }
        }

        private async Task EditCurrentItem()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count)
            {
                MessageBox.Show(this, "No current item to edit.", "Nothing to edit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = _items[_currentIndex];
            var before = $"index={item.Index} name={item.Name} weight={item.Weight} qty={item.Quantity} length={item.LengthMm}";

            using var editForm = new TaskItemEditForm(item, isNewItem: false, displayWeight: item.Weight * _currentMassCoefficient, getCurrentScaleWeight: () => _lastKnownWeight, pickLabelFromMatrix: PickLabelFromMatrix);
            if (editForm.ShowDialog(this) != DialogResult.OK)
                return;

            var edited = editForm.Result;

            // If the label changed, make sure it's not already used by a
            // DIFFERENT row in this task (same check AddItem does).
            if (!string.Equals(edited.Index, item.Index, StringComparison.OrdinalIgnoreCase) &&
                _items.Any(i => i != item && string.Equals(i.Index, edited.Index, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"Label \"{edited.Index}\" is already used by another row in this task.",
                    "Duplicate label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            item.Index = edited.Index;
            item.Name = edited.Name;
            item.Quantity = edited.Quantity;
            item.LengthMm = edited.LengthMm;

            if (_labels.TryGetWeight(item.Index, out var weight) && weight > 0)
            {
                // Existing index with a real registered weight (unchanged,
                // or changed to point at one that already has a weight) -
                // use it.
                item.Weight = weight;
            }
            else
            {
                // Either a brand new index, or an existing one still at
                // the 0 placeholder - use the dialog's value, preserving
                // any physical position that label already has.
                item.Weight = ToNominalWeight(editForm.EnteredWeight);
                var existingEntry = _labels.GetEntry(item.Index);
                if (_labels.Set(item.Index, item.Weight, existingEntry?.Row, existingEntry?.Col))
                {
                    _labels.SaveChanges();
                    Log($"Set weight for index \"{item.Index}\": {editForm.EnteredWeight:0}g true -> {item.Weight:0}g nominal (index.csv)");
                }
                else
                {
                    Log($"Could not save weight for index \"{item.Index}\" - unexpected conflict.");
                }
            }

            PopulateListView();
            SaveEditedTask();

            var after = $"index={item.Index} name={item.Name} weight={item.Weight} qty={item.Quantity} length={item.LengthMm}";
            Log($"Row {_currentIndex + 1} EDITED: before: {before} | after: {after}");
            _log.Log(_user.Id, _user.Name, "RowEdited", $"before: {before} | after: {after}");

            await AdvanceTo(_currentIndex); // refresh the current-item display and re-highlight
        }

        private async Task DeleteCurrentItem()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count)
            {
                MessageBox.Show(this, "No current item to delete.", "Nothing to delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = _items[_currentIndex];
            var confirm = MessageBox.Show(this, $"Delete \"{item.Name}\" [{item.Index}]? This cannot be undone.",
                "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            _items.RemoveAt(_currentIndex);
            PopulateListView();
            SaveEditedTask();

            Log($"Row DELETED: {item.Index} | {item.Name} | weight={item.Weight} qty={item.Quantity}");
            _log.Log(_user.Id, _user.Name, "RowDeleted", $"{item.Index} | {item.Name} | weight={item.Weight} qty={item.Quantity}");

            // Stay on the same position (now the next row), unless we deleted the last one.
            var nextIndex = Math.Min(_currentIndex, _items.Count - 1);
            _currentIndex = -1; // force AdvanceTo to treat this as a fresh move
            await AdvanceTo(Math.Max(nextIndex, 0));
        }

        /// <summary>
        /// Updates the weight for the current row's index directly from
        /// the task screen, without needing to leave for the LED matrix
        /// window. This writes to index.csv (the shared registry), so it
        /// affects every task that references this same index - not just
        /// the current row. Any physical LED position already assigned to
        /// this index is preserved.
        /// </summary>
        private void UpdateCurrentWeight()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count)
            {
                MessageBox.Show(this, "No current item to update.", "Nothing to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = _items[_currentIndex];
            if (string.IsNullOrWhiteSpace(item.Index))
            {
                MessageBox.Show(this, "This row has no index/label, so there's nothing in index.csv to update.",
                    "No index", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new WeightEditDialog(item.Index, item.Name, item.Weight * _currentMassCoefficient, () => _lastKnownWeight);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var oldWeight = item.Weight;
            var newNominalWeight = ToNominalWeight(dialog.ResultWeight);
            var existingEntry = _labels.GetEntry(item.Index);
            var saved = _labels.Set(item.Index, newNominalWeight, existingEntry?.Row, existingEntry?.Col);

            if (!saved)
            {
                Log($"Failed to save weight for index \"{item.Index}\" (unexpected - label conflict?).");
                return;
            }

            // This is a single quick edit, not part of a batch editing
            // session (unlike the LED matrix window) - persist right away.
            _labels.SaveChanges();

            // Every row in the currently loaded list that shares this index
            // should reflect the new weight too, not just the current one.
            foreach (var i in _items.Where(i => string.Equals(i.Index, item.Index, StringComparison.OrdinalIgnoreCase)))
                i.Weight = newNominalWeight;

            PopulateListView();
            _expectedWeightValueLabel.Text = $"{ExpectedWeightFor(item):0} g"; _quantityValueLabel.Text = $"x{item.Quantity}";

            Log($"Weight for index \"{item.Index}\" updated: {oldWeight * _currentMassCoefficient:0}g true -> {dialog.ResultWeight:0}g true ({newNominalWeight:0}g nominal, index.csv)");
            _log.Log(_user.Id, _user.Name, "WeightUpdated", $"{item.Index} | {oldWeight:0}g -> {newNominalWeight:0}g nominal");
        }

        /// <summary>
        /// Saves the current in-memory item list to a separate "_edited"
        /// file next to the originally loaded task, so the admin-provided
        /// source file is never overwritten - only the current working
        /// state is persisted, while OperationsLog keeps the full change
        /// history for audit purposes.
        /// </summary>
        private void SaveEditedTask()
        {
            if (string.IsNullOrEmpty(_currentTaskFilePath))
                return;

            try
            {
                var taskFile = new TaskFile { MassCoefficient = _currentMassCoefficient, Title = _currentTaskTitle, Items = _items };
                var fileName = Path.GetFileName(_currentTaskFilePath);

                // The system task list is a living local list, not an
                // admin-provided source file - save it in place. Spinning
                // off a separate "_edited" copy (as we do for other task
                // files, to protect a pristine original) would actually
                // cause real data loss here: EnsureSystemTaskLoaded()
                // always reloads system_task.csv itself, so edits saved
                // anywhere else would silently vanish next time it's used.
                if (fileName.Equals(_config.SystemTaskFileName, StringComparison.OrdinalIgnoreCase))
                {
                    TaskCsvLoader.Save(taskFile, _currentTaskFilePath);
                    return;
                }

                var dir = Path.GetDirectoryName(_currentTaskFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var baseName = Path.GetFileNameWithoutExtension(_currentTaskFilePath);
                // Once already saved as "_edited", keep saving to the same
                // file rather than stacking "_edited_edited_edited...".
                if (baseName.EndsWith("_edited", StringComparison.OrdinalIgnoreCase))
                {
                    var editedPath = Path.Combine(dir, baseName + ".csv");
                    TaskCsvLoader.Save(taskFile, editedPath);
                }
                else
                {
                    var editedPath = Path.Combine(dir, baseName + "_edited.csv");
                    TaskCsvLoader.Save(taskFile, editedPath);
                    _currentTaskFilePath = editedPath; // subsequent saves go to this file
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to save edited task file: {ex.Message}");
            }
        }

        private void DoLogout()
        {
            _log.Log(_user.Id, _user.Name, "Logout", "");
            StopAutoRecheck();
            DisposeHardware();
            LogoutRequested?.Invoke();
            Hide();
        }

        private void DisposeHardware()
        {
            (_relay as IDisposable)?.Dispose();
            _scale?.Dispose();
            _relay = null;
            _matrix = null;
            _scale = null;
        }

        private void Log(string message)
        {
            if (_logBox.IsHandleCreated && _logBox.InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(message)));
                return;
            }
            _logBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _healthCheckTimer.Dispose();
            DisposeHardware();
            base.OnFormClosed(e);
        }
    }
}
