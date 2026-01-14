# Final script to fix all remaining log syntax errors
param(
    [string]$ProjectPath = "c:/Users/User/source/repos/Scalping-Strategie"
)

Write-Host "Final fix for all remaining log syntax errors..."

# Get all C# files that contain LoggerHelper calls
$csFiles = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notlike "*\.vs\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
    Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)"
    }

foreach ($file in $csFiles) {
    Write-Host "Final fix: $($file.Name)"
    
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction Stop
        
        # Fix all log syntax issues comprehensively
        # Pattern 1: Missing quotes around entire message
        $content = [regex]::Replace($content, 
            'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*([^"]*?)\)', 
            {
                param($match)
                $logType = $match.Groups[1].Value
                $message = $match.Groups[2].Value
                
                # Ensure message is properly quoted
                if (-not $message.StartsWith('"')) {
                    $message = "`"$message`""
                }
                
                return "LoggerHelper.$logType(_loggerSource, $message)"
            })
        
        # Pattern 2: Fix specific malformed patterns
        $content = $content -replace 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*([^"]*?)\);', 'LoggerHelper.$1(_loggerSource, "$2");'
        
        # Pattern 3: Fix lines with missing quotes
        $content = $content -replace 'LoggerHelper\.(LogInfo|LogDebug|LogWarn|LogError)\(_loggerSource,\s*([^"]*?)([^)]*)\)', 'LoggerHelper.$1(_loggerSource, "$2$3")'
        
        # Write back to file
        Set-Content $file.FullName $content -ErrorAction Stop
        Write-Host "  Fixed successfully"
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}

Write-Host "Final fix completed. Please run 'dotnet build' to verify."
