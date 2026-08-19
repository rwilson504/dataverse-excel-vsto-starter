# VSTO

Everything Office-specific, and every trap that only shows up once Excel is in the picture.

## Prerequisites

The add-in needs the **Office/SharePoint development** workload, which is available in
Visual Studio 2019 and 2022 on x64.

**On Windows on ARM it is not.** The VS 2022 installer lists
`Microsoft.VisualStudio.Workload.Office` in its catalog but reports it as unavailable, and
VS 2019 — which runs emulated — is the only place it installs. That is an ARM64 limitation,
not a VS 2022 one: on an x64 machine, use VS 2022 and ignore every 2019-specific note below.
Catalog presence is not the same as installable availability.

The libraries under `src/` build fine with the .NET SDK; only `DataverseAddIn.ExcelHost` needs
Visual Studio.

## Generate a signing key first

The repository deliberately ships **no** `DataverseAddIn.ExcelHost_TemporaryKey.pfx`, so a fresh
clone fails:

```
error MSB3323: Unable to find manifest signing certificate in the certificate store.
```

Generate your own:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\new-signing-key.ps1
```

That creates an RSA 2048 / SHA-256 code-signing certificate in `Cert:\CurrentUser\My`, exports
it where the project expects it, and rewrites `<ManifestCertificateThumbprint>` in
`DataverseAddIn.ExcelHost.csproj` to match. **That `.csproj` edit is local to you — do not commit it.**

The thumbprint cannot simply be deleted instead. VSTO's `ManageCertificateStore` task requires
it:

```
error MSB4044: The "ManageCertificateStore" task was not given a value for the
required parameter "CertificateThumbprint".
```

Visual Studio's equivalent is Project Properties → **Signing** → *Create Test Certificate*,
which produces RSA 1024 rather than 2048.

> **Why the key is not in the repository.** It is a throwaway per-developer identity:
> self-signed, untrusted by Windows, valid for a year. Shipping one key with a template would
> give every user the same publisher identity — and ClickOnce keys publisher trust to the
> *certificate*, so anyone holding it could sign an update that a machine already trusting
> that publisher accepts without prompting. Worse, an organisation that adds it to Trusted
> Publishers to smooth their own deployment would silently trust every other user of the
> template. For real distribution, use a code-signing certificate from a certificate
> authority; a self-signed key always shows "Unknown Publisher" on first install.

## Building

On Windows on ARM this means VS 2019's MSBuild; on x64 use the 2022 equivalent. Either builds
everything, including the SDK-style libraries, because it uses the .NET SDK bundled with
Visual Studio rather than the .NET 10 CLI SDK — so ordinary `ProjectReference` entries work
and no `global.json` is needed.

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  DataverseExcelAddIn.sln /restore /t:Build /p:Configuration=Debug /m
```

> **Never run `dotnet build` on `DataverseExcelAddIn.sln`.** The CLI has no OfficeTools
> targets, so it fails on `DataverseAddIn.ExcelHost` — and worse, the restore writes
> `project.assets.json` and `*.nuget.g.*` into `src/DataverseAddIn.ExcelHost/obj`, after which
> Visual Studio fails with *"Your project file doesn't list 'win' as a RuntimeIdentifier"*. The fix is
> deleting that `obj` folder; adding `RuntimeIdentifiers`, which the error suggests, does not
> help.
>
> The **VS Code C# language server restores the whole solution in the background**, so those
> artifacts come back on their own. Cleaning once is not enough.
>
> Use `pwsh tools/build-and-test.ps1` instead of remembering any of this. It builds every
> project the CLI can build, runs the tests and the dialog check, and cleans the VSTO `obj`
> folder afterwards.

The solution is a classic `.sln`, not the `.slnx` that `dotnet new sln` produces by default on
the .NET 10 SDK — VSTO project types are safest in `.sln`. If you regenerate it, pass
`--format sln`.

## Running it

1. Put your own `ClientId` values in `src/DataverseAddIn.ExcelHost/app.config`. It deploys as
   `DataverseAddIn.ExcelHost.dll.config`; **a VSTO add-in does not read `App.config` from anywhere
   else.**
2. Open the solution in Visual Studio, set `DataverseAddIn.ExcelHost` as the startup project,
   press **F5**. Excel launches with a **Dataverse** tab containing **Connections** and
   **Who Am I**.

To try the UI without Office at all:

```powershell
.\samples\DataverseAddIn.Samples.WinFormsHost\bin\Debug\net462\DataverseAddIn.Samples.WinFormsHost.exe
```

It has the same two commands the ribbon does, so the whole flow is exercisable before Office
is involved.

If you rename the add-in project in a fork, delete the stale registration or Excel keeps
loading the old one:

```powershell
Remove-Item 'HKCU:\Software\Microsoft\Office\Excel\Addins\<old name>' -Recurse
```

## The deadlock

`ServiceClient` calls its token provider **synchronously** from inside its own plumbing. If
that call has to do interactive sign-in while running on Excel's STA UI thread, Excel
deadlocks.

`DataverseServiceClientFactory.CreateAsync` avoids it by acquiring the token on the async path
*first*, so the provider only ever hits the MSAL cache, and by constructing the client inside
`Task.Run`.

The rules that follow:

- **Never** call `.Result`, `.Wait()` or `GetAwaiter().GetResult()` on these tasks from the UI
  thread. `await` them. Ribbon handlers are `async void`, which is correct — they are event
  handlers.
- The libraries use `ConfigureAwait(false)` throughout, so `await` is always safe.
- `useUniqueInstance: true` is deliberate. Without it `ServiceClient` reuses cached
  connections, and an add-in that switches environments can silently keep talking to the
  previous one. Use `Clone()` for parallel work rather than sharing one instance.
- `Ingest` blocks by design — call it from a background thread.

## The task pane

Yes, VSTO has a dockable, poppable-out panel — the **custom task pane**. It hosts an ordinary
WinForms `UserControl`, docks left/right/top/bottom, and the user can drag it out to float.

`DataversePaneControl` lives in `DataverseAddIn.WinForms` and is host-agnostic; only
`DataverseTaskPanes` in the add-in knows about Office.

```csharp
var pane = Globals.ThisAddIn.CustomTaskPanes.Add(control, "Dataverse", window);
pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
pane.Width = 320;
pane.Visible = true;
```

Four things that are not obvious:

- **A pane belongs to one document frame window** and is visible only while that window is.
  Excel 2013 and later give every workbook its own window, so a single pane vanishes when the
  user switches workbooks. `DataverseTaskPanes` therefore keeps one pane per window, keyed by
  `Window.Hwnd`, and creates it on demand.
- **Panes start hidden and Office gives no way to reopen one.** If the user closes it with the
  X, it is gone unless you provide a control. That is why the ribbon has a `toggleButton` with
  `getPressed` — the documented pattern for keeping the two in sync.
- **Do not call `Remove` or `RemoveAt` in `ThisAddIn_Shutdown`.** The VSTO runtime disposes the
  panes first, so removing them there throws `ObjectDisposedException`. Clean up while the
  add-in is still running instead — here, when a workbook is deactivated.
- **`Width` only applies when docked left or right**, and height only when docked top or
  bottom.

### Why the project is called ExcelHost and not Excel

The obvious name for this project is `DataverseAddIn.Excel`. It was, briefly, and it is worth
explaining why it is not any more.

**A namespace segment shadows a using-alias of the same name.** Inside
`namespace DataverseAddIn.Excel`, the name `Excel` resolves to the *namespace*, not to an
alias, so the idiomatic line every VSTO sample opens with:

```csharp
using Excel = Microsoft.Office.Interop.Excel;
```

quietly stops working, and `Excel.Window` fails with:

```
error CS0234: The type or namespace name 'Window' does not exist in the
namespace 'DataverseAddIn.Excel'
```

That error says nothing about aliases, which is what makes it expensive. And it does not fail
once — it fails every time someone pastes a snippet using `Excel.Range`, `Excel.Worksheet` or
`Excel.Workbook`, which is most Office sample code in existence.

The workaround is a non-colliding alias (`ExcelInterop`). The fix is to not name a namespace
segment after a library you intend to alias. Since this repository is a starting point for
other people's add-ins, it takes the fix: the final segment is `ExcelHost`, matching the
`ConsoleHost` / `WinFormsHost` vocabulary used by the samples, and `using Excel = …` behaves
normally again.

**The same trap is waiting for `Office`.** `DataverseRibbon.cs` uses
`using Office = Microsoft.Office.Core;`, so a project or namespace segment named `Office`
would break in exactly the same way. Renaming only helps if the *last* segment changes —
`DataverseAddIn.Host.Excel` would still collide.

If you rename this project in a fork, three things must move together or the build breaks in
confusing ways: `RootNamespace`, the string passed to `GetResourceText`
(`<RootNamespace>.DataverseRibbon.xml`), and `ManifestKeyFile`. Delete the old registry key
afterwards, or Excel keeps loading the previous add-in from a path that no longer exists:

```powershell
Remove-Item 'HKCU:\Software\Microsoft\Office\Excel\Addins\<old name>' -Recurse
```

## What the VSTO template does not do for you

Already applied to `DataverseAddIn.ExcelHost`, and each one is a build or runtime failure if
missed:

- `DataverseRibbon.xml` must be an **EmbeddedResource**, and the name passed to
  `GetResourceText` must match `<RootNamespace>.DataverseRibbon.xml` exactly.
- A reference to **System.Configuration** is required to read `appSettings`.
- **NuGet references of referenced SDK-style projects do not flow to a legacy project.**
  `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy` and `Microsoft.PowerPlatform.Dataverse.Client`
  are referenced by `HintPath`, and a copy target brings `ServiceClient`'s ~130-assembly
  closure into the output. `ProjectReference` alone copies only 37 DLLs, which fails at run
  time.
- `ThisAddIn.Designer.cs` uses the project's `RootNamespace`. C# namespaces are
  case-sensitive, so a file declaring a differently-cased namespace silently creates a
  *different* class and the partial merge fails with `CS0115: no suitable method found to
  override`.
- **The ribbon's `OnLoad` runs before `ThisAddIn_Startup`.** Anything the ribbon touches must
  be created lazily, not assigned in Startup, or it is null when the ribbon loads.

## Ribbon patterns

One connection manager for the lifetime of the add-in, created lazily:

```csharp
public partial class ThisAddIn
{
    internal static DataverseConnectionManager Connections { get; private set; }

    private void ThisAddIn_Startup(object sender, EventArgs e)
    {
        Connections = new DataverseConnectionManager(cloud => new DataverseAuthOptions
        {
            ClientId = "<your client id>",
            Cloud    = cloud,
            // Parent the sign-in window to Excel so it can never appear behind it.
            ParentWindowHandleProvider = () => new IntPtr(Application.Hwnd)
        });
    }
}
```

Button state uses a `getEnabled` callback rather than stored state:

```csharp
public bool GetWhoAmIEnabled(Office.IRibbonControl control) =>
    ThisAddIn.Connections != null && ThisAddIn.Connections.IsConnected;
```

Office only calls it when the control is invalidated, so `DataverseConnectionManager` raises
`ConnectionChanged` and the ribbon responds with `_ribbon.InvalidateControl("btnWhoAmI")`.
Connect, disconnect and deleting the active connection all flow through that one event.

## Other Office-specific notes

- Keep `PlatformTarget` as `AnyCPU`, `x86` or `x64` — never blank. WebView2, if you ever opt
  into the embedded browser, cannot resolve its native loader otherwise.
- On an ARM64 OS with x64 Office, the add-in runs under emulation. `AnyCPU` handles it.
- The default is the **system browser**, not the embedded one. On .NET Framework the embedded
  view is WebView1 (Internet Explorer), which breaks FIDO keys and Windows Hello and trips
  several Conditional Access policies. Only set `UseEmbeddedWebView = true` if a
  system-browser popup is unacceptable, and add `Microsoft.Identity.Client.Desktop` plus
  `ms-appx-web://…` handling if you do. For AVD or managed desktops where CA leans on device
  state, the WAM broker is worth testing — it is the only option here with first-class
  device-signal support.
- Assemblies loaded into Excel's AppDomain are shared with every other add-in, so prefer
  `DataverseWebApiClient` (8 DLLs) over `ServiceClient` (135) where the SDK message types are
  not actually needed. See [architecture](architecture.md#why-the-split).

## A WinForms trap worth generalising

`Control.Visible` is false whenever a control is not on screen — **including after
`ShowDialog` returns**, which is exactly when callers read a dialog's result. Using it to
store *intent* meant every client-secret connection would have been saved with null fields.

Track intent in your own field; let `Visible` be an output. `tools/verify-connection-dialog.ps1`
pins this by constructing the dialog off screen and asserting the after-close case.
