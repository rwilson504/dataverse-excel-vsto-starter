using System;
using System.Drawing;
using System.Windows.Forms;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// The connection manager: list saved connections, add one by URL or via Global
    /// Discovery, edit, delete, and connect.
    /// </summary>
    public sealed class ConnectionManagerForm : Form
    {
        private readonly DataverseConnectionManager _manager;
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill };
        private readonly ImageList _swatches = new ImageList { ImageSize = new Size(14, 14) };
        private readonly Button _addUrl = FormScaling.CreateButton("Add by URL...");
        private readonly Button _addDiscovery = FormScaling.CreateButton("Add from discovery...");
        private readonly Button _edit = FormScaling.CreateButton("Edit...");
        private readonly Button _delete = FormScaling.CreateButton("Delete");
        private readonly Button _connect = FormScaling.CreateButton("Connect");
        private readonly Button _disconnect = FormScaling.CreateButton("Disconnect");
        private readonly Label _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left };

        public ConnectionManagerForm(DataverseConnectionManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            this.ApplyScaling();

            Text = "Dataverse connections";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(880, 440);
            MinimumSize = new Size(620, 360);
            Icon = Glyphs.CreateIcon(Glyphs.Cloud, Color.FromArgb(0x21, 0x5F, 0x9A));

            _addUrl.SetGlyph(Glyphs.Add);
            _addDiscovery.SetGlyph(Glyphs.Cloud);
            _edit.SetGlyph(Glyphs.Edit);
            _delete.SetGlyph(Glyphs.Delete, Color.FromArgb(0xB3, 0x2A, 0x2A));
            _connect.SetGlyph(Glyphs.Connect, Color.FromArgb(0x2E, 0x7D, 0x32));
            _disconnect.SetGlyph(Glyphs.Disconnect);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.SmallImageList = _swatches;
            _list.Margin = new Padding(0, 0, 12, 8);
            _list.Columns.Add("Name", 190);
            _list.Columns.Add("Cloud", 220);
            _list.Columns.Add("Sign-in", 190);
            _list.Columns.Add("Environment URL", 320);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += OnConnectAsync;

            _addUrl.Click += OnAddByUrl;
            _addDiscovery.Click += OnAddFromDiscovery;
            _edit.Click += OnEdit;
            _delete.Click += OnDelete;
            _connect.Click += OnConnectAsync;
            _disconnect.Click += (s, e) => { _manager.Disconnect(); Reload(); UpdateStatus(); };

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
            };

            foreach (var button in new[] { _addUrl, _addDiscovery, _edit, _delete, _connect, _disconnect })
            {
                button.Margin = new Padding(0, 0, 0, 6);
                button.Width = 0;
                button.AutoSize = true;
                buttons.Controls.Add(button);
            }

            _status.MaximumSize = new Size(600, 0);

            var close = FormScaling.CreateButton("Close", DialogResult.OK);

            // Column 0 fills with the grid; column 1 is the button stack.
            var layout = FormScaling.CreateLayout(2, 2);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(_list, 0, 0);
            layout.Controls.Add(buttons, 1, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(FormScaling.CreateButtonRow(close), 1, 1);

            Controls.Add(layout);

            CancelButton = close;

            Reload();
            UpdateStatus();
        }

        private ConnectionProfile Selected =>
            _list.SelectedItems.Count == 1 ? (ConnectionProfile)_list.SelectedItems[0].Tag : null;

        private void Reload()
        {
            var selectedId = Selected?.Id;

            _list.BeginUpdate();
            _list.Items.Clear();
            RebuildSwatches();

            for (var i = 0; i < _manager.Profiles.Count; i++)
            {
                var profile = _manager.Profiles[i];

                var item = new ListViewItem(profile.Name ?? string.Empty, i) { Tag = profile };
                item.SubItems.Add(profile.Cloud.GetDisplayName());
                item.SubItems.Add(DescribeAuthKind(profile));
                item.SubItems.Add(profile.EnvironmentUrl ?? string.Empty);

                if (_manager.CurrentProfile != null && _manager.CurrentProfile.Id == profile.Id)
                    item.Font = new Font(_list.Font, FontStyle.Bold);

                _list.Items.Add(item);

                if (selectedId == profile.Id) item.Selected = true;
            }

            _list.EndUpdate();

            UpdateButtons();
        }

        /// <summary>
        /// How the connection signs in, and as whom. A profile can name a kind this build no
        /// longer implements, so the enum name is the fallback rather than an exception.
        /// </summary>
        private static string DescribeAuthKind(ConnectionProfile profile)
        {
            var name = AuthKindDescriptor.TryGet(profile.AuthKind, out var descriptor)
                ? descriptor.DisplayName
                : profile.AuthKind + " (unsupported)";

            var who = profile.AuthKind == DataverseAuthKind.Interactive ? profile.UserName : profile.ClientId;

            return string.IsNullOrWhiteSpace(who) ? name : $"{name} — {who}";
        }

        /// <summary>One swatch image per profile, index-aligned with the rows added in Reload.</summary>
        private void RebuildSwatches()
        {
            foreach (Image image in _swatches.Images) image.Dispose();
            _swatches.Images.Clear();

            foreach (var profile in _manager.Profiles)
            {
                var bitmap = new Bitmap(_swatches.ImageSize.Width, _swatches.ImageSize.Height);

                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(ParseColor(profile.Color)))
                {
                    graphics.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
                    graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }

                _swatches.Images.Add(bitmap);
            }
        }

        private void UpdateButtons()
        {
            var hasSelection = Selected != null;

            _connect.Enabled = hasSelection;
            _edit.Enabled = hasSelection;
            _delete.Enabled = hasSelection;
            _disconnect.Enabled = _manager.IsConnected;
        }

        internal static Color ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return SystemColors.ControlDark;

            try { return ColorTranslator.FromHtml(hex); }
            catch (Exception) { return SystemColors.ControlDark; }
        }

        private void UpdateStatus()
        {
            if (_manager.IsConnected)
            {
                _status.ForeColor = Color.ForestGreen;
                _status.Text = $"Connected to {_manager.CurrentProfile.Name} ({_manager.CurrentProfile.EnvironmentUrl})" +
                               $"  •  {DescribeAuthKind(_manager.CurrentProfile)}";
            }
            else
            {
                _status.ForeColor = SystemColors.GrayText;
                _status.Text = "Not connected.";
            }
        }

        private void OnAddByUrl(object sender, EventArgs e)
        {
            using (var dialog = new ConnectionDetailsForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Environment == null)
                    return;

                if (_manager.AlreadySaved(dialog.Environment.Url) &&
                    !Confirm($"{dialog.Environment.Url} is already saved. Add it again?"))
                    return;

                _manager.Add(dialog.ConnectionName, dialog.Environment.Url, dialog.ColorHex, dialog.Authentication);
                Reload();
            }
        }

        private void OnAddFromDiscovery(object sender, EventArgs e)
        {
            DataverseInstance instance;

            using (var picker = new DiscoveryPickerForm(_manager))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedInstance == null)
                    return;

                instance = picker.SelectedInstance;
            }

            using (var details = new ConnectionDetailsForm(
                       DataverseEnvironmentReference.FromInstance(instance), instance.FriendlyName, null))
            {
                if (details.ShowDialog(this) != DialogResult.OK) return;

                _manager.AddDiscovered(instance, details.ConnectionName, details.ColorHex, details.Authentication);
                Reload();
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            var profile = Selected;
            if (profile == null) return;

            using (var dialog = new ConnectionDetailsForm(
                       profile.ToEnvironmentReference(),
                       profile.Name,
                       profile.Color,
                       ConnectionAuthentication.FromProfile(profile),
                       secretAlreadySaved: profile.SecretRef != null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                profile.Name = dialog.ConnectionName;
                profile.NameIsAuto = false;
                profile.Color = dialog.ColorHex;

                _manager.UpdateAuthentication(profile, dialog.Authentication);
                Reload();
                UpdateStatus();
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            var profile = Selected;
            if (profile == null) return;

            if (!Confirm($"Delete the connection '{profile.Name}'?"))
                return;

            _manager.Delete(profile);
            Reload();
            UpdateStatus();
        }

        private async void OnConnectAsync(object sender, EventArgs e)
        {
            var profile = Selected;
            if (profile == null) return;

            SetBusy(true, $"Connecting to {profile.Name}...");

            try
            {
                await _manager.ConnectAsync(profile).ConfigureAwait(true);
                Reload();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = ex.Message;
            }
            finally
            {
                SetBusy(false, null);
                UpdateButtons();
            }
        }

        private void SetBusy(bool busy, string message)
        {
            UseWaitCursor = busy;
            _list.Enabled = !busy;

            foreach (var button in new[] { _addUrl, _addDiscovery, _edit, _delete, _connect, _disconnect })
                button.Enabled = !busy;

            if (message != null)
            {
                _status.ForeColor = SystemColors.GrayText;
                _status.Text = message;
            }
        }

        private bool Confirm(string message) =>
            MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Image image in _swatches.Images) image.Dispose();
                _swatches.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
