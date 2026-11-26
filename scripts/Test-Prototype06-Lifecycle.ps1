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
$relayLog = Join-Path $env:TEMP "lynx-06-life-relay.log"
$watchLog = Join-Path $env:TEMP "lynx-06-life-watch.log"
$actorLog = Join-Path $env:TEMP "lynx-06-life-actor.log"
Remove-Item $relayLog,$watchLog,$actorLog -ErrorAction SilentlyContinue
$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject = Join-Path $root "tools\LynxOverlaySim\LynxOverlaySim.csproj"
$relay=$null; $watch=$null; $actor=$null
try {
  $relay=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7860")
  Wait-ForPattern $relayLog "Listening on UDP :7860"
  $watch=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $watchLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7860","--client","812","--scenario","watch-lifecycle")
  Wait-ForPattern $relayLog "OVERLAY-SNAPSHOT-REQUEST client=812"
  $actor=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $actorLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7860","--client","811","--scenario","types")
  Wait-ForPattern $actorLog "\[PASS\] Generalized marker/path/zone lifecycle passed\."
  Wait-ForPattern $watchLog "\[PASS\] Independent overlay viewer saw generalized lifecycle\."
  $relayText=Get-Content $relayLog -Raw
  if($relayText -notmatch "OVERLAY CREATE .*kind=MARKER"){throw "Marker create missing."}
  if($relayText -notmatch "OVERLAY CREATE .*kind=PATH"){throw "Path create missing."}
  if($relayText -notmatch "OVERLAY CREATE .*kind=ZONE"){throw "Zone create missing."}
  if($relayText -notmatch "OVERLAY UPDATE .*rev=2"){throw "Revision 2 update missing."}
  Write-Host "[PASS] Prototype 0.6 generalized lifecycle/fan-out passed."
} finally { foreach($p in @($actor,$watch,$relay)){if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force}} }