# Constructs the connection dialog off-screen and reports how it responds to each
# authentication kind. Unit tests cannot cover this: the dialog is WinForms, and the bug this
# was written to catch only appears when the form is not on screen.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\verify-connection-dialog.ps1
#
# Run it after changing ConnectionDetailsForm or adding an AuthKindDescriptor.

param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'

$bin = Join-Path $PSScriptRoot "..\src\DataverseAddIn.WinForms\bin\$Configuration\net462"
if (-not (Test-Path $bin)) { throw "Build DataverseAddIn.WinForms first: $bin not found." }

Add-Type -AssemblyName System.Windows.Forms
foreach ($name in 'DataverseAddIn.Discovery', 'DataverseAddIn.Connections', 'DataverseAddIn.WinForms') {
    [Reflection.Assembly]::LoadFrom((Join-Path $bin "$name.dll")) | Out-Null
}

$environment = [DataverseAddIn.Discovery.DataverseEnvironmentReference]::Parse('https://contoso.crm.dynamics.com')
$form = New-Object DataverseAddIn.WinForms.ConnectionDetailsForm($environment, 'Contoso', $null, $null, $false, $null)

$private = [Reflection.BindingFlags]'Instance,NonPublic'
$fields = $form.GetType().GetField('_authFields', $private).GetValue($form)
$combo = $form.GetType().GetField('_authKind', $private).GetValue($form)
$ok = $form.GetType().GetField('_ok', $private).GetValue($form)

function Field($name) { $fields[[DataverseAddIn.Discovery.AuthField]::$name] }

function Show-State($label) {
    $applicable = @()
    foreach ($key in $fields.Keys) { if ($fields[$key].IsApplicable) { $applicable += "$key" } }

    '{0,-20} kind={1,-14} fields=[{2}] ok={3}' -f `
        $label, $combo.SelectedItem.Kind, (($applicable | Sort-Object) -join ', '), $ok.Enabled
}

'kinds offered  : ' + (($combo.Items | ForEach-Object { $_.DisplayName }) -join ' | ')
Show-State 'default'

$combo.SelectedIndex = 1
Show-State 'client secret'

(Field ClientId).Editor.Text = 'app-1'
Show-State 'app id typed'

(Field TenantId).Editor.Text = 'contoso.onmicrosoft.com'
Show-State 'tenant typed'

(Field ClientSecret).Editor.Text = 's3cret'
Show-State 'secret typed'

'secret masked  : ' + (Field ClientSecret).Editor.UseSystemPasswordChar

# Deliberately not asserting $test.Visible: Control.Visible is false on a form that was never
# shown, so it would report the same either way. Check the wiring instead.
'test wired     : ' + ($null -ne $form.GetType().GetField('_tester', $private).GetValue($form)) + '  (none supplied)'

# The bug that prompted this script: the result was read from Control.Visible, which is false
# once the dialog closes, so every field came back null exactly when the caller needed it.
$form.Close()
$authentication = $form.Authentication

'after close    : kind={0} clientId={1} tenant={2} secret supplied={3}' -f `
    $authentication.Kind, $authentication.ClientId, $authentication.TenantId, ($null -ne $authentication.ClientSecret)

if ($null -eq $authentication.ClientId -or $null -eq $authentication.ClientSecret) {
    throw 'REGRESSION: the dialog lost its values once closed.'
}

$form.Dispose()

# A failing test must report the reason and leave the dialog usable, not wedge OK off.
$failing = [Func[DataverseAddIn.Discovery.DataverseEnvironmentReference, `
                 DataverseAddIn.Connections.ConnectionAuthentication, `
                 Threading.CancellationToken, `
                 Threading.Tasks.Task[string]]] {
    param($env, $auth, $token)
    $source = New-Object 'System.Threading.Tasks.TaskCompletionSource[string]'
    $source.SetException([InvalidOperationException]::new('no application user'))
    $source.Task
}

$form2 = New-Object DataverseAddIn.WinForms.ConnectionDetailsForm($environment, 'Contoso', $null, $null, $false, $failing)
$result2 = $form2.GetType().GetField('_testResult', $private).GetValue($form2)
$ok2 = $form2.GetType().GetField('_ok', $private).GetValue($form2)

'test wired     : ' + ($null -ne $form2.GetType().GetField('_tester', $private).GetValue($form2)) + '  (supplied)'

# PerformClick() is a no-op on a form that was never shown, so invoke the handler directly.
$handler = $form2.GetType().GetMethod('OnTestAsync', $private)
$handler.Invoke($form2, @($null, [EventArgs]::Empty))

$deadline = (Get-Date).AddSeconds(5)
while ($result2.Text -in @('', 'Testing...') -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }

'after failure  : text="{0}" ok={1}' -f $result2.Text, $ok2.Enabled

if ($result2.Text -notlike '*application user*') { throw 'REGRESSION: the failure reason was not shown.' }
if (-not $ok2.Enabled) { throw 'REGRESSION: a failed test left the dialog unusable.' }

$form2.Dispose()
'OK'
