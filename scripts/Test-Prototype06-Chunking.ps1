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
$relayLog=Join-Path $env:TEMP "lynx-06-chunk-relay.log"
$seedLog=Join-Path $env:TEMP "lynx-06-chunk-seed.log"
$joinLog=Join-Path $env:TEMP "lynx-06-chunk-join.log"
Remove-Item $relayLog,$seedLog,$joinLog -ErrorAction SilentlyContinue
$relayProject=Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject=Join-Path $root "tools\LynxOverlaySim\LynxOverlaySim.csproj"
$relay=$null;$seed=$null;$join=$null
try {
  $relay=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7863")
  Wait-ForPattern $relayLog "Listening on UDP :7863"
  $seed=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $seedLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7863","--client","831","--scenario","bulk-seed")
  Wait-ForPattern $seedLog "\[PASS\] Bulk overlay seed complete total=20\." 20
  $join=Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $joinLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7863","--client","832","--scenario","snapshot-only")
  Wait-ForPattern $joinLog "\[PASS\] Overlay snapshot recovery total=20" 20
  $relayText=Get-Content $relayLog -Raw
  $sizes=[regex]::Matches($relayText,"OVERLAY_SNAPSHOT chunk=\d+/\d+ bytes=(\d+)")
  if($sizes.Count -lt 2){throw "Overlay snapshot did not split into multiple chunks."}
  foreach($m in $sizes){if([int]$m.Groups[1].Value -gt 1200){throw "Overlay snapshot chunk exceeded 1200 bytes."}}
  Write-Host "[PASS] Prototype 0.6 variable-geometry snapshot chunking passed."
} finally {foreach($p in @($join,$seed,$relay)){if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force}}}