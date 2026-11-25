$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-discovery-relay.log"
Remove-Item $relayLog -ErrorAction SilentlyContinue

function Wait-ForPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 8) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            $text = Get-Content $Path -Raw -ErrorAction SilentlyContinue
            if ($text -match $Pattern) { return }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for pattern: $Pattern"
}

Write-Host "[Discovery] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$relayArgs = @(
    "run", "--no-build", "--project", $relayProject, "--",
    "--listen", "7805"
)
$relay = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Wait-ForPattern $relayLog "Discovery shares UDP :7805"

    Write-Host "[Discovery] Sending discovery probe..."
    $udp = [System.Net.Sockets.UdpClient]::new()
    try {
        $udp.Client.ReceiveTimeout = 2000
        $request = [System.Text.Encoding]::UTF8.GetBytes("LYNX_DISCOVER_V1")
        [void]$udp.Send($request, $request.Length, "127.0.0.1", 7805)

        $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
        $responseBytes = $udp.Receive([ref]$remote)
        $response = [System.Text.Encoding]::UTF8.GetString($responseBytes)

        if ($response -ne "LYNX_RELAY_V1|7805") {
            throw "Unexpected discovery response: $response"
        }
    } finally {
        $udp.Dispose()
    }

    Wait-ForPattern $relayLog "DISCOVERY reply .*relayPort=7805"

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 relay-discovery regression passed."
    Write-Host "[PASS] Discovery probe received a valid relay announcement."
    Write-Host "[PASS] Advertised service port matched the relay listen port."
}
finally {
    if ($relay -and -not $relay.HasExited) {
        Stop-Process -Id $relay.Id -Force
    }
}