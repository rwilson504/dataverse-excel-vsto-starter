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
$form = New-Object DataverseAddIn.WinForms.ConnectionDetailsForm($environment, 'Contoso', $null, $null, $false)

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
'OK'
