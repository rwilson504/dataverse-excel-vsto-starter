using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataverseAddIn.Connections;

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

        private readonly Label _environment = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        private readonly Label _detail = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
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

        public DataversePaneControl(DataverseConnectionManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            this.ApplyScaling();
            Dock = DockStyle.Fill;
            Padding = new Padding(8);

            _environment.MaximumSize = new Size(260, 0);
            _detail.MaximumSize = new Size(260, 0);
            _detail.Margin = new Padding(0, 0, 0, 8);

            _connections.Click += OnConnections;
            _disconnect.Click += (s, e) => { _manager.Disconnect(); Refresh(); };

            foreach (var button in new[] { _connections, _disconnect })
            {
                button.Margin = new Padding(0, 0, 0, 6);
                button.Width = 0;
                button.AutoSize = true;
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(_environment, 0, 0);
            layout.Controls.Add(_detail, 0, 1);
            layout.Controls.Add(_connections, 0, 2);
            layout.Controls.Add(_disconnect, 0, 3);
            layout.Controls.Add(_output, 0, 4);

            for (var row = 0; row < 4; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Controls.Add(layout);

            _manager.ConnectionChanged += OnConnectionChanged;
            Refresh();
        }

        /// <summary>Appends a line to the pane's log, so callers can report without a dialog.</summary>
        public void Report(string message)
        {
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
                _detail.Text = profile?.EnvironmentUrl ?? string.Empty;
            }
            else
            {
                _environment.ForeColor = SystemColors.GrayText;
                _environment.Text = "Not connected";
                _detail.Text = "Choose a connection to get started.";
            }

            _disconnect.Enabled = _manager.IsConnected;

            base.Refresh();
        }

        private void OnConnections(object sender, EventArgs e)
        {
            using (var dialog = new ConnectionManagerForm(_manager))
                dialog.ShowDialog(this);
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(Refresh));
                return;
            }

            Refresh();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _manager.ConnectionChanged -= OnConnectionChanged;

            base.Dispose(disposing);
        }
    }
}
