param(
    [string]$ReferenceProject = "D:\Tom\code\github\webgiya",
    [string]$OutputDir = "D:\Tom\code\github\webgiya-godot\screenshots\reference",
    [string[]]$Scenes = @("cornell-box", "leonardo", "occlusion", "marble-bust", "sponza", "beast"),
    [int]$Width = 1600,
    [int]$Height = 900,
    [int]$Port = 5173,
    [int]$VirtualTimeBudgetMs = 30000,
    [ValidateSet("HeadedCdp", "Headless")]
    [string]$CaptureMode = "HeadedCdp",
    [ValidateSet("https", "http")]
    [string]$Protocol = "https",
    [int]$DevToolsPort = 9222,
    [int]$WaitSeconds = 5,
    [int]$CdpCommandTimeoutSeconds = 60,
    [int]$ReadyTimeoutSeconds = 30,
    [switch]$UsePageScreenshotFallback
)

$ErrorActionPreference = "Stop"

function Resolve-Browser {
    $candidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
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

function Wait-For-DevToolsTarget {
    param(
        [int]$PortNumber,
        [string]$Scene,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $targets = @(Invoke-RestMethod -Uri "http://127.0.0.1:$PortNumber/json" -TimeoutSec 2)
            $target = $targets |
                Where-Object {
                    $_.type -eq "page" -and
                    $_.webSocketDebuggerUrl -and
                    $_.url -like "http*" -and
                    $_.url -match "scene=$([Regex]::Escape($Scene))"
                } |
                Select-Object -First 1

            if ($target) {
                return [string]@($target.webSocketDebuggerUrl)[0]
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for DevTools target for scene '$Scene' on port $PortNumber."
}

function Send-CdpCommand {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$Id,
        [string]$Method,
        [hashtable]$Params = @{}
    )

    $payload = @{
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 10 -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $segment = [System.ArraySegment[byte]]::new($bytes)
    $timeoutSeconds = [Math]::Max($script:CdpCommandTimeoutSeconds, 5)
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($timeoutSeconds))
    try {
        $Socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult() | Out-Null

        $buffer = New-Object byte[] 1048576
        while ($true) {
            $builder = [System.Text.StringBuilder]::new()
            do {
                $receiveSegment = [System.ArraySegment[byte]]::new($buffer)
                $result = $Socket.ReceiveAsync($receiveSegment, $timeout.Token).GetAwaiter().GetResult()
                if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                    throw "DevTools WebSocket closed while waiting for '$Method'."
                }

                $builder.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)) | Out-Null
            } while (!$result.EndOfMessage)

            $message = $builder.ToString() | ConvertFrom-Json
            if ($message.id -eq $Id) {
                if ($message.error) {
                    throw "DevTools command '$Method' failed: $($message.error.message)"
                }

                return $message.result
            }
        }
    }
    catch [OperationCanceledException] {
        throw "Timed out waiting for DevTools command '$Method'."
    }
    finally {
        $timeout.Dispose()
    }
}

function Get-ReferencePageState {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$Id
    )

    $state = Send-CdpCommand -Socket $Socket -Id $Id -Method "Runtime.evaluate" -Params @{
        expression = @"
(() => {
  const canvases = Array.from(document.querySelectorAll('canvas'));
  const canvas = canvases[0] || null;
  const loading = document.querySelector('#loading-overlay');
  const error = document.querySelector('#error-overlay');
  const errorMessage = document.querySelector('#error-message');
  const isHidden = (element) => !element || element.hidden || element.classList.contains('hidden');
  const text = document.body ? document.body.innerText : '';
  return {
    url: location.href,
    title: document.title,
    readyState: document.readyState,
    canvasCount: canvases.length,
    canvasWidth: canvas ? canvas.width : 0,
    canvasHeight: canvas ? canvas.height : 0,
    loadingVisible: !isHidden(loading),
    errorVisible: !isHidden(error),
    errorText: errorMessage ? errorMessage.textContent : '',
    bodyText: text.slice(0, 1200)
  };
})()
"@
        returnByValue = $true
    }

    return $state.result.value
}

function Wait-For-ReferenceFrameReady {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$StartId,
        [string]$Scene,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max($TimeoutSeconds, 5))
    $id = $StartId
    $lastState = $null

    while ((Get-Date) -lt $deadline) {
        $state = Get-ReferencePageState -Socket $Socket -Id $id
        $id++
        $lastState = $state

        if ($state.errorVisible) {
            $diagnostic = $state | ConvertTo-Json -Depth 6 -Compress
            throw "Reference app reported an error for '$Scene': $diagnostic"
        }

        if ($state.canvasCount -gt 0 -and $state.canvasWidth -gt 0 -and $state.canvasHeight -gt 0 -and !$state.loadingVisible) {
            return @{
                NextId = $id
                State = $state
            }
        }

        Start-Sleep -Milliseconds 250
    }

    $lastDiagnostic = if ($lastState) { $lastState | ConvertTo-Json -Depth 6 -Compress } else { "no state collected" }
    throw "Timed out waiting for reference canvas for '$Scene'. Last page state: $lastDiagnostic"
}

function Capture-HeadedCdpScreenshot {
    param(
        [string]$Browser,
        [string]$Url,
        [string]$Scene,
        [string]$OutPath,
        [int]$Width,
        [int]$Height,
        [int]$DevToolsPort,
        [int]$WaitSeconds
    )

    $profileDir = Join-Path ([System.IO.Path]::GetTempPath()) "webgiya-reference-capture-$([Guid]::NewGuid().ToString("N"))"
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null

    $args = @(
        "--remote-debugging-port=$DevToolsPort",
        "--user-data-dir=$profileDir",
        "--ignore-certificate-errors",
        "--allow-insecure-localhost",
        "--enable-unsafe-webgpu",
        "--ignore-gpu-blocklist",
        "--disable-gpu-sandbox",
        "--disable-web-security",
        "--disable-extensions",
        "--disable-component-extensions-with-background-pages",
        "--unsafely-treat-insecure-origin-as-secure=$Url",
        "--use-angle=vulkan",
        "--enable-features=Vulkan,WebGPU,UnsafeWebGPU",
        "--no-first-run",
        "--no-default-browser-check",
        "--window-size=$Width,$Height",
        "--new-window",
        $Url
    )

    $browserProcess = Start-Process -FilePath $Browser -ArgumentList $args -PassThru
    try {
        $wsUrl = Wait-For-DevToolsTarget -PortNumber $DevToolsPort -Scene $Scene
        $socket = [System.Net.WebSockets.ClientWebSocket]::new()
        try {
            $socket.ConnectAsync([Uri]$wsUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
            $id = 1
            Send-CdpCommand -Socket $socket -Id $id -Method "Page.enable" | Out-Null
            $id++
            Send-CdpCommand -Socket $socket -Id $id -Method "Runtime.enable" | Out-Null
            $id++
            Send-CdpCommand -Socket $socket -Id $id -Method "Emulation.setDeviceMetricsOverride" -Params @{
                width = $Width
                height = $Height
                deviceScaleFactor = 1
                mobile = $false
            } | Out-Null

            $ready = Wait-For-ReferenceFrameReady `
                -Socket $socket `
                -StartId ($id + 1) `
                -Scene $Scene `
                -TimeoutSeconds $script:ReadyTimeoutSeconds
            $id = $ready.NextId

            Start-Sleep -Seconds ([Math]::Min([Math]::Max($WaitSeconds, 2), 5))

            $id++
            $body = Send-CdpCommand -Socket $socket -Id $id -Method "Runtime.evaluate" -Params @{
                expression = "document.body ? document.body.innerText : ''"
                returnByValue = $true
            }
            $bodyText = $body.result.value
            if ($bodyText -match "Something went wrong" -or $bodyText -match "Cannot read properties") {
                Write-Warning "Reference page text contains an error marker for '$Scene'. Capturing anyway because the WebGPU frame may still be valid."
            }

            $id++
            $canvas = Send-CdpCommand -Socket $socket -Id $id -Method "Runtime.evaluate" -Params @{
                expression = "(() => { try { const canvases = Array.from(document.querySelectorAll('canvas')); const canvas = canvases[0]; if (!canvas) return { ok: false, reason: 'no canvas', count: canvases.length }; return { ok: true, count: canvases.length, width: canvas.width, height: canvas.height, data: canvas.toDataURL('image/png') }; } catch (error) { return { ok: false, reason: String(error), count: document.querySelectorAll('canvas').length }; } })()"
                returnByValue = $true
            }
            $canvasResult = $canvas.result.value
            $canvasDataUrl = [string]$canvasResult.data
            if ($canvasDataUrl.StartsWith("data:image/png;base64,")) {
                $base64 = $canvasDataUrl.Substring("data:image/png;base64,".Length)
                [System.IO.File]::WriteAllBytes($OutPath, [Convert]::FromBase64String($base64))
                return
            }

            if (!$script:UsePageScreenshotFallback) {
                $pageState = Get-ReferencePageState -Socket $socket -Id ($id + 1)
                $diagnostic = @{
                    canvas = $canvasResult
                    page = $pageState
                } | ConvertTo-Json -Depth 6 -Compress
                throw "Canvas screenshot did not return PNG data for '$Scene'. Canvas diagnostic: $diagnostic"
            }

            $id++
            $capture = Send-CdpCommand -Socket $socket -Id $id -Method "Page.captureScreenshot" -Params @{
                format = "png"
                fromSurface = $true
                captureBeyondViewport = $false
            }

            [System.IO.File]::WriteAllBytes($OutPath, [Convert]::FromBase64String($capture.data))
        }
        finally {
            if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
            }
            $socket.Dispose()
        }
    }
    finally {
        if (!$browserProcess.HasExited) {
            Stop-Process -Id $browserProcess.Id -Force -ErrorAction SilentlyContinue
        }

        if ((Test-Path -LiteralPath $profileDir) -and $profileDir.StartsWith([System.IO.Path]::GetTempPath())) {
            Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Capture-HeadlessScreenshot {
    param(
        [string]$Browser,
        [string]$Url,
        [string]$OutPath,
        [int]$Width,
        [int]$Height,
        [int]$VirtualTimeBudgetMs
    )

    $args = @(
        "--headless=new",
        "--ignore-certificate-errors",
        "--allow-insecure-localhost",
        "--enable-unsafe-webgpu",
        "--ignore-gpu-blocklist",
        "--disable-gpu-sandbox",
        "--disable-web-security",
        "--disable-extensions",
        "--disable-component-extensions-with-background-pages",
        "--unsafely-treat-insecure-origin-as-secure=$Url",
        "--use-angle=vulkan",
        "--enable-features=Vulkan,WebGPU,UnsafeWebGPU",
        "--window-size=$Width,$Height",
        "--virtual-time-budget=$VirtualTimeBudgetMs",
        "--screenshot=$OutPath",
        $Url
    )

    $browserProcess = Start-Process -FilePath $Browser -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
    if ($browserProcess.ExitCode -ne 0) {
        throw "Browser screenshot failed with exit code $($browserProcess.ExitCode)."
    }
}

if (!(Test-Path -LiteralPath $ReferenceProject)) {
    throw "Reference project not found: $ReferenceProject"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$browser = Resolve-Browser
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$vite = Start-Process -FilePath $npm -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", "$Port", "--strictPort") -WorkingDirectory $ReferenceProject -WindowStyle Hidden -PassThru

try {
    Wait-For-Port -HostName "127.0.0.1" -PortNumber $Port

    foreach ($scene in $Scenes) {
        $url = "$Protocol`://127.0.0.1:$Port/?scene=$scene"
        $outPath = Join-Path $OutputDir "$scene-reference.png"

        if ($CaptureMode -eq "HeadedCdp") {
            Capture-HeadedCdpScreenshot `
                -Browser $browser `
                -Url $url `
                -Scene $scene `
                -OutPath $outPath `
                -Width $Width `
                -Height $Height `
                -DevToolsPort $DevToolsPort `
                -WaitSeconds $WaitSeconds
        }
        else {
            Capture-HeadlessScreenshot `
                -Browser $browser `
                -Url $url `
                -OutPath $outPath `
                -Width $Width `
                -Height $Height `
                -VirtualTimeBudgetMs $VirtualTimeBudgetMs
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
