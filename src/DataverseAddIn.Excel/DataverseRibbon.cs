using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataverseAddIn.WinForms;
using Microsoft.Crm.Sdk.Messages;
using Office = Microsoft.Office.Core;

namespace DataverseAddIn.Excel
{
    [ComVisible(true)]
    public class DataverseRibbon : Office.IRibbonExtensibility
    {
        private Office.IRibbonUI _ribbon;

        public string GetCustomUI(string ribbonId) =>
            GetResourceText("DataverseAddIn.Excel.DataverseRibbon.xml");

        public void OnLoad(Office.IRibbonUI ribbonUi)
        {
            _ribbon = ribbonUi;

            // Connecting or disconnecting must re-evaluate the Who Am I button.
            ThisAddIn.Connections.ConnectionChanged += (s, e) => _ribbon.InvalidateControl("btnWhoAmI");
        }

        public bool GetWhoAmIEnabled(Office.IRibbonControl control) =>
            ThisAddIn.Connections.IsConnected;

        public void OnConnections(Office.IRibbonControl control)
        {
            using (var dialog = new ConnectionManagerForm(ThisAddIn.Connections))
                dialog.ShowDialog(new ExcelWindow());

            _ribbon.InvalidateControl("btnWhoAmI");
        }

        /// <summary>
        /// async void is correct for a ribbon callback. Never block here: Excel's UI thread
        /// is an STA with a message pump, and waiting on these tasks deadlocks it.
        /// </summary>
        public async void OnWhoAmI(Office.IRibbonControl control)
        {
            try
            {
                var service = ThisAddIn.Connections.Current;

                var response = await Task
                    .Run(() => (WhoAmIResponse)service.Execute(new WhoAmIRequest()))
                    .ConfigureAwait(true);

                MessageBox.Show(
                    $"Organization : {service.ConnectedOrgFriendlyName}\n" +
                    $"UserId       : {response.UserId}\n" +
                    $"BusinessUnit : {response.BusinessUnitId}\n" +
                    $"OrgId        : {response.OrganizationId}",
                    "Who Am I", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Who Am I failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetResourceText(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        $"Embedded resource '{resourceName}' not found. Set the .xml file's Build Action to Embedded Resource.");

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Parents dialogs to Excel so they cannot appear behind the workbook.</summary>
        private sealed class ExcelWindow : IWin32Window
        {
            public IntPtr Handle => new IntPtr(Globals.ThisAddIn.Application.Hwnd);
        }
    }
}
