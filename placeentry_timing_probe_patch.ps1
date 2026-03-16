$ErrorActionPreference = 'Stop'

$p = 'c:\Users\tleue\OneDrive\Projekte\Scalping-Strategie\Goldfluss3.3.cs'
$bak = $p + '.bak_placeentry_timing_probe'
Copy-Item -LiteralPath $p -Destination $bak -Force

$t = [System.IO.File]::ReadAllText($p)

$needle = 'this.LogInfo($"[PlaceEntry SET] entryBarIndex={_entryBarIndex} timeoutBars={_orderTimeoutBars} " +'
$idx = $t.IndexOf($needle)
if ($idx -lt 0) { throw 'Needle not found for PlaceEntry SET log.' }

# Find end of that LogInfo statement (search next "); after idx)
$end = $t.IndexOf(');', $idx)
if ($end -lt 0) { throw 'Could not find end of PlaceEntry SET LogInfo.' }
$end = $end + 2

$insert = @'

            try
            {
                var cb = CurrentBar;
                var cbTime = cb >= 0 ? GetCandle(cb)?.Time : (DateTime?)null;
                var argTime = bar >= 0 ? GetCandle(bar)?.Time : (DateTime?)null;
                this.LogInfo($"[PlaceEntry-TIMING] CurrentBar={cb} CurrentCandleTime={(cbTime.HasValue ? cbTime.Value.ToString("O") : "-")} argBar={bar} argCandleTime={(argTime.HasValue ? argTime.Value.ToString("O") : "-")} nowUtc={DateTime.UtcNow:O}");
            }
            catch { }
'@

$t = $t.Substring(0, $end) + $insert + $t.Substring($end)

[System.IO.File]::WriteAllText($p, $t)
Write-Host ('Patched PlaceEntry timing probe. Backup: ' + $bak)
