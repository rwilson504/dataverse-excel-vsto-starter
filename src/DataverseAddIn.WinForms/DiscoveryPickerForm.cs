using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.WinForms
{
    /// <summary>Lists environments from Global Discovery so the user can pick one.</summary>
    public sealed class DiscoveryPickerForm : Form
    {
        private readonly DataverseConnectionManager _manager;
        private readonly ComboBox _cloud = new ComboBox { Dock = DockStyle.Fill };
        private readonly TextBox _search = new TextBox { Dock = DockStyle.Fill };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill };
        private readonly Button _load = FormScaling.CreateButton("Load environments");
        private readonly Button _ok = FormScaling.CreateButton("Add", DialogResult.OK);
        private readonly Label _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private IReadOnlyList<DataverseInstance> _loaded = new List<DataverseInstance>();
        private readonly CancelableButton _loading;

        public DiscoveryPickerForm(DataverseConnectionManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _loading = new CancelableButton(_load);

            this.ApplyScaling();

            Text = "Find my environments";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 480);
            MinimumSize = new Size(560, 380);
            Icon = Glyphs.CreateIcon(Glyphs.Search, Color.FromArgb(0x21, 0x5F, 0x9A));

            _load.SetGlyph(Glyphs.Refresh);
            _ok.SetGlyph(Glyphs.Add);

            _cloud.DropDownStyle = ComboBoxStyle.DropDownList;
            _cloud.Items.AddRange(Enum.GetValues(typeof(DataverseCloud))
                .Cast<DataverseCloud>()
                .Select(c => (object)new CloudItem(c))
                .ToArray());
            _cloud.SelectedIndex = 0;
            _cloud.Margin = new Padding(0, 3, 8, 3);

            _load.Click += OnLoadAsync;
            _load.Margin = new Padding(0, 0, 0, 0);

            _search.Enabled = false;
            _search.Margin = new Padding(0, 3, 0, 3);
            _search.TextChanged += (s, e) => ApplyFilter();

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Margin = new Padding(0, 8, 0, 8);
            _list.Columns.Add("Name", 220);
            _list.Columns.Add("Admin", 70);
            _list.Columns.Add("Version", 120);
            _list.Columns.Add("URL", 360);
            _list.SelectedIndexChanged += (s, e) => _ok.Enabled = _list.SelectedItems.Count == 1;
            _list.DoubleClick += (s, e) => { if (_list.SelectedItems.Count == 1) { DialogResult = DialogResult.OK; Close(); } };

            _status.ForeColor = SystemColors.GrayText;
            _status.MaximumSize = new Size(520, 0);
            _status.Text = AuthKindDescriptor.DiscoveryRequirement;

            _ok.Enabled = false;
            var cancel = FormScaling.CreateButton("Cancel", DialogResult.Cancel);

            // Row 0: cloud + load. Row 1: search. Row 2: list (fills). Row 3: status + buttons.
            var layout = FormScaling.CreateLayout(3, 4);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(FormScaling.CreateLabel("Cloud"), 0, 0);
            layout.Controls.Add(_cloud, 1, 0);
            layout.Controls.Add(_load, 2, 0);

            layout.Controls.Add(FormScaling.CreateLabel("Search"), 0, 1);
            layout.Controls.Add(_search, 1, 1);

            layout.Controls.Add(_list, 0, 2);
            layout.SetColumnSpan(_list, 3);

            layout.Controls.Add(_status, 0, 3);
            layout.SetColumnSpan(_status, 2);
            layout.Controls.Add(FormScaling.CreateButtonRow(cancel, _ok), 2, 3);

            Controls.Add(layout);

            AcceptButton = _ok;
            CancelButton = cancel;
        }

        public DataverseInstance SelectedInstance =>
            _list.SelectedItems.Count == 1 ? (DataverseInstance)_list.SelectedItems[0].Tag : null;

        // async void is correct for an event handler; nothing blocks the UI thread.
        private async void OnLoadAsync(object sender, EventArgs e)
        {
            if (_loading.CancelIfRunning()) return;

            var cloud = ((CloudItem)_cloud.SelectedItem).Cloud;

            _load.Enabled = false;
            _ok.Enabled = false;
            _search.Enabled = false;
            _list.Items.Clear();
            _loaded = new List<DataverseInstance>();
            _status.ForeColor = SystemColors.GrayText;
            _status.Text = $"Signing in and querying {cloud.GetDisplayName()}...";
            UseWaitCursor = true;

            try
            {
                using (var scope = _loading.Begin())
                    _loaded = await _manager.DiscoverAsync(cloud, scope.Token).ConfigureAwait(true);

                _search.Enabled = _loaded.Count > 0;
                ApplyFilter();

                if (_loaded.Count == 0)
                    _status.Text = "No environments returned for this account in this cloud.";
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is SignInCanceledException)
            {
                _status.ForeColor = SystemColors.GrayText;
                _status.Text = ex is SignInCanceledException ? ex.Message : "Sign-in cancelled.";
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = ex.Message;
            }
            finally
            {
                UseWaitCursor = false;
                _load.Enabled = true;
            }
        }

        /// <summary>Matches on name, URL, region and version so any visible column can be searched.</summary>
        private void ApplyFilter()
        {
            var term = _search.Text.Trim();

            var matches = _loaded
                .Where(i => Matches(i, term))
                .OrderBy(i => i.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var instance in matches)
            {
                var item = new ListViewItem(instance.FriendlyName ?? string.Empty) { Tag = instance };
                item.SubItems.Add(instance.IsUserSysAdmin ? "yes" : "no");
                item.SubItems.Add(instance.Version ?? string.Empty);
                item.SubItems.Add(instance.ApiUrl ?? instance.Url ?? string.Empty);
                _list.Items.Add(item);
            }

            _list.EndUpdate();

            _ok.Enabled = false;

            if (_loaded.Count == 0) return;

            _status.ForeColor = SystemColors.GrayText;
            _status.Text = matches.Count == _loaded.Count
                ? $"{_loaded.Count} environment(s)."
                : $"{matches.Count} of {_loaded.Count} environment(s).";
        }

        private static bool Matches(DataverseInstance instance, string term)
        {
            if (term.Length == 0) return true;

            return Contains(instance.FriendlyName, term)
                   || Contains(instance.ApiUrl, term)
                   || Contains(instance.Url, term)
                   || Contains(instance.UniqueName, term)
                   || Contains(instance.Region, term)
                   || Contains(instance.Version, term);
        }

        private static bool Contains(string value, string term) =>
            value != null && value.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;

        /// <summary>Gives the combo box readable text without losing the enum value.</summary>
        private sealed class CloudItem
        {
            public CloudItem(DataverseCloud cloud) => Cloud = cloud;

            public DataverseCloud Cloud { get; }

            public override string ToString() => Cloud.GetDisplayName();
        }
    }
}
