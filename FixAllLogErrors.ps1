# Script to fix all remaining log syntax errors
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Fixing all remaining log syntax errors..."

# Files with known issues
$filesToFix = @(
    "Orderflow\BreakoutPatternEvaluator .cs",
    "Orderflow\ThresholdsResolver.cs",
    "Orderflow\OrderflowFeatureCalculator.cs",
    "Orderflow\ReversalBouncePatternEvaluator.cs"
)

foreach ($fileName in $filesToFix) {
    $filePath = Join-Path $ProjectPath $fileName
    if (Test-Path $filePath) {
        Write-Host "Fixing: $fileName"
        
        try {
            $content = Get-Content $filePath -Raw -ErrorAction Stop
            
            # Fix common log syntax issues
            # Pattern 1: Missing quotes around message
            $content = [regex]::Replace($content, 
                'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*([^"]*?[^"]*?)\)', 
                'LoggerHelper.$1(_loggerSource, "$2")')
            
            # Pattern 2: Missing opening quote
            $content = [regex]::Replace($content, 
                'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*([^["]*?)\)', 
                'LoggerHelper.$1(_loggerSource, "$2")')
            
            # Pattern 3: Fix specific known issues
            $content = $content -replace 'LoggerHelper\.LogWarn\(_loggerSource, \[CalculateAdaptiveThresholds\] Keine Historie verfügbar; Standardwerte zurücksetzen und zurückkehren.\)', 'LoggerHelper.LogWarn(_loggerSource, "[CalculateAdaptiveThresholds] Keine Historie verfügbar; Standardwerte zurücksetzen und zurückkehren.")'
            
            # Write back to file
            Set-Content $filePath $content -ErrorAction Stop
            Write-Host "  Fixed successfully"
        }
        catch {
            Write-Host "  Error: $($_.Exception.Message)"
        }
    }
}

Write-Host "Fix script completed. Please run 'dotnet build' to verify."
