$ErrorActionPreference = 'Stop'

$p = 'c:\Users\tleue\OneDrive\Projekte\Scalping-Strategie\Goldfluss3.3.cs'
$bak = $p + '.bak_pending_eval_variantB'
Copy-Item -LiteralPath $p -Destination $bak -Force

$t = [System.IO.File]::ReadAllText($p)

# 1) Add tracker field (idempotent)
if ($t -notmatch '_lastPendingZonesLogClosed') {
    $t = [regex]::Replace(
        $t,
        '(\r?\n\s*private int _lastIntrabarZoneTriggerClosed = -1;\s*\r?\n)',
        "`$1        private int _lastPendingZonesLogClosed = -1;`r`n",
        1
    )
}

$newPrecheck = @'
                    int maxReadyZoneId = -1;
                    int pendingCount = 0;
                    int activeCount = 0;
                    try
                    {
                        var zonesNow = _marketStructureContext.ActiveZones;
                        activeCount = zonesNow != null ? zonesNow.Count : 0;
                        if (zonesNow != null && zonesNow.Count > 0)
                        {
                            for (int zi = 0; zi < zonesNow.Count; zi++)
                            {
                                var z = zonesNow[zi];
                                if (z == null)
                                    continue;
                                if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used)
                                    continue;
                                if (z.IsConfirmed)
                                    continue;
                                pendingCount++;
                                if (z.Id > maxReadyZoneId)
                                    maxReadyZoneId = z.Id;
                            }
                        }
                    }
                    catch { maxReadyZoneId = -1; }

                    int closedNow = bar - 1;
                    if (closedNow >= 0 && closedNow != _lastPendingZonesLogClosed)
                    {
                        this.LogInfo("[OnCalculate-PENDING-ZONES] bar=" + bar + " closed=" + closedNow + " activeZones=" + activeCount + " pendingUnconfirmed=" + pendingCount + " maxPendingId=" + maxReadyZoneId);
                        _lastPendingZonesLogClosed = closedNow;
                    }

                    bool notAlreadyTriggeredForClosed = closedNow != _lastIntrabarZoneTriggerClosed;
                    if (pendingCount > 0 && closedNow >= 0 && notAlreadyTriggeredForClosed)
                    {
                        this.LogInfo("[OnCalculate-INTRABAR-PENDING] Pending zones present (pending=" + pendingCount + ", maxPendingId=" + maxReadyZoneId + ") at bar=" + bar + " -> EvaluateSignalsAndOrders(closed=" + closedNow + ")");
                        EvaluateSignalsAndOrders(
                            closedNow,
                            ovSnapshot,
                            _ofFeaturesHistory,
                            _currentLevelsSnapshot.IsBlockedLong,
                            _currentLevelsSnapshot.IsBlockedShort,
                            _currentLevelsSnapshot,
                            _currentLevelsSnapshot.CurrentPOC,
                            _currentLevelsSnapshot.CurrentVAH,
                            _currentLevelsSnapshot.CurrentVAL,
                            _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.None,
                            new MyNamespace.Strategies.Models.DetectedOrderflowPattern(),
                            _currentMarketStateV2,
                            _currentVwapSnapshot?.Current ?? 0m);
                        _lastIntrabarZoneTriggerClosed = closedNow;
                    }

                    _lastSeenMarketStructureMaxReadyZoneId = maxReadyZoneId;
'@

# 2) Replace OnCalculate precheck trigger block with variant B (counts + immediate eval)
# Use index-based replacement to tolerate indentation/newline differences.
$anchor = 'if (_marketStructureContext != null && bar > 0 && _currentLevelsSnapshot != null && _hasOvLastClosed && ovSnapshot != null)'
$anchorIdx = $t.IndexOf($anchor)
if ($anchorIdx -lt 0) {
    throw 'Anchor for precheck not found. No changes applied.'
}

$blockStartNeedle = 'int maxReadyZoneId = -1;'
$startIdx = $t.IndexOf($blockStartNeedle, $anchorIdx)
if ($startIdx -lt 0) {
    throw 'Precheck block start not found. No changes applied.'
}

$blockEndNeedle = '_lastSeenMarketStructureMaxReadyZoneId = maxReadyZoneId;'
$endNeedleIdx = $t.IndexOf($blockEndNeedle, $startIdx)
if ($endNeedleIdx -lt 0) {
    throw 'Precheck block end needle not found. No changes applied.'
}

# include the whole end line
$lineEndIdx = $t.IndexOf([Environment]::NewLine, $endNeedleIdx)
if ($lineEndIdx -lt 0) {
    $lineEndIdx = $t.Length
} else {
    $lineEndIdx = $lineEndIdx + [Environment]::NewLine.Length
}

$t = $t.Substring(0, $startIdx) + $newPrecheck + $t.Substring($lineEndIdx)

# 3) After-update scans: treat pending as !IsConfirmed && Status!=Used (replace Ready-only continue)
$t = $t -replace 'if \(z\.Status != MyNamespace\.Strategies\.MarketAnalysis\.MarketStructureContext\.ZoneStatus\.Ready\)\s*\r?\n\s*continue;', 'if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used)\r\n                                                    continue;\r\n                                                if (z.IsConfirmed)\r\n                                                    continue;'

[System.IO.File]::WriteAllText($p, $t)
Write-Host ('Patched: ' + $p)
Write-Host ('Backup: ' + $bak)
