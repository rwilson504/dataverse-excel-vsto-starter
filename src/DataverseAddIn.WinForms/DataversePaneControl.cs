using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataverseAddIn.Connections;
using Microsoft.Extensions.Logging;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// The persistent surface: which environment is connected, and the actions that act on it.
    /// Hosted in a VSTO custom task pane, but a plain <see cref="UserControl"/> so it works in
    /// any WinForms host — the sample host shows it without Office involved.
    /// </summary>
    public sealed class DataversePaneControl : UserControl
    {
        private readonly DataverseConnectionManager _manager;
        private readonly FileLoggerProvider _logs;

        private readonly Label _environment = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        private readonly Label _detail = new Label { AutoSize = true };
        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000 };
        private readonly Button _connections = FormScaling.CreateButton("Connections...");
        private readonly Button _disconnect = FormScaling.CreateButton("Disconnect");
        private readonly TextBox _output = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window
        };
        private readonly LinkLabel _openLogs = new LinkLabel
        {
            Text = "Open log folder",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        public DataversePaneControl(DataverseConnectionManager manager, FileLoggerProvider logs = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _logs = logs;

            this.ApplyScaling();
            Dock = DockStyle.Fill;
            Padding = new Padding(8);

            // Both are ambient, and the task pane host supplies white for ForeColor — which is
            // why the URL looked invisible and the buttons read as blank. State both.
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.ControlText;

            _detail.Margin = new Padding(0, 0, 0, 8);

            _connections.Click += OnConnections;
            _disconnect.Click += (s, e) => { _manager.Disconnect(); Refresh(); };

            foreach (var button in new[] { _connections, _disconnect })
            {
                button.Margin = new Padding(0, 0, 0, 6);
                button.Width = 0;
                button.AutoSize = true;

                // BackColor is ambient, so the white background above reaches the buttons and a
                // Button with an explicit BackColor stops drawing its themed face — a white
                // button on a white pane. Order matters: assigning BackColor clears this flag.
                button.BackColor = SystemColors.Control;
                button.UseVisualStyleBackColor = true;
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                AutoSize = true
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(_environment, 0, 0);
            layout.Controls.Add(_detail, 0, 1);
            layout.Controls.Add(_connections, 0, 2);
            layout.Controls.Add(_disconnect, 0, 3);
            layout.Controls.Add(_output, 0, 4);
            layout.Controls.Add(_openLogs, 0, 5);

            for (var row = 0; row < 4; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(layout);

            _manager.ConnectionChanged += OnConnectionChanged;

            if (_logs != null)
            {
                _logs.Written += OnLogged;
                _openLogs.Click += OnOpenLogs;
            }

            _openLogs.Visible = _logs != null;

            Refresh();
        }

        /// <summary>
        /// Only warnings and worse: the file carries everything, and a pane that scrolls past
        /// routine chatter is one nobody reads when it matters.
        /// </summary>
        private void OnLogged(object sender, LogEntryEventArgs e)
        {
            if (e.Level >= LogLevel.Warning) Report(e.Message);
        }

        private void OnOpenLogs(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_logs.Directory);
                Process.Start("explorer.exe", $"\"{_logs.Directory}\"");
            }
            catch (Exception ex)
            {
                Report($"Could not open {_logs.Directory}: {ex.Message}");
            }
        }

        /// <summary>Wrapping tracks the pane, which the user can resize, rather than a fixed width.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            var available = Math.Max(80, ClientSize.Width - Padding.Horizontal - 4);

            _environment.MaximumSize = new Size(available, 0);
            _detail.MaximumSize = new Size(available, 0);
        }

        /// <summary>Appends a line to the pane's log, so callers can report without a dialog.</summary>
        public void Report(string message)
        {
            // Same trap as OnConnectionChanged: InvokeRequired is false with no handle, whatever
            // the thread, and log entries arrive from wherever the work happened.
            if (!IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Report), message);
                return;
            }

            _output.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{System.Environment.NewLine}");
        }

        public new void Refresh()
        {
            if (_manager.IsConnected)
            {
                var profile = _manager.CurrentProfile;

                _environment.ForeColor = Color.ForestGreen;
                _environment.Text = profile?.Name ?? "Connected";

                // Full contrast: this is the one thing on the pane worth reading carefully.
                _detail.ForeColor = SystemColors.ControlText;
                _detail.Text = profile?.EnvironmentUrl ?? string.Empty;
                _tips.SetToolTip(_detail, _detail.Text);
            }
            else
            {
                _environment.ForeColor = SystemColors.GrayText;
                _environment.Text = "Not connected";

                _detail.ForeColor = SystemColors.GrayText;
                _detail.Text = "Choose a connection to get started.";
                _tips.SetToolTip(_detail, string.Empty);
            }

            _disconnect.Enabled = _manager.IsConnected;

            base.Refresh();
        }

        private void OnConnections(object sender, EventArgs e)
        {
            using (var dialog = new ConnectionManagerForm(_manager))
                dialog.ShowDialog(this);

            // The dialog can connect or disconnect, so re-read state rather than trusting that
            // the event reached us while it was open.
            Refresh();
        }

        /// <summary>Catches up on anything that changed while this control had no window.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Refresh();
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            // InvokeRequired is false whenever the handle does not exist, whatever the thread,
            // so without this the update would touch these controls from the thread pool.
            // ConnectAsync completes there, so that is the usual case rather than the rare one.
            if (!IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(Refresh));
                return;
            }

            Refresh();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _manager.ConnectionChanged -= OnConnectionChanged;
                if (_logs != null) _logs.Written -= OnLogged;
            }

            base.Dispose(disposing);
        }
    }
}
