# Script to completely restore original log calls (remove all modifications)
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Completely restoring original log calls..."

# Get all C# files that contain LoggerHelper calls
$csFiles = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notlike "*\.vs\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
    Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)"
    }

foreach ($file in $csFiles) {
    Write-Host "Restoring: $($file.Name)"
    
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction Stop
        
        # Remove all class prefixes and restore original format
        # Pattern: LoggerHelper.LogInfo(_loggerSource, "[ClassName-Message" -> LoggerHelper.LogInfo(_loggerSource, "[Message"
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?-[^"]*"', 'LoggerHelper.$1(_loggerSource, "[')
        
        # Also fix cases where the prefix was partially applied
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?-[^"]*$', 'LoggerHelper.$1(_loggerSource, "[')
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Restored successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Complete restore script completed. All log calls should be back to original state."
