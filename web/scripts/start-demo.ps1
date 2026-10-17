param(
    [ValidateSet('Sqlite','SqlServer')][string]$Provider = 'Sqlite',
    [int]$Port = 0,
    [string]$SqliteDataFile = '',
    [string]$StorageRoot = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path $localDotnet) { $env:PATH = "$localDotnet;$env:PATH" }
if ($Port -lt 0 -or $Port -gt 65535) { throw 'Port must be 0-65535.' }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
try {
    $listener.Start()
    $selectedPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
}
finally { $listener.Stop() }
if ($Provider -eq 'SqlServer' -and -not $env:ConnectionStrings__SqlServer) {
    throw 'Set ConnectionStrings__SqlServer explicitly for the SQL Server profile.'
}
$env:Database__Provider = $Provider
if ($Provider -eq 'Sqlite' -and $SqliteDataFile) {
    $databasePath = [System.IO.Path]::GetFullPath($SqliteDataFile)
    $env:ConnectionStrings__Sqlite = "Data Source=$databasePath"
}
if ($StorageRoot) { $env:Storage__ManagedRoot = [System.IO.Path]::GetFullPath($StorageRoot) }
$env:Host__Url = "http://127.0.0.1:$selectedPort"
Write-Host "Opening $env:Host__Url (database: $Provider). Ctrl+C stops this host."
Push-Location $repo
try {
    & dotnet run --project web/src/ThesisDemo.Api --no-build --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Application exited with code $LASTEXITCODE" }
}
finally { Pop-Location }
