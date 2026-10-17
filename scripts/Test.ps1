[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# WPF builds and runs only on Windows; macOS and Linux build and check the shared projects.
$windows = $PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows
$solution = if ($windows) { 'Lexi.sln' } else { 'Lexi.Shared.slnf' }
Push-Location $root
try {
    & dotnet restore $solution --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    $fixture = Join-Path $root '.cache/test-dictionary'
    New-Item -ItemType Directory -Force $fixture | Out-Null
    $runner = Join-Path $root 'Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll'
    & dotnet $runner build-data tests/fixtures/dictionary.csv $fixture
    if ($LASTEXITCODE) { throw 'Fixture creation failed' }
    & dotnet $runner check $fixture --fixture
    if ($LASTEXITCODE) { throw 'Dictionary checks failed' }
    & dotnet $runner usability-check $fixture
    if ($LASTEXITCODE) { throw 'Dictionary usability and connection checks failed' }
    & dotnet $runner stream-check
    if ($LASTEXITCODE) { throw 'Streaming checks failed' }
    & dotnet $runner extended-check (Join-Path $root '.cache/extended-checks')
    if ($LASTEXITCODE) { throw 'Extended dictionary/translation checks failed' }
    & dotnet $runner parallel-check (Join-Path $root '.cache/parallel-checks')
    if ($LASTEXITCODE) { throw 'Parallel dictionary checks failed' }
    & dotnet $runner lexical-check
    if ($LASTEXITCODE) { throw 'Lexical coverage checks failed' }
    & dotnet $runner speech-check
    if ($LASTEXITCODE) { throw 'Pronunciation and speech checks failed' }
    & dotnet $runner platform-check (Join-Path $root '.cache/platform-checks')
    if ($LASTEXITCODE) { throw 'Platform path, client discovery and Chinese variant checks failed' }
    & dotnet $runner session-check $fixture (Join-Path $root '.cache/session-checks')
    if ($LASTEXITCODE) { throw 'Shared query session checks failed' }
    & dotnet $runner api-check $fixture (Join-Path $root '.cache/api-checks')
    if ($LASTEXITCODE) { throw 'Model API checks failed' }
    & dotnet $runner host-check $fixture (Join-Path $root '.cache/host-checks') (Join-Path $root 'Lexi.Host/bin/Release/net10.0/lexi-host.dll')
    if ($LASTEXITCODE) { throw 'Host protocol checks failed' }
    if ($windows) {
        & dotnet run --project tests/Lexi.PresentationChecks -c Release
        if ($LASTEXITCODE) { throw 'WPF resource construction checks failed' }
        & ./scripts/Test-ShellIcon.ps1
    }
} finally { Pop-Location }
