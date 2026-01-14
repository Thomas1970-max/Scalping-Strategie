# Script to restore project to original state before any log modifications
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Restoring project to original state before log modifications..."

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
        
        # Remove all class prefixes and restore original format completely
        # Pattern: LoggerHelper.LogInfo(_loggerSource, "[ClassName-Message" -> LoggerHelper.LogInfo(_loggerSource, "[Message"
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?-[^"]*"', 'LoggerHelper.$1(_loggerSource, "[')
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?-[^"]*$', 'LoggerHelper.$1(_loggerSource, "[')
        
        # Also fix any remaining malformed patterns
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?[^"]*$', 'LoggerHelper.$1(_loggerSource, "[')
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Restored successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Project restored to original state. All log calls should be back to their original format."
