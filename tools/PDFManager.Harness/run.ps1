<#
.SYNOPSIS
Runs the real PDFManager.exe with the harness loaded, then reports how each run went.

.DESCRIPTION
Builds the app and the harness, starts bin\<Configuration>\net9.0-windows10.0.19041.0\PDFManager.exe
with the harness as a .NET startup hook, and waits for it to close itself. Each run gets a report
file with a PASS or FAIL line per check, and one summary line here: the exit code, whether the
checks passed, and any crash Windows logged for that process (Application log, event 1000).
Settings and error logs go to a temp folder; the user's own are never read or written.
The app's window is on screen while a run lasts. Don't use the mouse or keyboard meanwhile.

.PARAMETER Steps
Comma-separated steps (see README.md), or a preset: smoke (default), exit or crash.

.PARAMETER Times
How many runs. Use 3 or more when looking for a crash that does not happen every time.

.PARAMETER ExePath
Another PDFManager.exe to run, such as an older build. Its app must still have what the steps use.

.EXAMPLE
.\tools\PDFManager.Harness\run.ps1

.EXAMPLE
.\tools\PDFManager.Harness\run.ps1 -Steps split_load,gc -Times 3
#>
param(
    [string[]]$Steps = @("smoke"),
    [int]$Times = 1,
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [string]$ExePath
)
$ErrorActionPreference = "Stop"

$presets = @{
    # Every tool flow that goes through real dialogs, then the theme switch
    smoke = "split_open,split_run,merge_open,merge_saveas,merge_run,rotate_flow,batch_flow,theme_light"
    # Page previews released before exit: crashed in the graphics driver before the fix
    exit  = "split_load,gc"
    # An exception on the UI thread, then a tool flow to show the app kept working
    crash = "crash_ui,rotate_flow"
}
$stepList = ($Steps -join ",")
if ($presets.ContainsKey($stepList)) { $stepList = $presets[$stepList] }

$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$framework = "net9.0-windows10.0.19041.0"
$exe = if ($ExePath) { (Resolve-Path $ExePath).Path } else { Join-Path $repo "bin\$Configuration\$framework\PDFManager.exe" }
$hook = Join-Path $PSScriptRoot "bin\$Configuration\$framework\PDFManager.Harness.dll"

if (-not $NoBuild) {
    # Building the harness builds the app too
    dotnet build (Join-Path $PSScriptRoot "PDFManager.Harness.csproj") -c $Configuration -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
}
foreach ($file in $exe, $hook) {
    if (-not (Test-Path $file)) { throw "$file is missing. Build first, or leave out -NoBuild." }
}

$realSettings = Join-Path $env:LOCALAPPDATA "PDFManager\settings.json"
$settingsBefore = if (Test-Path $realSettings) { (Get-Item $realSettings).LastWriteTimeUtc } else { $null }
$reports = Join-Path $env:TEMP "PDFManager.Harness\reports"
New-Item -ItemType Directory -Force $reports | Out-Null
"Steps: $stepList"
"Exe:   $exe (built $((Get-Item $exe).LastWriteTime))"

$failedRuns = 0
for ($run = 1; $run -le $Times; $run++) {
    $report = Join-Path $reports ("{0:yyyyMMdd-HHmmss}-{1}.txt" -f (Get-Date), $run)
    $start = New-Object System.Diagnostics.ProcessStartInfo $exe
    $start.UseShellExecute = $false
    $start.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = $hook
    $start.EnvironmentVariables["PDFMANAGER_HARNESS_REPORT"] = $report
    $start.EnvironmentVariables["PDFMANAGER_HARNESS_STEPS"] = $stepList
    $startedAt = Get-Date
    $process = [System.Diagnostics.Process]::Start($start)

    if ($process.WaitForExit(180000)) {
        $exitCode = $process.ExitCode
    } else {
        $process.Kill()
        $exitCode = "TIMEOUT"
    }

    # Windows Error Reporting needs a moment to log a crash
    Start-Sleep -Milliseconds 1500
    $crashes = @(Get-WinEvent -FilterHashtable @{ LogName = "Application"; Id = 1000; StartTime = $startedAt } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match "Faulting process id: 0x([0-9a-fA-F]+)" -and [Convert]::ToInt32($Matches[1], 16) -eq $process.Id } |
        ForEach-Object {
            $module = if ($_.Message -match "Faulting module name: ([^,\r\n]+)") { $Matches[1] } else { "?" }
            $code = if ($_.Message -match "Exception code: (0x[0-9a-fA-F]+)") { $Matches[1] } else { "?" }
            "$module $code"
        })

    $lines = if (Test-Path $report) { Get-Content $report } else { @("(no report: the harness did not load)") }
    $passed = [bool]($lines | Select-String -SimpleMatch "ALL PASSED")
    $crashText = if ($crashes.Count -gt 0) { $crashes -join "; " } else { "none" }
    "Run {0}: exit {1}, checks {2}, crash {3}" -f $run, $exitCode, $(if ($passed) { "passed" } else { "FAILED" }), $crashText
    if (-not $passed -or $exitCode -ne 0 -or $crashes.Count -gt 0) {
        $failedRuns++
        $lines | Where-Object { $_ -cmatch "FAIL|UNEXPECTED|no report" } | ForEach-Object { "    $_" }
        "    Report: $report"
    }
}

$settingsAfter = if (Test-Path $realSettings) { (Get-Item $realSettings).LastWriteTimeUtc } else { $null }
if ($settingsBefore -ne $settingsAfter) { Write-Warning "Your own settings.json changed during the runs: $realSettings" }
exit $failedRuns
