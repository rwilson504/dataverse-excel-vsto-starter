using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;
using DataverseAddIn.WinForms;
using Microsoft.Crm.Sdk.Messages;

namespace DataverseAddIn.Samples.WinFormsHost
{
    /// <summary>
    /// Stand-in for the Excel ribbon: the same two commands, wired the same way, so the
    /// enabled/disabled behaviour can be exercised without Office.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly DataverseConnectionManager _manager;
        private readonly Button _connect = new Button();
        private readonly Button _whoAmI = new Button();
        private readonly TextBox _output = new TextBox();
        private readonly Label _state = new Label();

        public MainForm()
        {
            this.ApplyScaling();

            Text = "Dataverse add-in — ribbon stand-in";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 480);
            MinimumSize = new Size(520, 320);
            Icon = Glyphs.CreateIcon(Glyphs.Cloud, Color.FromArgb(0x21, 0x5F, 0x9A));

            _manager = new DataverseConnectionManager(BuildAuthOptions);
            _manager.ConnectionChanged += (s, e) => UpdateState();

            _connect.Text = "Connections...";
            _connect.Click += OnConnections;

            _whoAmI.Text = "Who Am I";
            _whoAmI.Click += OnWhoAmIAsync;

            _connect.SetGlyph(Glyphs.Cloud);
            _whoAmI.SetGlyph(Glyphs.Contact);

            foreach (var button in new[] { _connect, _whoAmI })
            {
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.MinimumSize = new Size(120, 30);
                button.Margin = new Padding(0, 0, 8, 0);
            }

            _state.AutoSize = true;
            _state.Anchor = AnchorStyles.Left;
            _state.Margin = new Padding(8, 0, 0, 0);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            toolbar.Controls.AddRange(new Control[] { _connect, _whoAmI, _state });

            _output.Multiline = true;
            _output.ReadOnly = true;
            _output.ScrollBars = ScrollBars.Vertical;
            _output.Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints);
            _output.Dock = DockStyle.Fill;
            _output.Margin = new Padding(0);

            var layout = FormScaling.CreateLayout(1, 2);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(_output, 0, 1);

            Controls.Add(layout);

            UpdateState();
        }

        private void UpdateState()
        {
            _whoAmI.Enabled = _manager.IsConnected;

            if (_manager.IsConnected)
            {
                _state.ForeColor = Color.ForestGreen;
                _state.Text = $"Connected: {_manager.CurrentProfile.Name}";
            }
            else
            {
                _state.ForeColor = SystemColors.GrayText;
                _state.Text = "Not connected";
            }
        }

        private void OnConnections(object sender, EventArgs e)
        {
            using (var dialog = new ConnectionManagerForm(_manager))
                dialog.ShowDialog(this);

            UpdateState();
        }

        private async void OnWhoAmIAsync(object sender, EventArgs e)
        {
            _whoAmI.Enabled = false;

            try
            {
                var service = _manager.Current;

                // Execute is synchronous, so keep it off the UI thread.
                var response = await System.Threading.Tasks.Task
                    .Run(() => (WhoAmIResponse)service.Execute(new WhoAmIRequest()))
                    .ConfigureAwait(true);

                Log($"WhoAmI against {_manager.CurrentProfile.EnvironmentUrl}");
                Log($"  Organization : {service.ConnectedOrgFriendlyName}");
                Log($"  UserId       : {response.UserId}");
                Log($"  BusinessUnit : {response.BusinessUnitId}");
                Log($"  OrgId        : {response.OrganizationId}");
                Log(string.Empty);
            }
            catch (Exception ex)
            {
                Log($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                UpdateState();
            }
        }

        private void Log(string message) =>
            _output.AppendText(message + Environment.NewLine);

        /// <summary>Per-cloud settings, so GCC High can use its own registration.</summary>
        private static DataverseAuthOptions BuildAuthOptions(DataverseCloud cloud)
        {
            string Setting(string key) =>
                ConfigurationManager.AppSettings[$"{key}.{cloud}"] ?? ConfigurationManager.AppSettings[key];

            return new DataverseAuthOptions
            {
                ClientId = Setting("ClientId"),
                TenantId = Setting("TenantId") ?? "organizations",
                RedirectUri = Setting("RedirectUri") ?? "http://localhost",
                Cloud = cloud
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _manager.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
