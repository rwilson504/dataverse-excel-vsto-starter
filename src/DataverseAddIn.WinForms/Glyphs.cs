using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// Icons drawn from the Segoe icon fonts shipped with Windows.
    /// </summary>
    /// <remarks>
    /// Chosen over bitmap resources because they are vector: one call renders crisply at any
    /// display scaling, with no 16/24/32/48px asset set to maintain and nothing to ship.
    /// Segoe Fluent Icons is preferred (Windows 11) and Segoe MDL2 Assets is the fallback
    /// (Windows 10). If neither is installed the helpers return null and callers simply show
    /// text, so this can never be the reason a dialog fails to open.
    /// </remarks>
    public static class Glyphs
    {
        // Codepoints verified to render in both fonts.
        public const string Add = "\uE710";
        public const string Edit = "\uE70F";
        public const string Delete = "\uE74D";
        public const string Connect = "\uE71B";
        public const string Disconnect = "\uE711";
        public const string Search = "\uE721";
        public const string Cloud = "\uE753";
        public const string Contact = "\uE77B";
        public const string Refresh = "\uE72C";
        public const string Upload = "\uE898";
        public const string Table = "\uE80A";

        private static readonly string FontName = ResolveFontName();

        public static bool IsAvailable => FontName != null;

        /// <summary>
        /// Renders a glyph sized relative to <paramref name="referenceControl"/>'s font, so the
        /// icon tracks the display scaling that the surrounding text already follows.
        /// </summary>
        public static Image CreateImage(string glyph, Control referenceControl, Color? color = null)
        {
            if (referenceControl == null) throw new ArgumentNullException(nameof(referenceControl));

            return CreateImage(glyph, (int)Math.Round(referenceControl.Font.Height * 0.85), color);
        }

        public static Image CreateImage(string glyph, int size, Color? color = null)
        {
            if (FontName == null || string.IsNullOrEmpty(glyph) || size < 4) return null;

            var bitmap = new Bitmap(size, size);
            bitmap.SetResolution(96f, 96f);

            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font(FontName, size * 0.62f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color ?? SystemColors.ControlText))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            }

            return bitmap;
        }

        /// <summary>Window icon for a dialog. Returns null when no icon font is present.</summary>
        public static Icon CreateIcon(string glyph, Color color, int size = 32)
        {
            using (var image = CreateImage(glyph, size, color))
            {
                if (image == null) return null;

                var handle = ((Bitmap)image).GetHicon();

                try
                {
                    // Clone so the icon survives DestroyIcon; FromHandle does not own the handle.
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        /// <summary>Adds an icon to a button, left of its text. No-op when unavailable.</summary>
        public static void SetGlyph(this Button button, string glyph, Color? color = null)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));

            var image = CreateImage(glyph, button, color);
            if (image == null) return;

            button.Image = image;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(6, 2, 10, 2);
        }

        private static string ResolveFontName()
        {
            var installed = new InstalledFontCollection().Families.Select(f => f.Name).ToList();

            return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
                .FirstOrDefault(name => installed.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
