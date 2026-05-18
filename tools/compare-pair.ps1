param(
    [Parameter(Mandatory = $true)]
    [string]$Reference,
    [Parameter(Mandatory = $true)]
    [string]$Candidate,
    [string]$Diff = "screenshots\diff.png",
    [string]$Report = "screenshots\compare.json",
    [string]$Godot = "C:\Bin\Godot\CurrentVersion\Godot.exe",
    [string]$Project = "D:\Tom\code\github\webgiya-godot"
)

$ErrorActionPreference = "Stop"

$reportPath = if ([System.IO.Path]::IsPathRooted($Report)) { $Report } else { Join-Path $Project $Report }
if (Test-Path -LiteralPath $reportPath) {
    Remove-Item -Force -LiteralPath $reportPath
}

$logPath = Join-Path $Project "compare-pair.log"
& $Godot --headless --log-file $logPath --path $Project --scene "res://scenes/Main.tscn" -- --compare --compare-reference=$Reference --compare-candidate=$Candidate --compare-diff=$Diff --compare-report=$Report

$deadline = (Get-Date).AddSeconds(20)
while (!(Test-Path -LiteralPath $reportPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}

if (!(Test-Path -LiteralPath $reportPath)) {
    throw "Godot comparison did not produce report: $reportPath"
}
