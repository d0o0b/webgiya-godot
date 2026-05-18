param(
    [string]$Project = "D:\Tom\code\github\webgiya-godot",
    [string]$ReferenceProject = "D:\Tom\code\github\webgiya",
    [string]$Godot = "C:\Bin\Godot\CurrentVersion\Godot.exe",
    [string[]]$Scenes = @("cornell-box", "sponza", "leonardo"),
    [string[]]$Modes = @("direct", "indirect", "combined"),
    [string[]]$Views = @("default"),
    [string]$OutputDir = "screenshots\matrix",
    [string]$ReferenceDir = "screenshots\reference",
    [switch]$CaptureReference,
    [ValidateSet("HeadedCdp", "Headless")]
    [string]$ReferenceCaptureMode = "HeadedCdp",
    [int]$ReferenceWaitSeconds = 5,
    [int]$ReferenceCdpCommandTimeoutSeconds = 60,
    [int]$ReferenceMinBytes = 100000,
    [float]$Delay = 2.0
)

$ErrorActionPreference = "Stop"

function Resolve-ProjectPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $Project $Path
}

function Get-LatestCandidate {
    param(
        [string]$Directory,
        [string]$Scene,
        [string]$Mode,
        [string]$View
    )

    $pattern = "$Scene-$Mode-$View-*.png"
    $plain = Get-ChildItem -LiteralPath $Directory -Filter $pattern -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch "-surfels-" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    return $plain
}

$outputRoot = Resolve-ProjectPath $OutputDir
$candidateDir = Join-Path $outputRoot "candidates"
$diffDir = Join-Path $outputRoot "diffs"
$reportDir = Join-Path $outputRoot "reports"
$referenceRoot = Resolve-ProjectPath $ReferenceDir
New-Item -ItemType Directory -Force -Path $candidateDir, $diffDir, $reportDir | Out-Null

if ($CaptureReference) {
    & (Join-Path $Project "tools\capture-reference.ps1") `
        -ReferenceProject $ReferenceProject `
        -OutputDir $referenceRoot `
        -Scenes $Scenes `
        -CaptureMode $ReferenceCaptureMode `
        -WaitSeconds $ReferenceWaitSeconds `
        -CdpCommandTimeoutSeconds $ReferenceCdpCommandTimeoutSeconds
}

$sceneCsv = [string]::Join(",", $Scenes)
$modeCsv = [string]::Join(",", $Modes)
$viewCsv = [string]::Join(",", $Views)
$godotLog = Join-Path $outputRoot "godot-capture.log"
$godotArgs = @(
    "--path", $Project,
    "--scene", "res://scenes/Main.tscn",
    "--log-file", $godotLog,
    "--",
    "--screenshots",
    "--screenshot-scenes=$sceneCsv",
    "--screenshot-modes=$modeCsv",
    "--screenshot-views=$viewCsv",
    "--screenshot-delay=$Delay",
    "--screenshot-dir=$candidateDir",
    "--screenshot-hide-ui",
    "--render-quality=ultra",
    "--export-render-report"
)
$godotProcess = Start-Process -FilePath $Godot -ArgumentList $godotArgs -WorkingDirectory $Project -WindowStyle Hidden -PassThru -Wait
if ($godotProcess.ExitCode -ne 0) {
    throw "Godot screenshot capture failed with exit code $($godotProcess.ExitCode). See $godotLog"
}

$rows = @()
foreach ($scene in $Scenes) {
    $reference = Join-Path $referenceRoot "$scene-reference.png"
    $referenceItem = if (Test-Path -LiteralPath $reference) { Get-Item -LiteralPath $reference } else { $null }
    $referenceValid = $referenceItem -ne $null -and $referenceItem.Length -ge $ReferenceMinBytes

    foreach ($mode in $Modes) {
        foreach ($view in $Views) {
            $candidate = Get-LatestCandidate -Directory $candidateDir -Scene $scene -Mode $mode -View $view
            $row = [ordered]@{
                scene = $scene
                mode = $mode
                view = $view
                reference = $reference
                referenceValid = $referenceValid
                candidate = if ($candidate) { $candidate.FullName } else { "" }
                meanAbsoluteError = $null
                rootMeanSquareError = $null
                maxError = $null
                percentPixelsOver05 = $null
                status = "missing-candidate"
            }

            if ($candidate -and $referenceValid) {
                $safeName = "$scene-$mode-$view"
                $diff = Join-Path $diffDir "$safeName-diff.png"
                $report = Join-Path $reportDir "$safeName-compare.json"
                & (Join-Path $Project "tools\compare-pair.ps1") `
                    -Godot $Godot `
                    -Project $Project `
                    -Reference $reference `
                    -Candidate $candidate.FullName `
                    -Diff $diff `
                    -Report $report

                $metrics = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
                $row.meanAbsoluteError = $metrics.meanAbsoluteError
                $row.rootMeanSquareError = $metrics.rootMeanSquareError
                $row.maxError = $metrics.maxError
                $row.percentPixelsOver05 = $metrics.percentPixelsOver05
                $row.status = "compared"
            }
            elseif ($candidate -and !$referenceValid) {
                $row.status = "invalid-reference"
            }

            $rows += [pscustomobject]$row
        }
    }
}

$jsonPath = Join-Path $outputRoot "visual-matrix.json"
$mdPath = Join-Path $outputRoot "visual-matrix.md"
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Visual Matrix")
$lines.Add("")
$lines.Add("Generated: $(Get-Date -Format s)")
$lines.Add("")
$lines.Add("| Scene | Mode | View | Status | MAE | RMSE | >0.05 | Candidate |")
$lines.Add("| --- | --- | --- | --- | ---: | ---: | ---: | --- |")
foreach ($row in $rows) {
    $mae = if ($null -eq $row.meanAbsoluteError) { "" } else { "{0:0.000000}" -f [double]$row.meanAbsoluteError }
    $rmse = if ($null -eq $row.rootMeanSquareError) { "" } else { "{0:0.000000}" -f [double]$row.rootMeanSquareError }
    $over = if ($null -eq $row.percentPixelsOver05) { "" } else { "{0:0.00}" -f [double]$row.percentPixelsOver05 }
    $candidateName = if ([string]::IsNullOrWhiteSpace($row.candidate)) { "" } else { [System.IO.Path]::GetFileName($row.candidate) }
    $lines.Add("| $($row.scene) | $($row.mode) | $($row.view) | $($row.status) | $mae | $rmse | $over | $candidateName |")
}

$lines.Add("")
$lines.Add("Notes:")
$lines.Add("- `invalid-reference` means the reference file is missing or below the byte threshold, usually a browser/WebGPU loading or error capture.")
$lines.Add("- Godot screenshots are captured with `--render-quality=ultra`, UI hidden, and the requested delay.")
$lines | Set-Content -LiteralPath $mdPath -Encoding UTF8

Write-Host "Saved matrix JSON: $jsonPath"
Write-Host "Saved matrix report: $mdPath"
