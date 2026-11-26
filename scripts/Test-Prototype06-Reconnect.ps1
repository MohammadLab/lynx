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
$relayLog=Join-Path $env:TEMP "lynx-06-rec-relay.log"
$seedLog=Join-Path $env:TEMP "lynx-06-rec-seed.log"
$joinLog=Join-Path $env:TEMP "lynx-06-rec-join.log"
Remove-Item $relayLog,$seedLog,$joinLog -ErrorAction SilentlyContinue
$relayProject=Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject=Join-Path $root "tools\LynxOverlaySim\LynxOverlaySim.csproj"
$relay=$null;$seed=$null;$join=$null
try {
  $relay=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7862")
  Wait-ForPattern $relayLog "Listening on UDP :7862"
  $seed=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $seedLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7862","--client","821","--scenario","seed-only")
  Wait-ForPattern $seedLog "\[PASS\] Generalized overlay seed state left authoritative\."
  $join=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $joinLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7862","--client","821","--scenario","snapshot-only")
  Wait-ForPattern $joinLog "\[PASS\] Overlay snapshot recovery total=3"
  $text=Get-Content $joinLog -Raw
  foreach($kind in @("MARKER","PATH","ZONE")){if($text -notmatch $kind){throw "Snapshot missing $kind"}}
  Write-Host "[PASS] Prototype 0.6 reconnect generalized snapshot passed."
} finally {foreach($p in @($join,$seed,$relay)){if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force}}}