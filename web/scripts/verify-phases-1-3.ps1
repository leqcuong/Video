param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$IncludeLocalDb
)

$ErrorActionPreference = "Stop"
$webRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $webRoot "ThesisDemo.sln"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultRoot = Join-Path $webRoot "artifacts\verification\phase123-$timestamp"
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

$originalLocalDb = [Environment]::GetEnvironmentVariable("THESIS_TEST_LOCALDB", "Process")
$buildExit = -1
$testExit = -1
$localDbEnabled = $false

try {
    if ($IncludeLocalDb) {
        if (-not (Get-Command SqlLocalDB -ErrorAction SilentlyContinue)) {
            throw "SqlLocalDB is not installed or is missing from PATH."
        }
        & SqlLocalDB info MSSQLLocalDB *> $null
        if ($LASTEXITCODE -ne 0) { throw "LocalDB instance MSSQLLocalDB is unavailable." }
        $localDbEnabled = $true
        [Environment]::SetEnvironmentVariable("THESIS_TEST_LOCALDB", "1", "Process")
    } else {
        [Environment]::SetEnvironmentVariable("THESIS_TEST_LOCALDB", $null, "Process")
    }

    & dotnet build $solution -c $Configuration --nologo 2>&1 |
        Tee-Object -FilePath (Join-Path $resultRoot "build.log")
    $buildExit = $LASTEXITCODE

    if ($buildExit -eq 0) {
        & dotnet test $solution -c $Configuration --no-build --no-restore --nologo `
            --logger "trx;LogFilePrefix=phase123" --collect "XPlat Code Coverage" `
            --results-directory (Join-Path $resultRoot "test-results") 2>&1 |
            Tee-Object -FilePath (Join-Path $resultRoot "test.log")
        $testExit = $LASTEXITCODE
    }
}
finally {
    [Environment]::SetEnvironmentVariable("THESIS_TEST_LOCALDB", $originalLocalDb, "Process")
}

$trxFiles = @(Get-ChildItem -Path $resultRoot -Recurse -Filter "*.trx" -ErrorAction SilentlyContinue)
$rows = @()
foreach ($file in $trxFiles) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName
    $counter = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counter) { continue }
    $rows += [pscustomobject]@{
        File = $file.Name
        Total = [int]$counter.total
        Passed = [int]$counter.passed
        Failed = [int]$counter.failed
        Other = [int]$counter.total - [int]$counter.passed - [int]$counter.failed
    }
}

$totals = @{
    Total = ($rows | Measure-Object -Property Total -Sum).Sum
    Passed = ($rows | Measure-Object -Property Passed -Sum).Sum
    Failed = ($rows | Measure-Object -Property Failed -Sum).Sum
    Other = ($rows | Measure-Object -Property Other -Sum).Sum
}
$repoRoot = (Resolve-Path (Join-Path $webRoot "..")).Path
$commit = (& git -C $repoRoot rev-parse HEAD 2>$null)
$sdk = (& dotnet --version)
$testStatus = if ($buildExit -eq 0 -and $testExit -eq 0 -and $rows.Count -eq 4 -and $totals.Failed -eq 0) { "PASS" } else { "FAIL" }
$report = @(
    "# Phase 1-3 Automated Verification",
    "",
    "- Run: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "- Revision: $commit",
    "- .NET SDK: $sdk",
    "- Configuration: $Configuration",
    "- SQL Server LocalDB: $(if ($localDbEnabled) { 'exercised' } else { 'not exercised (use -IncludeLocalDb)' })",
    "- Build exit code: $buildExit",
    "- Test exit code: $testExit",
    "- Result: **$testStatus** ($($totals.Passed)/$($totals.Total) passed, $($totals.Failed) failed, $($totals.Other) other)",
    "",
    "## Test Projects",
    "",
    "| TRX | Total | Passed | Failed | Other |",
    "| --- | ---: | ---: | ---: | ---: |"
)
foreach ($row in $rows) {
    $report += "| $($row.File) | $($row.Total) | $($row.Passed) | $($row.Failed) | $($row.Other) |"
}
$report += @(
    "",
    "## Verification Scope",
    "",
    "| Gate | Automated evidence | Remaining checks |",
    "| --- | --- | --- |",
    "| M0 | Domain, architecture, SQLite migration/concurrency, API security; SQL Server rowversion when enabled | Wider SQL Server integration/load testing |",
    "| V01 | Preview/commit idempotency, restart identity and provenance, collision handling | Import and warm-query tests against current 895-asset corpus |",
    "| V02 | Missing media and stale-preview rejection; checksums/provenance recorded | Exhaustive malformed-row cases and independent artifact byte-for-byte audit |",
    "| V03 | Alpha/K/filter/validation API cases and deterministic fixture ranking | Historical golden parity for all model variants on actual index |",
    "| V04 | Not part of Phases 1-3 (upload is T026, Phase 5) | Stream/abort/oversize tests pending implementation |",
    "| V05 | HEAD/GET/ranges/If-Range, absent media; real Chromium/WebM seek and stop on desktop/mobile | Unsupported codec behavior on installed target browsers |",
    "| V06 | Registry/protocol checks and baseline/reranked UI comparison | Real reranker features/provenance mismatch and same-snapshot golden parity |",
    "",
    'Test artifacts: `test-results/*.trx`, `test-results/*/coverage.cobertura.xml`, `build.log`, `test.log`. Browser captures are in `web/artifacts/verification/screenshots/`.',
    "The green automated suite does not by itself close the M1/full-corpus parity acceptance gate."
)
$reportPath = Join-Path $resultRoot "report.md"
$report | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "`nReport: $reportPath"
Write-Host "Status: $testStatus, passed $($totals.Passed)/$($totals.Total)"
if ($testStatus -ne "PASS") { exit 1 }
