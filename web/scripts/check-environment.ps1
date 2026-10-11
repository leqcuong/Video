param(
    [string]$PythonPath = "C:\Python313\python.exe",
    [string]$SourceRoot = (Join-Path $PSScriptRoot "..\..\legacy-pipeline"),
    [string]$ModelsRoot = (Join-Path $PSScriptRoot "..\..\legacy-pipeline\models"),
    [int]$MinimumFreeGb = 10,
    [switch]$Strict
)
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check($Name, $Ok, $Detail, $Required = $true) {
    $checks.Add([pscustomobject]@{ Check=$Name; Ready=[bool]$Ok; Required=[bool]$Required; Detail=$Detail })
}
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdk = if ($dotnet) { (& $dotnet.Source --version 2>$null) } else { "missing" }
Add-Check ".NET SDK" ($sdk -like "10.*") $sdk
$pythonOk = Test-Path -LiteralPath $PythonPath
$pythonInfo = if ($pythonOk) { (& $PythonPath -c "import platform,sys; print(f'{sys.executable}|{platform.architecture()[0]}')" 2>$null) } else { "missing: $PythonPath" }
Add-Check "Python x64" ($pythonOk -and $pythonInfo -like "*|64bit") $pythonInfo
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
Add-Check "FFmpeg" ($null -ne $ffmpeg) $(if ($ffmpeg) {$ffmpeg.Source} else {"missing from PATH"})
Add-Check "Pipeline source" (Test-Path -LiteralPath $SourceRoot) $SourceRoot
Add-Check "Models" (Test-Path -LiteralPath $ModelsRoot) $ModelsRoot
$drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot((Resolve-Path $PSScriptRoot)).TrimEnd(':\'))
$freeGb = [math]::Round($drive.Free / 1GB, 1)
Add-Check "Disk" ($freeGb -ge $MinimumFreeGb) "$freeGb GB free"
$nvidia = Get-Command nvidia-smi -ErrorAction SilentlyContinue
$gpu = if ($nvidia) { & $nvidia.Source --query-gpu=name,memory.total --format=csv,noheader 2>$null } else { $null }
Add-Check "Device" $true $(if ($gpu) {"CUDA: $gpu"} else {"CPU (no NVIDIA device detected)"}) $false
$checks | Format-Table -AutoSize
if ($Strict -and ($checks | Where-Object { $_.Required -and -not $_.Ready })) { exit 1 }
