using System;
using System.Configuration;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;
using Office = Microsoft.Office.Core;

namespace DataverseAddIn.ExcelHost
{
    public partial class ThisAddIn
    {
        private DataverseRibbon _ribbon;
        private static readonly object ConnectionsGate = new object();
        private static DataverseConnectionManager _connections;

        /// <summary>The Dataverse task pane, one per workbook window. Created on demand.</summary>
        internal static DataverseTaskPanes TaskPanes { get; } = new DataverseTaskPanes();

        /// <summary>
        /// One manager for the lifetime of the add-in, so sign-in is shared. Created on first
        /// use rather than in Startup, because Office loads the ribbon and raises its OnLoad
        /// before ThisAddIn_Startup runs.
        /// </summary>
        internal static DataverseConnectionManager Connections
        {
            get
            {
                lock (ConnectionsGate)
                    return _connections ?? (_connections = new DataverseConnectionManager(BuildAuthOptions));
            }
        }

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            // Panes belong to a window; when its workbook closes the pane must go with it.
            Application.WorkbookDeactivate += workbook => TaskPanes.ReleaseClosedWindows();
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            lock (ConnectionsGate)
            {
                _connections?.Dispose();
                _connections = null;
            }
        }

        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject() =>
            _ribbon = new DataverseRibbon();

        /// <summary>
        /// A VSTO add-in does not read App.config; settings come from the add-in's own
        /// DataverseAddIn.ExcelHost.dll.config, which ConfigurationManager resolves for the loaded assembly.
        /// </summary>
        private static DataverseAuthOptions BuildAuthOptions(DataverseCloud cloud)
        {
            string Setting(string key) =>
                ConfigurationManager.AppSettings[$"{key}.{cloud}"] ?? ConfigurationManager.AppSettings[key];

            return new DataverseAuthOptions
            {
                ClientId = Setting("ClientId"),
                TenantId = Setting("TenantId") ?? "organizations",
                RedirectUri = Setting("RedirectUri") ?? "http://localhost",
                Cloud = cloud,

                // Parent the sign-in window to Excel so it cannot open behind it.
                ParentWindowHandleProvider = () => new IntPtr(Globals.ThisAddIn.Application.Hwnd)
            };
        }

        #region VSTO generated code

        private void InternalStartup()
        {
            Startup += ThisAddIn_Startup;
            Shutdown += ThisAddIn_Shutdown;
        }

        #endregion
    }
}
