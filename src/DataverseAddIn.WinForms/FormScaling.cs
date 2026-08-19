using System;
using System.Drawing;
using System.Windows.Forms;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// Shared setup so every dialog scales with the user's font and display scaling.
    /// </summary>
    /// <remarks>
    /// WinForms only applies font scaling when <see cref="ContainerControl.AutoScaleDimensions"/>
    /// is set — the designer normally emits it, so hand-built forms silently get no scaling at
    /// all and clip their text at anything above 100%. The values below are Segoe UI 9pt at
    /// 96 DPI, the baseline these layouts are written against.
    /// </remarks>
    public static class FormScaling
    {
        private static readonly SizeF DesignDimensions = new SizeF(7F, 15F);

        public static void ApplyScaling(this ContainerControl control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));

            // Default WinForms font is Microsoft Sans Serif 8.25pt; use the shell's UI font.
            control.Font = SystemFonts.MessageBoxFont;
            control.AutoScaleDimensions = DesignDimensions;
            control.AutoScaleMode = AutoScaleMode.Font;
        }

        /// <summary>A table layout that grows with its content rather than fixed pixels.</summary>
        public static TableLayoutPanel CreateLayout(int columns, int rows) =>
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                RowCount = rows,
                Padding = new Padding(12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

        public static FlowLayoutPanel CreateButtonRow(params Control[] buttonsRightToLeft)
        {
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 8, 0, 0),
                WrapContents = false
            };

            flow.Controls.AddRange(buttonsRightToLeft);
            return flow;
        }

        public static Label CreateLabel(string text) =>
            new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 6)
            };

        public static Button CreateButton(string text, DialogResult result = DialogResult.None) =>
            new Button
            {
                Text = text,
                DialogResult = result,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 28),
                Margin = new Padding(6, 0, 0, 0),
                Padding = new Padding(8, 2, 8, 2)
            };
    }
}
