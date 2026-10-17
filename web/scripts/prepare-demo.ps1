param(
    [ValidateSet('Sqlite','SqlServer')][string]$Provider = 'Sqlite',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repo 'web\ThesisDemo.sln'
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path $localDotnet) { $env:PATH = "$localDotnet;$env:PATH" }
Push-Location (Join-Path $repo 'web')
try {
    $version = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $version -notlike '10.*') { throw "Expected .NET SDK 10.0.401 or compatible 10.x; found $version" }
    Write-Host "Using .NET $version; selected database profile: $Provider"
    if ($Provider -eq 'SqlServer' -and -not $env:ConnectionStrings__SqlServer) {
        throw 'Set ConnectionStrings__SqlServer explicitly before preparing the SQL Server profile.'
    }
    & dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build $solution --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & dotnet test $solution --no-build --no-restore --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Solution tests failed.' }
    }
    Write-Host 'Build preparation complete. Model, FFmpeg, browser, provider and offline evidence still require preflight.'
}
finally { Pop-Location }
