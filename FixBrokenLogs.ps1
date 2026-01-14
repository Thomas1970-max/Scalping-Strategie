# Script to fix broken log calls by restoring proper syntax
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Fixing broken log calls..."

# Get all C# files that contain broken LoggerHelper calls
$csFiles = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notlike "*\.vs\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
    Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError).*\[.*-.*\]"
    }

foreach ($file in $csFiles) {
    $className = [System.IO.Path]::GetFileNameWithoutExtension($file.FullName)
    
    Write-Host "Fixing: $className"
    
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction Stop
        
        # Fix broken log calls by removing class prefixes and restoring proper syntax
        $content = $content -replace "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource, ""\[$className-([^]]*)\]", "LoggerHelper.`$1(_loggerSource, ""[$2`""
        $content = $content -replace "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource, ""\[$className-([^]]*)", "LoggerHelper.`$1(_loggerSource, ""[$2"
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Fixed successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Fix script completed. The log calls should now have proper syntax."
