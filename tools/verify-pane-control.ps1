# Constructs the task pane control off-screen and reports how each child actually measures.
# Unit tests cannot cover this: the layout only misbehaves once WinForms has laid it out.
#
#   pwsh tools\verify-pane-control.ps1
#
# Run it after changing DataversePaneControl.

param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'

$bin = Join-Path $PSScriptRoot "..\src\DataverseAddIn.WinForms\bin\$Configuration\net462"
if (-not (Test-Path $bin)) { throw "Build DataverseAddIn.WinForms first: $bin not found." }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
foreach ($name in 'DataverseAddIn.Discovery', 'DataverseAddIn.Connections', 'DataverseAddIn.WinForms') {
    [Reflection.Assembly]::LoadFrom((Join-Path $bin "$name.dll")) | Out-Null
}

# An isolated store and secret store, so running this never touches the real profile.
$temp = Join-Path ([IO.Path]::GetTempPath()) ("pane-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $store = New-Object DataverseAddIn.Connections.ConnectionStore((Join-Path $temp 'connections.json'))

    $options = [Func[DataverseAddIn.Discovery.DataverseCloud, DataverseAddIn.Discovery.DataverseAuthOptions]] {
        param($cloud)
        $o = New-Object DataverseAddIn.Discovery.DataverseAuthOptions
        $o.Cloud = $cloud
        $o
    }

    $manager = New-Object DataverseAddIn.Connections.DataverseConnectionManager($options, $store, $null)
    $pane = New-Object DataverseAddIn.WinForms.DataversePaneControl($manager)

    # Force a layout at the width Excel gives a 600-point pane, roughly 800px.
    $host_ = New-Object System.Windows.Forms.Form
    $host_.ClientSize = New-Object System.Drawing.Size(800, 600)
    $host_.Controls.Add($pane)
    $host_.CreateControl()
    $pane.PerformLayout()

    $private = [Reflection.BindingFlags]'Instance,NonPublic'

    function Show-Child($field) {
        $c = $pane.GetType().GetField($field, $private).GetValue($pane)

        $themed = if ($c -is [System.Windows.Forms.Button]) { " themed=$($c.UseVisualStyleBackColor)" } else { '' }

        '{0,-12} text="{1}" size={2}x{3} fore={4} back={5} enabled={6}{7}' -f `
            $field, $c.Text, $c.Width, $c.Height, $c.ForeColor.Name, $c.BackColor.Name, $c.Enabled, $themed
    }

    'pane size    : {0}x{1}' -f $pane.Width, $pane.Height
    Show-Child '_environment'
    Show-Child '_detail'
    Show-Child '_connections'
    Show-Child '_disconnect'

    foreach ($field in '_connections', '_disconnect') {
        $button = $pane.GetType().GetField($field, $private).GetValue($pane)

        if ($button.Width -lt 60 -or $button.Height -lt 16) {
            throw "REGRESSION: $field collapsed to $($button.Width)x$($button.Height)."
        }

        # The pane sets a white BackColor for legibility, and BackColor is ambient: without
        # this the buttons inherit it, drop their themed face and vanish into the background.
        if (-not $button.UseVisualStyleBackColor) {
            throw "REGRESSION: $field is not themed, so it will render as a flat $($button.BackColor.Name) block."
        }
    }

    $host_.Dispose()

    # A pane that has no window yet still receives ConnectionChanged: the manager raises it from
    # the thread pool, where InvokeRequired reports false and the update would go cross-thread.
    $detached = New-Object DataverseAddIn.WinForms.DataversePaneControl($manager)
    $handler = $detached.GetType().GetMethod('OnConnectionChanged', $private)

    'no window    : handle created={0}' -f $detached.IsHandleCreated

    if ($detached.IsHandleCreated) {
        throw 'Test is not exercising the no-window case: the control already has a handle.'
    }

    try {
        $handler.Invoke($detached, @($null, [EventArgs]::Empty))
        'no window    : ConnectionChanged handled without touching controls'
    }
    catch {
        throw "REGRESSION: ConnectionChanged threw with no window: $($_.Exception.InnerException.Message)"
    }

    $detached.Dispose()
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}