using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// Turns the button that starts a long operation into the button that cancels it, so the
    /// user is never left looking at a disabled window with no way out.
    /// </summary>
    /// <remarks>
    /// Reuses the starting button rather than adding a second one, because each form lays its
    /// buttons out in fixed grid cells. It also keeps the existing Click handler and has that
    /// handler ask <see cref="CancelIfRunning"/> first, since WinForms offers no way to detach
    /// an arbitrary handler and put it back afterwards.
    /// </remarks>
    public sealed class CancelableButton
    {
        private readonly Button _button;
        private CancellationTokenSource _running;

        public CancelableButton(Button button)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            _button = button;
        }

        public bool IsRunning => _running != null;

        /// <summary>Call first in the Click handler; <c>true</c> means the click meant "cancel".</summary>
        public bool CancelIfRunning()
        {
            if (_running == null) return false;

            // Cancelling is not instant — don't invite a second press meanwhile.
            _button.Enabled = false;
            _running.Cancel();

            return true;
        }

        /// <summary>
        /// Offers cancellation until the returned scope is disposed. Call after the form has
        /// disabled its controls, since this deliberately re-enables the one button.
        /// </summary>
        public Scope Begin(string caption = "Cancel")
        {
            if (_running != null)
                throw new InvalidOperationException("This button is already running an operation.");

            _running = new CancellationTokenSource();

            return new Scope(this, caption);
        }

        public sealed class Scope : IDisposable
        {
            private readonly CancelableButton _owner;
            private readonly string _caption;
            private readonly Image _glyph;

            internal Scope(CancelableButton owner, string caption)
            {
                _owner = owner;
                _caption = owner._button.Text;
                _glyph = owner._button.Image;

                owner._button.Text = caption;
                owner._button.SetGlyph(Glyphs.Cancel);
                owner._button.Enabled = true;
            }

            public CancellationToken Token => _owner._running.Token;

            public void Dispose()
            {
                _owner._button.Text = _caption;
                _owner._button.Image = _glyph;

                _owner._running.Dispose();
                _owner._running = null;
            }
        }
    }
}
