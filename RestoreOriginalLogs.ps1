# Script to restore log calls to original state (remove class prefixes)
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Restoring log calls to original state..."

# Get all C# files that contain LoggerHelper calls with class prefixes
$csFiles = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notlike "*\.vs\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
    Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError).*\[.*-"
    }

foreach ($file in $csFiles) {
    $className = [System.IO.Path]::GetFileNameWithoutExtension($file.FullName)
    
    Write-Host "Restoring: $className"
    
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction Stop
        
        # Remove class prefixes from log messages
        $content = $content -replace "LoggerHelper\.LogInfo\(_loggerSource, ""\[$className-", "LoggerHelper.LogInfo(_loggerSource, ""["
        $content = $content -replace "LoggerHelper\.LogDebug\(_loggerSource, ""\[$className-", "LoggerHelper.LogDebug(_loggerSource, ""["
        $content = $content -replace "LoggerHelper\.LogWarn\(_loggerSource, ""\[$className-", "LoggerHelper.LogWarn(_loggerSource, ""["
        $content = $content -replace "LoggerHelper\.LogError\(_loggerSource, ""\[$className-", "LoggerHelper.LogError(_loggerSource, ""["
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Restored successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Restore script completed. The log calls should now be back to their original state."
