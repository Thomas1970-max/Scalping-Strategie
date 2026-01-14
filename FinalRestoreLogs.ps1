# Final script to completely restore original log calls
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Final restoration of original log calls..."

# Get all C# files that contain LoggerHelper calls
$csFiles = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notlike "*\.vs\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
    Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)"
    }

foreach ($file in $csFiles) {
    Write-Host "Final restore: $($file.Name)"
    
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction Stop
        
        # Remove all modifications and restore to completely original state
        # Pattern 1: Remove class prefixes completely
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?-[^"]*"', 'LoggerHelper.$1(_loggerSource, "[')
        
        # Pattern 2: Fix incomplete log calls (missing closing quote and parenthesis)
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?[^"]*$', 'LoggerHelper.$1(_loggerSource, "[$2")')
        
        # Pattern 3: Fix lines that end abruptly
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?[^"]*$', 'LoggerHelper.$1(_loggerSource, "[$2")')
        
        # Pattern 4: Ensure proper closing
        $content = [regex]::Replace($content, 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*"\[.*?[^"]*$', 'LoggerHelper.$1(_loggerSource, "[$2")')
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Restored successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Final restoration completed. Please manually check remaining syntax errors."
