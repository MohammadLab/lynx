$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
function Wait-ForPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 15) {
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
$relayLog=Join-Path $env:TEMP "lynx-06-rev-relay.log"
$probeLog=Join-Path $env:TEMP "lynx-06-rev-probe.log"
Remove-Item $relayLog,$probeLog -ErrorAction SilentlyContinue
$relayProject=Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$probeProject=Join-Path $root "tools\LynxOverlayProbe\LynxOverlayProbe.csproj"
$relay=$null; $probe=$null
try {
  $relay=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7861")
  Wait-ForPattern $relayLog "Listening on UDP :7861"
  $probe=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $probeLog -ArgumentList @("run","--no-build","--project",$probeProject,"--","--host","127.0.0.1","--port","7861","--client","901")
  Wait-ForPattern $probeLog "\[PASS\] Overlay relay rejection/revision probe passed\."
  Wait-ForPattern $relayLog "OVERLAY_UPDATE rejected id=8801 result=RevisionMismatch"
  Wait-ForPattern $relayLog "sender not registered at endpoint"
  Wait-ForPattern $relayLog "DROP overlay .*PATH requires at least 2 point"
  Write-Host "[PASS] Prototype 0.6 revision/ownership/geometry rejection passed."
} finally { foreach($p in @($probe,$relay)){if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force}} }