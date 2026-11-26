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
$r1Log=Join-Path $env:TEMP "lynx-06-restart-r1.log"
$r2Log=Join-Path $env:TEMP "lynx-06-restart-r2.log"
$simLog=Join-Path $env:TEMP "lynx-06-restart-sim.log"
Remove-Item $r1Log,$r2Log,$simLog -ErrorAction SilentlyContinue
$relayProject=Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject=Join-Path $root "tools\LynxOverlaySim\LynxOverlaySim.csproj"
$args=@("run","--no-build","--project",$relayProject,"--","--listen","7864")
$r1=$null;$r2=$null;$sim=$null
try {
  $r1=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $r1Log -ArgumentList $args
  Wait-ForPattern $r1Log "Listening on UDP :7864"
  $sim=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $simLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7864","--client","841","--scenario","restart-watch")
  Wait-ForPattern $simLog "WAITING FOR RELAY RESTART"
  Stop-Process -Id $r1.Id -Force; $r1=$null
  Start-Sleep -Milliseconds 1200
  $r2=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $r2Log -ArgumentList $args
  Wait-ForPattern $r2Log "Listening on UDP :7864"
  Wait-ForPattern $simLog "\[PASS\] Generalized overlay relay restart recovery confirmed\." 20
  Write-Host "[PASS] Prototype 0.6 relay restart/new-epoch recovery passed."
} finally {foreach($p in @($sim,$r2,$r1)){if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force}}}