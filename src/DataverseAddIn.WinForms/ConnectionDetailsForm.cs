using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// Collects the label for a connection: display name and an environment colour. Also
    /// collects the URL when the environment was typed rather than picked from discovery.
    /// </summary>
    public sealed class ConnectionDetailsForm : Form
    {
        /// <summary>Distinguishable at a glance and readable with white text.</summary>
        private static readonly string[] Palette =
        {
            "#C62828", "#EF6C00", "#F9A825", "#2E7D32", "#00838F",
            "#1565C0", "#4527A0", "#AD1457", "#4E342E", "#37474F"
        };

        private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _url = new TextBox { Dock = DockStyle.Fill };
        private readonly Label _detected = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
        private readonly ComboBox _authKind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _authNotes = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
        private readonly Dictionary<AuthField, AuthFieldRow> _authFields = new Dictionary<AuthField, AuthFieldRow>();
        private readonly FlowLayoutPanel _swatches = new FlowLayoutPanel();
        private readonly Button _test = FormScaling.CreateButton("Test connection");
        private readonly Label _testResult = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
        private readonly Button _ok = FormScaling.CreateButton("OK", DialogResult.OK);
        private readonly bool _urlIsFixed;
        private readonly bool _secretAlreadySaved;
        private readonly Func<DataverseEnvironmentReference, ConnectionAuthentication, CancellationToken, Task<string>> _tester;

        /// <summary>Manual entry: the user types the URL.</summary>
        public ConnectionDetailsForm() : this(null, null, null)
        {
        }

        /// <summary>The environment is already known, typically chosen from Global Discovery.</summary>
        public ConnectionDetailsForm(
            DataverseEnvironmentReference environment,
            string suggestedName,
            string color,
            ConnectionAuthentication authentication = null,
            bool secretAlreadySaved = false,
            Func<DataverseEnvironmentReference, ConnectionAuthentication, CancellationToken, Task<string>> tester = null)
        {
            this.ApplyScaling();

            Environment = environment;
            ColorHex = color ?? Palette[0];
            _urlIsFixed = environment != null;
            _secretAlreadySaved = secretAlreadySaved;
            _tester = tester;

            Text = _urlIsFixed ? "Connection details" : "Add connection by URL";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Icon = Glyphs.CreateIcon(Glyphs.Edit, Color.FromArgb(0x21, 0x5F, 0x9A));

            _name.Text = suggestedName ?? string.Empty;
            _name.MinimumSize = new Size(360, 0);
            _name.TextChanged += (s, e) => UpdateOkState();

            _url.Text = environment?.Url ?? string.Empty;
            _url.MinimumSize = new Size(360, 0);
            _url.ReadOnly = _urlIsFixed;
            _url.TextChanged += (s, e) => OnUrlChanged();

            if (_urlIsFixed) _url.BackColor = SystemColors.Control;

            _detected.ForeColor = SystemColors.GrayText;
            _detected.Margin = new Padding(0, 4, 0, 8);

            _authNotes.ForeColor = SystemColors.GrayText;
            _authNotes.Margin = new Padding(0, 4, 0, 8);

            BuildAuthFields();
            BuildAuthKinds(authentication);

            _swatches.AutoSize = true;
            _swatches.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _swatches.FlowDirection = FlowDirection.LeftToRight;
            _swatches.WrapContents = true;
            _swatches.MaximumSize = new Size(460, 0);
            _swatches.Margin = new Padding(0);
            BuildSwatches();

            var custom = FormScaling.CreateButton("Custom...");
            custom.Margin = new Padding(0, 6, 0, 0);
            custom.Click += OnPickCustomColor;

            var cancel = FormScaling.CreateButton("Cancel", DialogResult.Cancel);

            _test.Visible = _tester != null;
            _test.Margin = new Padding(0, 6, 0, 0);
            _test.Click += OnTestAsync;

            _testResult.ForeColor = SystemColors.GrayText;
            _testResult.Margin = new Padding(0, 4, 0, 0);
            _testResult.Visible = false;

            var layout = FormScaling.CreateLayout(2, 10 + _authFields.Count);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var row = 0;
            layout.Controls.Add(FormScaling.CreateLabel("Name"), 0, row);
            layout.Controls.Add(_name, 1, row++);
            layout.Controls.Add(FormScaling.CreateLabel("Environment URL"), 0, row);
            layout.Controls.Add(_url, 1, row++);
            layout.Controls.Add(_detected, 1, row++);
            layout.Controls.Add(FormScaling.CreateLabel("Authentication"), 0, row);
            layout.Controls.Add(_authKind, 1, row++);
            layout.Controls.Add(_authNotes, 1, row++);

            foreach (var field in _authFields.Values)
            {
                layout.Controls.Add(field.Label, 0, row);
                layout.Controls.Add(field.Editor, 1, row++);
            }

            layout.Controls.Add(FormScaling.CreateLabel("Colour"), 0, row);
            layout.Controls.Add(_swatches, 1, row++);
            layout.Controls.Add(custom, 1, row++);
            layout.Controls.Add(_test, 1, row++);
            layout.Controls.Add(_testResult, 1, row++);
            layout.Controls.Add(FormScaling.CreateButtonRow(cancel, _ok), 1, row);

            for (var i = 0; i < layout.RowCount; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(layout);

            AcceptButton = _ok;
            CancelButton = cancel;

            ApplyAuthentication(authentication);
            OnAuthKindChanged();
            OnUrlChanged();
        }

        public DataverseEnvironmentReference Environment { get; private set; }

        public string ConnectionName => _name.Text.Trim();

        /// <summary>Hex colour, e.g. <c>#C62828</c>.</summary>
        public string ColorHex { get; private set; }

        /// <summary>What the user chose. A blank secret means "keep whatever is already saved".</summary>
        public ConnectionAuthentication Authentication => new ConnectionAuthentication
        {
            Kind = SelectedKind,
            ClientId = ValueOf(AuthField.ClientId),
            TenantId = ValueOf(AuthField.TenantId),
            UserName = ValueOf(AuthField.UserName),
            ClientSecret = ValueOf(AuthField.ClientSecret),
            CertificateThumbprint = ValueOf(AuthField.CertificateThumbprint)
        };

        private AuthKindDescriptor SelectedDescriptor =>
            (AuthKindDescriptor)_authKind.SelectedItem ?? AuthKindDescriptor.Supported[0];

        private DataverseAuthKind SelectedKind => SelectedDescriptor.Kind;

        private void BuildAuthFields()
        {
            Add(AuthField.ClientId, "Application (client) ID");
            Add(AuthField.TenantId, "Directory (tenant) ID");
            Add(AuthField.UserName, "User name");
            Add(AuthField.ClientSecret, "Client secret", isSecret: true);
            Add(AuthField.CertificateThumbprint, "Certificate thumbprint");

            void Add(AuthField field, string label, bool isSecret = false)
            {
                var editor = new TextBox { Dock = DockStyle.Fill, MinimumSize = new Size(360, 0) };

                if (isSecret) editor.UseSystemPasswordChar = true;

                editor.TextChanged += (s, e) => UpdateOkState();

                _authFields[field] = new AuthFieldRow(FormScaling.CreateLabel(label), editor);
            }
        }

        private void BuildAuthKinds(ConnectionAuthentication authentication)
        {
            foreach (var descriptor in AuthKindDescriptor.Supported)
                _authKind.Items.Add(descriptor);

            var wanted = authentication?.Kind ?? DataverseAuthKind.Interactive;

            _authKind.SelectedItem = AuthKindDescriptor.TryGet(wanted, out var chosen)
                ? chosen
                : AuthKindDescriptor.Supported[0];

            _authKind.SelectedIndexChanged += (s, e) => OnAuthKindChanged();
        }

        private void ApplyAuthentication(ConnectionAuthentication authentication)
        {
            if (authentication == null) return;

            _authFields[AuthField.ClientId].Editor.Text = authentication.ClientId ?? string.Empty;
            _authFields[AuthField.TenantId].Editor.Text = authentication.TenantId ?? string.Empty;
            _authFields[AuthField.UserName].Editor.Text = authentication.UserName ?? string.Empty;
            _authFields[AuthField.CertificateThumbprint].Editor.Text = authentication.CertificateThumbprint ?? string.Empty;
        }

        private void OnAuthKindChanged()
        {
            var descriptor = SelectedDescriptor;
            var shown = descriptor.RequiredFields | descriptor.OptionalFields;

            foreach (var pair in _authFields)
            {
                var applicable = (shown & pair.Key) == pair.Key;
                var required = (descriptor.RequiredFields & pair.Key) == pair.Key;

                pair.Value.SetApplicable(applicable);
                pair.Value.Label.Text = pair.Value.Caption + (required ? " *" : string.Empty);
            }

            var secret = _authFields[AuthField.ClientSecret];

            // .NET Framework WinForms has no PlaceholderText, so the hint goes in the label.
            if (_secretAlreadySaved && (shown & AuthField.ClientSecret) == AuthField.ClientSecret)
                secret.Label.Text = secret.Caption + " (blank keeps the saved one)";

            _authNotes.ForeColor = descriptor.Warning == null ? SystemColors.GrayText : Color.DarkOrange;
            _authNotes.Text = descriptor.Warning == null
                ? descriptor.Description
                : descriptor.Description + "\r\n" + descriptor.Warning;

            UpdateOkState();
        }

        private string ValueOf(AuthField field)
        {
            var row = _authFields[field];

            if (!row.IsApplicable) return null;

            var text = row.Editor.Text.Trim();
            return text.Length == 0 ? null : text;
        }

        private bool RequiredFieldsSupplied()
        {
            var descriptor = SelectedDescriptor;

            foreach (var pair in _authFields)
            {
                if ((descriptor.RequiredFields & pair.Key) != pair.Key) continue;

                // An already-saved secret does not have to be retyped to save an edit.
                if (pair.Key == AuthField.ClientSecret && _secretAlreadySaved) continue;

                if (ValueOf(pair.Key) == null) return false;
            }

            return true;
        }

        private void BuildSwatches()
        {
            _swatches.Controls.Clear();

            foreach (var hex in Palette)
            {
                var swatch = new Button
                {
                    BackColor = ColorTranslator.FromHtml(hex),
                    FlatStyle = FlatStyle.Flat,
                    Tag = hex,
                    Size = new Size(32, 26),
                    Margin = new Padding(0, 0, 6, 0)
                };

                swatch.FlatAppearance.BorderSize = 1;
                swatch.Click += (s, e) => SelectColor((string)((Button)s).Tag);

                _swatches.Controls.Add(swatch);
            }

            HighlightSelectedSwatch();
        }

        private void SelectColor(string hex)
        {
            ColorHex = hex;
            HighlightSelectedSwatch();
        }

        private void HighlightSelectedSwatch()
        {
            foreach (Button swatch in _swatches.Controls)
            {
                var selected = string.Equals((string)swatch.Tag, ColorHex, StringComparison.OrdinalIgnoreCase);

                swatch.FlatAppearance.BorderSize = selected ? 3 : 1;
                swatch.FlatAppearance.BorderColor = selected ? SystemColors.WindowText : SystemColors.ControlDark;
            }
        }

        private void OnPickCustomColor(object sender, EventArgs e)
        {
            using (var dialog = new ColorDialog { Color = SafeColor(ColorHex) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                ColorHex = ColorTranslator.ToHtml(dialog.Color);
                HighlightSelectedSwatch();
            }
        }

        private void OnUrlChanged()
        {
            if (_urlIsFixed)
            {
                _detected.Text = $"{Environment.Cloud.GetDisplayName()}\r\nSign-in: {Environment.Cloud.GetAuthorityHost()}";
                UpdateOkState();
                return;
            }

            if (!DataverseEnvironmentReference.TryParse(_url.Text, out var environment, out var error))
            {
                Environment = null;
                _detected.ForeColor = SystemColors.GrayText;
                _detected.Text = string.IsNullOrWhiteSpace(_url.Text) ? string.Empty : error;
                UpdateOkState();
                return;
            }

            Environment = environment;

            _detected.ForeColor = environment.CloudWasRecognized ? SystemColors.GrayText : Color.DarkOrange;
            _detected.Text = environment.CloudWasRecognized
                ? $"{environment.Url}\r\n{environment.Cloud.GetDisplayName()}  •  Sign-in: {environment.Cloud.GetAuthorityHost()}"
                : $"{environment.Url}\r\nHost matches no known Dataverse suffix; assuming {environment.Cloud.GetDisplayName()}.";

            // TextBox.Modified is false until the user edits it, so a typed name is never
            // overwritten by a later URL change.
            if (!_name.Modified)
            {
                _name.Text = ConnectionProfile.SuggestName(environment);
                _name.Modified = false;
            }

            UpdateOkState();
        }

        private void UpdateOkState()
        {
            var ready = Environment != null && _name.Text.Trim().Length > 0 && RequiredFieldsSupplied();

            _ok.Enabled = ready;
            _test.Enabled = ready;
        }

        /// <summary>
        /// Proves the credential works before it is saved, which for a service principal is the
        /// difference between finding out now and finding out at connect time.
        /// </summary>
        private async void OnTestAsync(object sender, EventArgs e)
        {
            if (_tester == null || Environment == null) return;

            _test.Enabled = false;
            _ok.Enabled = false;
            Cursor = Cursors.WaitCursor;

            _testResult.Visible = true;
            _testResult.ForeColor = SystemColors.GrayText;
            _testResult.Text = "Testing...";

            try
            {
                var description = await _tester(Environment, Authentication, CancellationToken.None)
                    .ConfigureAwait(true);

                _testResult.ForeColor = Color.ForestGreen;
                _testResult.Text = description;
            }
            catch (Exception ex)
            {
                _testResult.ForeColor = Color.Firebrick;
                _testResult.Text = ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
                UpdateOkState();
            }
        }

        private static Color SafeColor(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch (Exception) { return SystemColors.Highlight; }
        }

        /// <summary>A label and editor pair that hides together, collapsing its auto-sized row.</summary>
        private sealed class AuthFieldRow
        {
            public AuthFieldRow(Label label, TextBox editor)
            {
                Label = label;
                Editor = editor;
                Caption = label.Text;
            }

            public Label Label { get; }

            public TextBox Editor { get; }

            public string Caption { get; }

            /// <summary>
            /// Whether the chosen kind uses this field. Tracked rather than read back from
            /// <see cref="Control.Visible"/>, which is false whenever the form is not on screen —
            /// including after the dialog closes, which is exactly when callers read the result.
            /// </summary>
            public bool IsApplicable { get; private set; }

            public void SetApplicable(bool applicable)
            {
                IsApplicable = applicable;
                Label.Visible = applicable;
                Editor.Visible = applicable;

                if (!applicable) Editor.Clear();
            }
        }
    }
}
