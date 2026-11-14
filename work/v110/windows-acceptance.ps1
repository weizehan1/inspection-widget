param()
$ErrorActionPreference = 'Stop'

$packageRoot = $PSScriptRoot
$manifestPath = Join-Path $packageRoot 'verification-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($item in $manifest.files) {
    $itemPath = Join-Path $packageRoot $item.name
    $actual = (Get-FileHash -LiteralPath $itemPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $item.sha256) {
        throw "Package file hash mismatch: $($item.name)"
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$reportRoot = Join-Path $packageRoot "verification-$stamp-$suffix"
New-Item -ItemType Directory -Path $reportRoot | Out-Null
$logPath = Join-Path $reportRoot 'windows-tests.log'
$testExe = Join-Path $packageRoot 'InspectionWidget.Tests.exe'
$testExit = 1

Push-Location $packageRoot
try {
    # --windows-ui runs both the logic suite and real WinForms tests on an STA thread.
    & $testExe --windows-ui 2>&1 | Tee-Object -FilePath $logPath
    $testExit = $LASTEXITCODE
}
catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath -Encoding UTF8
    $testExit = 1
}
finally {
    Pop-Location
}

$results = @()
foreach ($line in Get-Content -LiteralPath $logPath) {
    if ($line -match '^\[(PASS|FAIL|SKIP)\]\s*(.*)$') {
        $status = switch ($Matches[1]) {
            'PASS' { 'passed' }
            'FAIL' { 'failed' }
            'SKIP' { 'skipped' }
        }
        $results += [PSCustomObject]@{ name = $Matches[2]; status = $status }
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportRoot 'tests.json') -Encoding UTF8
$evidence = [PSCustomObject]@{
    sourceSha = $manifest.sourceSha
    packageBuild = $manifest.buildEnvironment
    verifiedAt = (Get-Date).ToString('o')
    windowsVersion = [Environment]::OSVersion.VersionString
    process64Bit = [Environment]::Is64BitProcess
    exitCode = $testExit
    passed = @($results | Where-Object { $_.status -eq 'passed' }).Count
    failed = @($results | Where-Object { $_.status -eq 'failed' }).Count
    skipped = @($results | Where-Object { $_.status -eq 'skipped' }).Count
    executableSha256 = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'InspectionWidget.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportRoot 'verification.json') -Encoding UTF8
Write-Host "Reports: $reportRoot"
$expectedCases = $manifest.expectedHeadlessCases + $manifest.expectedWindowsUiCases
if ($testExit -ne 0 -or $evidence.failed -gt 0 -or $evidence.skipped -gt 0 -or $results.Count -ne $expectedCases) {
    Write-Error 'Windows verification did not fully pass. Keep the reports for review.' -ErrorAction Continue
    exit 1
}
Write-Host 'Automated Windows verification passed. Manual capture, tray and mixed-DPI checks remain separate.'
exit 0
