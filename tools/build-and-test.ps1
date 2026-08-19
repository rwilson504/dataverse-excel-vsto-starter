# Builds and tests everything the .NET SDK can build, and nothing it cannot.
#
#   pwsh tools\build-and-test.ps1 [-Configuration Release] [-SkipDialogCheck]
#
# DataverseAddIn.ExcelHost is excluded deliberately. It is a VSTO project: the .NET CLI has no
# OfficeTools targets, so building the solution fails, and worse, the restore writes
# project.assets.json and *.nuget.g.* into its obj folder, after which Visual Studio's legacy
# NuGet targets fail with "doesn't list 'win' as a RuntimeIdentifier". Build that project in
# Visual Studio 2019 with the Office/SharePoint workload.

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$SkipDialogCheck,
    # CI has no language server restoring in the background, so anything found there is a
    # regression in this script's exclusions rather than an editor side effect.
    [switch]$FailOnVstoArtifacts
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action

    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

$excluded = 'DataverseAddIn.ExcelHost'

$projects = Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'samples'), (Join-Path $root 'tests') `
    -Recurse -Filter *.csproj |
    Where-Object { $_.BaseName -ne $excluded } |
    Sort-Object FullName

$testProjects = $projects | Where-Object { $_.BaseName -like '*.Tests' }

Write-Host "Building $($projects.Count) projects in $Configuration (excluding $excluded)." -ForegroundColor Yellow

foreach ($project in $projects) {
    Invoke-Step "build $($project.BaseName)" { dotnet build $project.FullName --configuration $Configuration --nologo -v minimal }
}

foreach ($project in $testProjects) {
    Invoke-Step "test $($project.BaseName)" { dotnet test $project.FullName --configuration $Configuration --nologo -v minimal --no-build }
}

# The connection dialog cannot be unit tested: its worst failure mode only appears once the
# form is off screen. See tools/verify-connection-dialog.ps1.
if (-not $SkipDialogCheck) {
    $dialogCheck = Join-Path $PSScriptRoot 'verify-connection-dialog.ps1'

    Invoke-Step 'verify connection dialog' {
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dialogCheck -Configuration $Configuration
    }
}

# A contaminated VSTO obj folder breaks the next Visual Studio build with "doesn't list 'win'
# as a RuntimeIdentifier". The VS Code C# language server restores the whole solution in the
# background, so this comes back on its own and cleaning it once is not enough.
$contamination = Get-ChildItem -Path (Join-Path $root "src\$excluded\obj") `
    -Include project.assets.json, *.nuget.g.props, *.nuget.g.targets, project.nuget.cache `
    -Recurse -ErrorAction SilentlyContinue

if ($contamination) {
    if ($FailOnVstoArtifacts) {
        $contamination | ForEach-Object { Write-Host "  $($_.FullName)" -ForegroundColor Red }
        throw "NuGet artifacts were written into the VSTO project. Something built or restored $excluded."
    }

    Write-Host "==> removing NuGet artifacts from $excluded (they break the Visual Studio build)" -ForegroundColor Yellow
    $contamination | ForEach-Object {
        Write-Host "  $($_.Name)"
        Remove-Item $_.FullName -Force
    }
}

Write-Host 'All builds, tests and checks passed.' -ForegroundColor Green
