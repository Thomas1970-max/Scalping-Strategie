# Manual restoration guide for log calls

## Current Status
The automated scripts have not successfully restored the original state. Manual intervention is required.

## Files with Remaining Issues
Based on build errors, these files still have syntax errors:
- Orderflow/ThresholdsResolver.cs
- Orderflow/OrderflowFeatureCalculator.cs
- Orderflow/ReversalBouncePatternEvaluator.cs

## Manual Restoration Process

### Step 1: Check Current State
Open each file and look for malformed log calls like:
```csharp
LoggerHelper.LogInfo(_loggerSource, "[ThresholdsResolver-Instance created.
```

### Step 2: Restore Original Format
Change them back to:
```csharp
LoggerHelper.LogInfo(_loggerSource, "[Instance created.");
```

### Step 3: Common Patterns to Fix
1. **Remove class prefixes**: `[ClassName-` → `[`
2. **Add missing closing quote**: Add `"` at the end
3. **Add missing closing parenthesis**: Add `)` at the end
4. **Add missing semicolon**: Add `;` at the end

### Step 4: Example Fixes

**Broken:**
```csharp
LoggerHelper.LogInfo(_loggerSource, "[ThresholdsResolver-Instance created.
LoggerHelper.LogDebug(_loggerSource, "[ThresholdsResolver-Debug message
```

**Fixed:**
```csharp
LoggerHelper.LogInfo(_loggerSource, "[Instance created.");
LoggerHelper.LogDebug(_loggerSource, "[Debug message");
```

### Step 5: Use IDE Find and Replace
1. Search for: `LoggerHelper.LogInfo(_loggerSource, "[`
2. Replace with: `LoggerHelper.LogInfo(_loggerSource, "[`
3. Manually fix each line to ensure proper syntax

### Step 6: Lines to Check
**ThresholdsResolver.cs:** Lines 33, 170, 214, 234, 262, 330
**OrderflowFeatureCalculator.cs:** Lines 92, 105, 207, 300, 428
**ReversalBouncePatternEvaluator.cs:** Line 475

### Step 7: Verify
After manual fixes, run `dotnet build` to verify all syntax errors are resolved.

## Note
The automated scripts were unable to properly handle the complex string patterns. Manual correction is the most reliable approach.
