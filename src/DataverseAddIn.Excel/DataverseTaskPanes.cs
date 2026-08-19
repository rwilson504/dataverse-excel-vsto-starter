using System;
using System.Collections.Generic;
using DataverseAddIn.WinForms;
using Microsoft.Office.Tools;
using Office = Microsoft.Office.Core;

// Not "Excel": this project's own namespace ends in .Excel, which shadows that alias and
// makes Excel.Window bind to DataverseAddIn.Excel.Window.
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace DataverseAddIn.Excel
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
                    var _ = ((ExcelInterop.Window)entry.Value.Window).Hwnd;
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

        private static ExcelInterop.Window ActiveWindow
        {
            get
            {
                try { return Globals.ThisAddIn.Application.ActiveWindow; }
                catch (Exception) { return null; }
            }
        }

        private CustomTaskPane Find(ExcelInterop.Window window)
        {
            if (window == null) return null;

            return _panes.TryGetValue(window.Hwnd, out var pane) ? pane : null;
        }

        private CustomTaskPane Create(ExcelInterop.Window window)
        {
            var control = new DataversePaneControl(ThisAddIn.Connections);

            var pane = Globals.ThisAddIn.CustomTaskPanes.Add(control, Title, window);
            pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
            pane.Width = 320;

            _panes[window.Hwnd] = pane;

            return pane;
        }
    }
}
