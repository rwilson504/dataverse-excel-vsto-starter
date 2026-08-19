using System;
using System.Collections.Generic;
using DataverseAddIn.WinForms;
using Microsoft.Office.Tools;
using Excel = Microsoft.Office.Interop.Excel;
using Office = Microsoft.Office.Core;

namespace DataverseAddIn.ExcelHost
{
    /// <summary>
    /// Owns the Dataverse custom task pane.
    /// </summary>
    /// <remarks>
    /// A task pane is bound to one document frame window and is visible only while that window
    /// is. Excel 2013 and later put every workbook in its own window, so a single pane would
    /// vanish the moment the user switched workbooks — hence one pane per window, keyed by the
    /// window handle.
    /// </remarks>
    internal sealed class DataverseTaskPanes
    {
        private const string Title = "Dataverse";

        /// <summary>
        /// Wide enough for an environment URL without wrapping. Office may raise it to its own
        /// minimum, and caps a docked pane at roughly half the screen.
        /// </summary>
        private const int DefaultWidthInPoints = 300;

        private readonly Dictionary<int, CustomTaskPane> _panes = new Dictionary<int, CustomTaskPane>();

        /// <summary>True when the pane for the active window is open, which drives the ribbon toggle.</summary>
        public bool IsVisible
        {
            get
            {
                var pane = Find(ActiveWindow);
                return pane != null && pane.Visible;
            }
        }

        public void Show(bool visible)
        {
            var window = ActiveWindow;
            if (window == null) return;

            var pane = Find(window);
            var isNew = pane == null;

            if (isNew) pane = Create(window);
            if (pane == null) return;

            pane.Visible = visible;

            // Office ignores Width while the pane is hidden — set in Create it opened at the
            // minimum instead. Only for a new pane, so a user's own resize survives reopening.
            // Not in a VisibleChanged handler: setting Width there throws COMException.
            if (isNew && visible) ApplyDefaultWidth(pane);
        }

        /// <summary>
        /// Reports rather than throws: Office swallows exceptions raised in ribbon callbacks, so
        /// a pane that opened at the wrong size would otherwise give no clue why.
        /// </summary>
        private void ApplyDefaultWidth(CustomTaskPane pane)
        {
            try
            {
                pane.Width = DefaultWidthInPoints;

                if (pane.Width < DefaultWidthInPoints)
                {
                    Report($"Excel set the pane to {pane.Width} points, not the {DefaultWidthInPoints} " +
                           "requested — it may be restoring a remembered width.");
                }
            }
            catch (Exception ex)
            {
                Report($"Could not set the pane width: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Writes to the pane's log if it exists, so callers need not care whether it is open.</summary>
        public void Report(string message)
        {
            var pane = Find(ActiveWindow);
            (pane?.Control as DataversePaneControl)?.Report(message);
        }

        /// <summary>
        /// Panes for a closing workbook are removed here rather than at shutdown: the VSTO
        /// runtime disposes them before ThisAddIn_Shutdown runs, so removing them there throws
        /// ObjectDisposedException.
        /// </summary>
        public void ReleaseClosedWindows()
        {
            var stale = new List<int>();

            foreach (var entry in _panes)
            {
                try
                {
                    // Touching a released COM window throws; that is the signal it is gone.
                    var _ = ((Excel.Window)entry.Value.Window).Hwnd;
                }
                catch (Exception)
                {
                    stale.Add(entry.Key);
                }
            }

            foreach (var handle in stale)
            {
                try { Globals.ThisAddIn.CustomTaskPanes.Remove(_panes[handle]); }
                catch (Exception) { }

                _panes.Remove(handle);
            }
        }

        private static Excel.Window ActiveWindow
        {
            get
            {
                try { return Globals.ThisAddIn.Application.ActiveWindow; }
                catch (Exception) { return null; }
            }
        }

        private CustomTaskPane Find(Excel.Window window)
        {
            if (window == null) return null;

            return _panes.TryGetValue(window.Hwnd, out var pane) ? pane : null;
        }

        private CustomTaskPane Create(Excel.Window window)
        {
            var control = new DataversePaneControl(ThisAddIn.Connections);

            var pane = Globals.ThisAddIn.CustomTaskPanes.Add(control, Title, window);

            // Width is in POINTS, not pixels, and throws COMException on a top/bottom dock —
            // so the dock position has to be settled before Show applies the width.
            pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;

            _panes[window.Hwnd] = pane;

            return pane;
        }
    }
}
