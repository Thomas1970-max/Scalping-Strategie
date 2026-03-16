$ErrorActionPreference = 'Stop'

$p = 'c:\Users\tleue\OneDrive\Projekte\Scalping-Strategie\Goldfluss3.3.cs'
$bak = $p + '.bak_pending_eval_variantB'

if (-not (Test-Path -LiteralPath $bak)) {
    throw ('Backup not found: ' + $bak)
}

Copy-Item -LiteralPath $bak -Destination $p -Force
Write-Host ('Restored: ' + $p)
Write-Host ('From:     ' + $bak)
