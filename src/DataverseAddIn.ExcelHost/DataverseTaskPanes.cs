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

            var pane = Find(window) ?? Create(window);
            if (pane != null) pane.Visible = visible;
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
            pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;

            // Width is in POINTS, not pixels, and only settable while docked left or right —
            // setting it on a top/bottom dock throws COMException.
            pane.Width = DefaultWidthInPoints;

            _panes[window.Hwnd] = pane;

            return pane;
        }
    }
}
