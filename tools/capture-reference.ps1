param(
    [string]$ReferenceProject = "D:\Tom\code\github\webgiya",
    [string]$OutputDir = "D:\Tom\code\github\webgiya-godot\screenshots\reference",
    [string[]]$Scenes = @("cornell-box", "leonardo", "occlusion", "marble-bust", "sponza", "beast"),
    [int]$Width = 1600,
    [int]$Height = 900,
    [int]$Port = 5173,
    [int]$VirtualTimeBudgetMs = 30000
)

$ErrorActionPreference = "Stop"

function Resolve-Browser {
    $candidates = @(
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "No Edge or Chrome executable found."
}

function Wait-For-Port {
    param(
        [string]$HostName,
        [int]$PortNumber,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $connect = $client.BeginConnect($HostName, $PortNumber, $null, $null)
            if ($connect.AsyncWaitHandle.WaitOne(500)) {
                $client.EndConnect($connect)
                return
            }
        }
        catch {
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for $HostName`:$PortNumber."
}

if (!(Test-Path -LiteralPath $ReferenceProject)) {
    throw "Reference project not found: $ReferenceProject"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$browser = Resolve-Browser
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$vite = Start-Process -FilePath $npm -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", "$Port") -WorkingDirectory $ReferenceProject -WindowStyle Hidden -PassThru

try {
    Wait-For-Port -HostName "127.0.0.1" -PortNumber $Port

    foreach ($scene in $Scenes) {
        $url = "https://127.0.0.1:$Port/?scene=$scene"
        $outPath = Join-Path $OutputDir "$scene-reference.png"
        $args = @(
            "--headless=new",
            "--ignore-certificate-errors",
            "--enable-unsafe-webgpu",
            "--ignore-gpu-blocklist",
            "--disable-gpu-sandbox",
            "--use-angle=vulkan",
            "--enable-features=Vulkan,WebGPU,UnsafeWebGPU",
            "--window-size=$Width,$Height",
            "--virtual-time-budget=$VirtualTimeBudgetMs",
            "--screenshot=$outPath",
            $url
        )

        $browserProcess = Start-Process -FilePath $browser -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
        if ($browserProcess.ExitCode -ne 0) {
            throw "Browser screenshot failed for scene '$scene' with exit code $($browserProcess.ExitCode)."
        }

        Write-Host "Saved reference screenshot: $outPath"
        $file = Get-Item -LiteralPath $outPath
        if ($file.Length -lt 100000) {
            Write-Warning "Reference screenshot for '$scene' is very small. It may still be the loading or error overlay rather than a completed WebGPU frame."
        }
    }
}
finally {
    if (!$vite.HasExited) {
        Stop-Process -Id $vite.Id -Force
    }

    $escapedReferenceProject = [Regex]::Escape($ReferenceProject)
    Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -eq "node.exe" -and
            $_.CommandLine -match $escapedReferenceProject -and
            $_.CommandLine -match "vite"
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}
