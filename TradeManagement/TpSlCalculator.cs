using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using static MyNamespace.Strategies.Goldfluss3_3;
using ATAS.Indicators;
using ATAS.Strategies;
using ATAS.Indicators.Technical;
using System.Diagnostics;
using Utils.Common.Logging;

namespace MyNamespace.Strategies.TradeManagement
{
    public class TpSlCalculator : ITpSlCalculator
    {
        private const decimal DefaultTpTicks = 12m;
        private const decimal DefaultSlTicks = 8m;
        private const decimal DefaultBeOffset = 0m;
        private const decimal DefaultTrailingOffset = 0m;
        private const decimal DefaultTrailingActivateAfterTicks = 0m;
        private const decimal DefaultMaxEarlyExitDistTicks = decimal.MaxValue;
        private const int DefaultMinSlTicks = 3;
        private const int DefaultMaxSlTicks = 20;
        private const decimal DefaultBaseSlTicksAtAvgVol = 8m;
        private const decimal DefaultSlVolPerSecondScaleMultiplier = 1.0m;
        private const string DefaultTpType = "Ticks";
        private const string DefaultSlType = "Ticks";

        private bool TryResolveAbsoluteStopFromSetup(decimal entryPrice, TpSlContext ctx, out decimal slPrice, out string reason)
        {
            slPrice = 0m;
            reason = string.Empty;

            var setup = ctx.SetupParams;
            if (setup == null)
            {
                reason = "SetupParams null";
                return false;
            }

            if (setup.SuggestedStopLossPrice <= 0m)
            {
                reason = "SuggestedStopLossPrice not set";
                return false;
            }

            var suggested = setup.SuggestedStopLossPrice;

            // Validierung: SL muss auf der richtigen Seite vom Entry liegen.
            // Long: SL < Entry, Short: SL > Entry
            if (ctx.Direction == OrderDirections.Buy)
            {
                if (suggested >= entryPrice)
                {
                    reason = $"SuggestedStopLossPrice invalid for Long (suggested={suggested:F5} >= entry={entryPrice:F5})";
                    return false;
                }
            }
            else
            {
                if (suggested <= entryPrice)
                {
                    reason = $"SuggestedStopLossPrice invalid for Short (suggested={suggested:F5} <= entry={entryPrice:F5})";
                    return false;
                }
            }

            slPrice = suggested;
            reason = "SuggestedStopLossPrice";
            return true;
        }

        public TpSlResult Calculate(decimal entryPrice, TpSlContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.SetupParams == null)
            {
                throw new InvalidOperationException("TpSlContext.SetupParams darf nicht null sein. Bitte SetupParams immer initialisieren.");
            }

            var setup = ctx.SetupParams;
            var debugMessages = new List<string>();
            var tick = ctx.Tick;

            // --- 0. SEPARATE TP/SL-STRATEGIEN BASIEREND AUF SYSTEM-STATUS ---
            debugMessages.Add($"SYSTEM_STATUS: EnableIsBlocked={ctx.EnableIsBlocked}, EnableMicroCompositeSystem={ctx.EnableMicroCompositeSystem}");
            debugMessages.Add($"WEG_FREI: WegFreiLong={ctx.WegFreiLong}, WegFreiShort={ctx.WegFreiShort}");

            // Wenn beide Systeme deaktiviert: Standard-TP/SL verwenden
            if (!ctx.EnableIsBlocked && !ctx.EnableMicroCompositeSystem)
            {
                debugMessages.Add("STRATEGIE: Beide Systeme deaktiviert -> Standard-TP/SL (Ticks-basiert)");
                
                decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                decimal slTicks = setup.SlTicks ?? DefaultSlTicks;
                
                decimal standardTpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);

                decimal standardSlPrice;
                if (TryResolveAbsoluteStopFromSetup(entryPrice, ctx, out var absSl, out var absReason))
                {
                    standardSlPrice = absSl;
                    debugMessages.Add($"SL_SOURCE: {absReason} -> {standardSlPrice:F5}");
                }
                else
                {
                    standardSlPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks,
                        ctx.Direction == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy);
                    debugMessages.Add($"SL_SOURCE: Ticks (Reason: {absReason})");
                }
                
                debugMessages.Add($"TP_TICKS (Standard): {tpTicks} -> {standardTpPrice:F5}");
                debugMessages.Add($"SL_TICKS (Standard): {slTicks} -> {standardSlPrice:F5}");
                
                return new TpSlResult
                {
                    EntryPrice = entryPrice,
                    TakeProfitPrice = standardTpPrice,
                    StopPrice = standardSlPrice,
                    Management = CreateStandardManagementPlan(setup, debugMessages, ctx.Logger)
                };
            }

            // Wenn nur Level-System aktiv: Level-basierte TP/SL bevorzugen
            if (ctx.EnableIsBlocked && !ctx.EnableMicroCompositeSystem)
            {
                debugMessages.Add("STRATEGIE: Nur Level-System aktiv -> Level-basierte TP/SL");
                return CalculateLevelBasedTpSl(entryPrice, ctx, debugMessages);
            }

            // Wenn nur MicroComposite-System aktiv: Weg-Frei-basierte TP/SL
            if (!ctx.EnableIsBlocked && ctx.EnableMicroCompositeSystem)
            {
                debugMessages.Add("STRATEGIE: Nur MicroComposite-System aktiv -> Weg-Frei-basierte TP/SL");
                return CalculateWegFreiBasedTpSl(entryPrice, ctx, debugMessages);
            }

            // Wenn beide Systeme aktiv: Intelligente Kombination
            if (ctx.EnableIsBlocked && ctx.EnableMicroCompositeSystem)
            {
                debugMessages.Add("STRATEGIE: Beide Systeme aktiv -> Intelligente Kombination");
                
                bool wegFreiOk = (ctx.Direction == OrderDirections.Buy && ctx.WegFreiLong) ||
                                (ctx.Direction == OrderDirections.Sell && ctx.WegFreiShort);
                
                if (wegFreiOk)
                {
                    debugMessages.Add("Weg-Frei OK -> Level + MicroComposite kombiniert");
                    return CalculateCombinedTpSl(entryPrice, ctx, debugMessages);
                }
                else
                {
                    debugMessages.Add("Weg-Frei BLOCKIERT -> Nur Level-basierte TP/SL");
                    return CalculateLevelBasedTpSl(entryPrice, ctx, debugMessages);
                }
            }

            // --- 1. TAKE PROFIT (TP) Berechnung ---
            decimal tpPrice;
            var tpType = setup.TpType ?? DefaultTpType;
            debugMessages.Add($"TP_TYPE: {tpType}");

            if (tpType.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                var tpLevelKey = setup.TpLevelKey;
                if (!string.IsNullOrWhiteSpace(tpLevelKey))
                {
                    if (TryFindClosestVwapLevelInDirection(ctx, tpLevelKey, entryPrice, ctx.Direction, out var closestLevelPrice, out var actualLevelKey))
                    {
                        decimal offsetTicks = setup.TpLevelOffsetTicks ?? 0m;
                        if (offsetTicks != 0)
                        {
                            closestLevelPrice += ctx.Direction == OrderDirections.Buy ? offsetTicks * tick : -offsetTicks * tick;
                            debugMessages.Add($"TP_LEVEL_OFFSET_TICKS applied: {offsetTicks} ticks.");
                        }
                        tpPrice = closestLevelPrice;
                        debugMessages.Add($"TP_LEVEL '{tpLevelKey}' resolved to '{actualLevelKey}' and applied: {tpPrice:F5}");
                    }
                    else
                    {
                        decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                        tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                        debugMessages.Add($"WARNING: TP_LEVEL '{tpLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] WARNING: TP_LEVEL '{tpLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                    }
                }
                else
                {
                    decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                    tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                    debugMessages.Add($"WARNING: TP_LEVEL_KEY nicht angegeben für TpType='Level'. Zurückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                    ctx.Logger?.Invoke($"[TpSlCalc] WARNING: TP_LEVEL_KEY nicht angegeben für TpType='Level'. Zurückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                }
            }
            else if (tpType.Equals("Ticks", StringComparison.OrdinalIgnoreCase))
            {
                decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                debugMessages.Add("TP_SOURCE: Ticks");
                debugMessages.Add($"TP_TICKS angewandt: {tpTicks} -> {tpPrice:F5}");
            }
            else if (tpType.Equals("Vorgeschlagen", StringComparison.OrdinalIgnoreCase))
            {
                if (setup.SuggestedTargetPrice > 0m)
                {
                    var suggestedTp = setup.SuggestedTargetPrice;
                    var tpDistance = Math.Abs(suggestedTp - entryPrice) / ctx.Tick;
                    
                    // Prüfe ob Level weiter als 20 Ticks entfernt ist
                    if (tpDistance > 20)
                    {
                        // Fallback auf 12 Ticks wenn Level zu weit entfernt
                        tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, 12, ctx.Direction);
                        debugMessages.Add($"DYNAMIC_TP_LIMITED: {tpDistance:F1} > 20 -> Fallback 12 Ticks -> {tpPrice:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] DYNAMIC_TP_LIMITED: {tpDistance:F1} > 20 -> Fallback 12 Ticks -> {tpPrice:F5}");
                    }
                    else
                    {
                        tpPrice = suggestedTp;
                        debugMessages.Add($"DYNAMIC_TP_APPLIED: {tpDistance:F1} Ticks -> {tpPrice:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] DYNAMIC_TP_APPLIED: {tpDistance:F1} Ticks -> {tpPrice:F5}");
                    }
                }
                else
                {
                    decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                    tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                    debugMessages.Add($"WARNING: SuggestedTargetPrice nicht verfügbar. Rückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                    ctx.Logger?.Invoke($"[TpSlCalc] WARNING: SuggestedTargetPrice nicht verfügbar. Rückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                }
            }
            else
            {
                decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                debugMessages.Add($"WARNING: Unbekannt TP_TYPE '{tpType}'. Zurückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
                ctx.Logger?.Invoke($"[TpSlCalc] WARNING: Unbekannt TP_TYPE '{tpType}'. Zurückfall auf Ticks-basiert TP: {tpTicks} -> {tpPrice:F5}");
            }

            // --- 2. STOP LOSS (SL) Berechnung ---
            decimal slPrice;
            var slType = setup.SlType ?? DefaultSlType;
            debugMessages.Add($"SL_TYPE: {slType}");
            var slDirection = ctx.Direction == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy;
            
            if (slType.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                var slLevelKey = setup.SlLevelKey;
                if (!string.IsNullOrWhiteSpace(slLevelKey))
                {
                    if (TryFindClosestVwapLevelInDirection(ctx, slLevelKey, entryPrice, slDirection, out var closestLevelPrice, out var actualLevelKey))
                    {
                        decimal offsetTicks = setup.SlLevelOffsetTicks ?? 0m;
                        if (offsetTicks != 0)
                        {
                            closestLevelPrice += ctx.Direction == OrderDirections.Buy ? -offsetTicks * tick : offsetTicks * tick;
                            debugMessages.Add($"SL_LEVEL_OFFSET_TICKS angewandt: {offsetTicks} ticks.");
                        }

                        if ((ctx.Direction == OrderDirections.Buy && closestLevelPrice >= entryPrice) ||
                            (ctx.Direction == OrderDirections.Sell && closestLevelPrice <= entryPrice))
                        {
                            closestLevelPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, DefaultSlTicks, slDirection);
                            debugMessages.Add($"WARNING: SL_LEVEL '{slLevelKey}' resolved to '{actualLevelKey}' war auf der falschen Seite des Eintrags. Zurückfallen auf Ticks-basiert SL: {DefaultSlTicks} -> {closestLevelPrice:F5}");
                            ctx.Logger?.Invoke($"[TpSlCalc] WARNING: SL_LEVEL '{slLevelKey}' resolved to '{actualLevelKey}' war auf der falschen Seite des Eintrags. Zurückfallen auf Ticks-basiert SL: {DefaultSlTicks} -> {closestLevelPrice:F5}");
                        }

                        slPrice = closestLevelPrice;
                        debugMessages.Add($"SL_LEVEL '{slLevelKey}' entschlossen, '{actualLevelKey}' und angewendet: {slPrice:F5}");
                    }
                    else
                    {
                        slType = "Ticks";
                        decimal slTicks = setup.SlTicks ?? DefaultSlTicks;
                        slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks, slDirection);
                        debugMessages.Add($"WARNING: SL_LEVEL '{slLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] WARNING: SL_LEVEL '{slLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
                    }
                }
                else
                {
                    slType = "Ticks";
                    decimal slTicks = setup.SlTicks ?? DefaultSlTicks;
                    slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks, slDirection);
                    debugMessages.Add($"WARNING: SL_LEVEL_KEY nicht angegeben für SlType='Level'. Zurückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
                    ctx.Logger?.Invoke($"[TpSlCalc] WARNING: SL_LEVEL_KEY nicht angegeben für SlType='Level'. Zurückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
                }
            }
            else if (slType.Equals("Ticks", StringComparison.OrdinalIgnoreCase))
            {
                decimal slTicks = setup.SlTicks ?? DefaultSlTicks;
                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks, slDirection);
                debugMessages.Add($"SL_TICKS angewandt: {slTicks} -> {slPrice:F5}");
            }
            else if (slType.Equals("ATR", StringComparison.OrdinalIgnoreCase))
            {
                decimal atrMultiple = 2.0m;
                decimal atrSl = atrMultiple * ctx.Tr;
                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, atrSl / tick, slDirection);
                debugMessages.Add($"SL_ATR angewandt: {atrMultiple}xATR={atrSl:F5} -> {slPrice:F5}");
            }
            else if (slType.Equals("VolPerSecondScaled", StringComparison.OrdinalIgnoreCase))
            {
                var baseSlTicks = setup.BaseSlTicksAtAvgVol ?? DefaultBaseSlTicksAtAvgVol;
                var scaleMultiplier = setup.SlVolPerSecondScaleMultiplier ?? DefaultSlVolPerSecondScaleMultiplier;
                var minSlTicks = setup.MinSlTicks ?? DefaultMinSlTicks;
                var maxSlTicks = setup.MaxSlTicks ?? DefaultMaxSlTicks;

                decimal volRatio = ctx.AvgVolPerSecond > 0m ? ctx.CurrentVolPerSecond / ctx.AvgVolPerSecond : 1.0m;
                decimal scaledSlTicks = baseSlTicks * scaleMultiplier * volRatio;

                scaledSlTicks = Math.Max(minSlTicks, Math.Min(maxSlTicks, scaledSlTicks));

                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, scaledSlTicks, slDirection);
                debugMessages.Add($"SL_VOLPERSECOND angewandt: Base={baseSlTicks}, Ratio={volRatio:F2}, Scaled={scaledSlTicks:F2} -> {slPrice:F5}");
            }
            else
            {
                decimal slTicks = setup.SlTicks ?? DefaultSlTicks;
                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks, slDirection);
                debugMessages.Add($"WARNING: Unbekannt SL_TYPE '{slType}'. Zurückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
                ctx.Logger?.Invoke($"[TpSlCalc] WARNING: Unbekannt SL_TYPE '{slType}'. Zurückfall auf Ticks-basiert SL: {slTicks} -> {slPrice:F5}");
            }

            // --- 3. Management Plan erstellen ---
            var plan = BuildManagementPlan(ctx);
            
            foreach (var msg in debugMessages)
            {
                plan.DebugSourceMessages.Add(msg);
                ctx.Logger?.Invoke($"[TpSlCalc] {msg}");
            }

            // --- 4. Ergebnis zurückgeben ---
            return new TpSlResult
            {
                EntryPrice = entryPrice,
                TakeProfitPrice = tpPrice,
                StopPrice = slPrice,
                Management = plan
            };
        }

        private bool TryFindClosestVwapLevelInDirection(TpSlContext ctx, string baseLevelKey, decimal entryPrice, OrderDirections direction, out decimal closestLevelPrice, out string actualLevelKey)
        {
            closestLevelPrice = 0m;
            actualLevelKey = string.Empty;

            var relevantLevels = new List<(string key, decimal price)>();

            if (TpSlHelpers.TryGetLevelPrice(ctx, "VWAP", out var vwapPrice) && vwapPrice > 0m)
            {
                relevantLevels.Add(("VWAP", vwapPrice));
            }

            if (TpSlHelpers.TryGetLevelPrice(ctx, "VWAPBAND1_UPPER", out var vwapBand1Upper) && vwapBand1Upper > 0m)
            {
                relevantLevels.Add(("VWAPBAND1_UPPER", vwapBand1Upper));
            }
            if (TpSlHelpers.TryGetLevelPrice(ctx, "VWAPBAND1_LOWER", out var vwapBand1Lower) && vwapBand1Lower > 0m)
            {
                relevantLevels.Add(("VWAPBAND1_LOWER", vwapBand1Lower));
            }

            if (TpSlHelpers.TryGetLevelPrice(ctx, "VWAPBAND2_UPPER", out var vwapBand2Upper) && vwapBand2Upper > 0m)
            {
                relevantLevels.Add(("VWAPBAND2_UPPER", vwapBand2Upper));
            }
            if (TpSlHelpers.TryGetLevelPrice(ctx, "VWAPBAND2_LOWER", out var vwapBand2Lower) && vwapBand2Lower > 0m)
            {
                relevantLevels.Add(("VWAPBAND2_LOWER", vwapBand2Lower));
            }

            IEnumerable<(string key, decimal price)> filteredLevels;

            if (direction == OrderDirections.Buy)
            {
                filteredLevels = relevantLevels
                    .Where(l => l.price > entryPrice)
                    .OrderBy(l => l.price - entryPrice);
            }
            else
            {
                filteredLevels = relevantLevels
                    .Where(l => l.price < entryPrice)
                    .OrderByDescending(l => entryPrice - l.price);
            }

            if (baseLevelKey.Equals("VWAP", StringComparison.OrdinalIgnoreCase))
            {
                filteredLevels = filteredLevels.Where(l => l.key.Contains("VWAP") && !l.key.Contains("BAND"));
            }
            else if (baseLevelKey.Equals("VWAPBAND1", StringComparison.OrdinalIgnoreCase))
            {
                filteredLevels = filteredLevels.Where(l => l.key.Contains("VWAPBAND1"));
            }
            else if (baseLevelKey.Equals("VWAPBAND2", StringComparison.OrdinalIgnoreCase))
            {
                filteredLevels = filteredLevels.Where(l => l.key.Contains("VWAPBAND2"));
            }

            var foundLevel = filteredLevels.FirstOrDefault();

            if (foundLevel.key != null)
            {
                closestLevelPrice = foundLevel.price;
                actualLevelKey = foundLevel.key;
                return true;
            }

            return false;
        }

        private static ManagementPlan BuildManagementPlan(TpSlContext ctx)
        {
            var plan = new ManagementPlan();
            var setup = ctx.SetupParams;

            if (setup.BeStage1Trigger.HasValue)
            {
                plan.BreakEvenStages.Add(new BreakEvenStage
                {
                    TriggerTicks = setup.BeStage1Trigger.Value,
                    OffsetTicks = setup.BeStage1Offset ?? DefaultBeOffset
                });
                TpSlHelpers.AddDebugSource(plan, $"BE Stage1 from SetupParams: trigger={setup.BeStage1Trigger} ticks, offset={setup.BeStage1Offset} ticks");
            }
            if (setup.BeStage2Trigger.HasValue)
            {
                plan.BreakEvenStages.Add(new BreakEvenStage
                {
                    TriggerTicks = setup.BeStage2Trigger.Value,
                    OffsetTicks = setup.BeStage2Offset ?? DefaultBeOffset
                });
                TpSlHelpers.AddDebugSource(plan, $"BE Stage2 from SetupParams: trigger={setup.BeStage2Trigger} ticks, offset={setup.BeStage2Offset} ticks");
            }

            if (!string.IsNullOrWhiteSpace(setup.BreakEvenLevelsTrendConfig))
            {
                var stages = ParseBreakEvenConfigString(setup.BreakEvenLevelsTrendConfig);
                foreach (var stage in stages)
                {
                    plan.BreakEvenStages.Add(stage);
                }
                TpSlHelpers.AddDebugSource(plan, $"BreakEven stages from config: {setup.BreakEvenLevelsTrendConfig} -> {stages.Count} stages");
            }

            var trailing = new TrailingConfiguration();
            if (!string.IsNullOrEmpty(setup.TrailType))
            {
                trailing.TrailType = setup.TrailType.ToUpper() switch
                {
                    "CANDLE_HL" or "CANDLELOWHIGH" or "CANDLE" => TrailingType.CandleLowHigh,
                    "ATR" => TrailingType.ATR,
                    "FIXED_TICKS" or "FIXED" => TrailingType.FixedTicks,
                    "NONE" or "" => TrailingType.None,
                    _ => TrailingType.None
                };
                if (trailing.TrailType == TrailingType.None && !string.IsNullOrEmpty(setup.TrailType))
                {
                    TpSlHelpers.AddDebugSource(plan, $"WARNING: Unknown TrailType '{setup.TrailType}', defaulted to None.");
                }
            }
            else
            {
                trailing.TrailType = TrailingType.None;
            }
            trailing.TrailOffsetTicks = setup.TrailOffset ?? DefaultTrailingOffset;
            trailing.TrailActivateAfterTicks = setup.TrailActivateAfterTicks ?? DefaultTrailingActivateAfterTicks;

            plan.Trailing = trailing;
            TpSlHelpers.AddDebugSource(plan, $"Trailing from SetupParams: type={trailing.TrailType}, offsetTicks={trailing.TrailOffsetTicks}, activateAfter={trailing.TrailActivateAfterTicks}");

            if (!string.IsNullOrWhiteSpace(setup.EarlyExitLevels))
            {
                var parts = setup.EarlyExitLevels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in parts)
                {
                    var key = raw.Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    var rule = new EarlyExitRule
                    {
                        LevelKey = key,
                        MaxDistanceTicks = setup.EarlyExitMaxDistTicks ?? DefaultMaxEarlyExitDistTicks,
                        CloseAtLevel = setup.EarlyCloseAtLevel ?? false
                    };
                    plan.EarlyExitRules.Add(rule);
                    TpSlHelpers.AddDebugSource(plan, $"EarlyExitRule added: {key}, closeAtLevel={rule.CloseAtLevel}, maxDistTicks={rule.MaxDistanceTicks}");
                }
            }

            return plan;
        }

        private static List<BreakEvenStage> ParseBreakEvenConfigString(string config)
        {
            var stages = new List<BreakEvenStage>();
            if (string.IsNullOrWhiteSpace(config)) return stages;

            var pairs = config.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), out var trigger) &&
                    decimal.TryParse(parts[1].Trim(), out var offset))
                {
                    stages.Add(new BreakEvenStage
                    {
                        TriggerTicks = trigger,
                        OffsetTicks = offset
                    });
                }
            }
            return stages;
        }

        private decimal ResolveTpPriceFromSetup(decimal entryPrice, TpSlContext ctx, List<string> debugMessages)
        {
            var setup = ctx.SetupParams;
            var tick = ctx.Tick;
            var tpType = setup.TpType ?? DefaultTpType;
            debugMessages.Add($"TP_TYPE: {tpType}");

            if (tpType.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                var tpLevelKey = setup.TpLevelKey;
                if (!string.IsNullOrWhiteSpace(tpLevelKey))
                {
                    if (TryFindClosestVwapLevelInDirection(ctx, tpLevelKey, entryPrice, ctx.Direction, out var closestLevelPrice, out var actualLevelKey))
                    {
                        decimal offsetTicks = setup.TpLevelOffsetTicks ?? 0m;
                        if (offsetTicks != 0)
                        {
                            closestLevelPrice += ctx.Direction == OrderDirections.Buy ? offsetTicks * tick : -offsetTicks * tick;
                            debugMessages.Add($"TP_LEVEL_OFFSET_TICKS applied: {offsetTicks} ticks.");
                        }
                        debugMessages.Add($"TP_SOURCE: Level({tpLevelKey}->{actualLevelKey})");
                        debugMessages.Add($"TP_LEVEL '{tpLevelKey}' resolved to '{actualLevelKey}' and applied: {closestLevelPrice:F5}");
                        return closestLevelPrice;
                    }

                    decimal fallbackTicks = setup.TpTicks ?? DefaultTpTicks;
                    var fallback = TpSlHelpers.PriceFromTicks(ctx, entryPrice, fallbackTicks, ctx.Direction);
                    debugMessages.Add($"TP_SOURCE: LevelFallback(Ticks)");
                    debugMessages.Add($"WARNING: TP_LEVEL '{tpLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert TP: {fallbackTicks} -> {fallback:F5}");
                    ctx.Logger?.Invoke($"[TpSlCalc] WARNING: TP_LEVEL '{tpLevelKey}' nicht gefunden oder ungültig. Rückfall auf Ticks-basiert TP: {fallbackTicks} -> {fallback:F5}");
                    return fallback;
                }

                decimal defaultTicks = setup.TpTicks ?? DefaultTpTicks;
                var defaultTp = TpSlHelpers.PriceFromTicks(ctx, entryPrice, defaultTicks, ctx.Direction);
                debugMessages.Add($"TP_SOURCE: LevelFallback(Ticks)");
                debugMessages.Add($"WARNING: TP_LEVEL_KEY nicht angegeben für TpType='Level'. Zurückfall auf Ticks-basiert TP: {defaultTicks} -> {defaultTp:F5}");
                ctx.Logger?.Invoke($"[TpSlCalc] WARNING: TP_LEVEL_KEY nicht angegeben für TpType='Level'. Zurückfall auf Ticks-basiert TP: {defaultTicks} -> {defaultTp:F5}");
                return defaultTp;
            }

            if (tpType.Equals("Ticks", StringComparison.OrdinalIgnoreCase))
            {
                decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                var tpPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                debugMessages.Add($"TP_TICKS angewandt: {tpTicks} -> {tpPrice:F5}");
                return tpPrice;
            }

            if (tpType.Equals("Vorgeschlagen", StringComparison.OrdinalIgnoreCase))
            {
                if (setup.SuggestedTargetPrice > 0m)
                {
                    var suggestedTp = setup.SuggestedTargetPrice;
                    var tpDistance = Math.Abs(suggestedTp - entryPrice) / ctx.Tick;
                    var minDist = setup.MinDynamicTpDistanceTicks ?? 0m;
                    var maxDist = setup.MaxDynamicTpDistanceTicks ?? 20m;

                    if (minDist > 0m && tpDistance < minDist)
                    {
                        var adjusted = TpSlHelpers.PriceFromTicks(ctx, entryPrice, minDist, ctx.Direction);
                        debugMessages.Add("TP_SOURCE: VorgeschlagenAdjusted(MinDist)");
                        debugMessages.Add($"DYNAMIC_TP_TOO_CLOSE: {tpDistance:F1} < {minDist} -> Adjust to {minDist} Ticks -> {adjusted:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] DYNAMIC_TP_TOO_CLOSE: {tpDistance:F1} < {minDist} -> Adjust to {minDist} Ticks -> {adjusted:F5}");
                        return adjusted;
                    }

                    if (tpDistance > maxDist)
                    {
                        var fallback = TpSlHelpers.PriceFromTicks(ctx, entryPrice, DefaultTpTicks, ctx.Direction);
                        debugMessages.Add("TP_SOURCE: VorgeschlagenFallback(Ticks)");
                        debugMessages.Add($"DYNAMIC_TP_LIMITED: {tpDistance:F1} > {maxDist} -> Fallback {DefaultTpTicks} Ticks -> {fallback:F5}");
                        ctx.Logger?.Invoke($"[TpSlCalc] DYNAMIC_TP_LIMITED: {tpDistance:F1} > {maxDist} -> Fallback {DefaultTpTicks} Ticks -> {fallback:F5}");
                        return fallback;
                    }

                    debugMessages.Add("TP_SOURCE: Vorgeschlagen");
                    debugMessages.Add($"DYNAMIC_TP_APPLIED: {tpDistance:F1} Ticks -> {suggestedTp:F5}");
                    ctx.Logger?.Invoke($"[TpSlCalc] DYNAMIC_TP_APPLIED: {tpDistance:F1} Ticks -> {suggestedTp:F5}");
                    return suggestedTp;
                }

                decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
                var fallbackTp = TpSlHelpers.PriceFromTicks(ctx, entryPrice, tpTicks, ctx.Direction);
                debugMessages.Add("TP_SOURCE: VorgeschlagenFallback(Ticks)");
                debugMessages.Add($"WARNING: SuggestedTargetPrice nicht verfügbar. Rückfall auf Ticks-basiert TP: {tpTicks} -> {fallbackTp:F5}");
                ctx.Logger?.Invoke($"[TpSlCalc] WARNING: SuggestedTargetPrice nicht verfügbar. Rückfall auf Ticks-basiert TP: {tpTicks} -> {fallbackTp:F5}");
                return fallbackTp;
            }

            var unknownTicks = setup.TpTicks ?? DefaultTpTicks;
            var unknownTp = TpSlHelpers.PriceFromTicks(ctx, entryPrice, unknownTicks, ctx.Direction);
            debugMessages.Add("TP_SOURCE: UnknownTypeFallback(Ticks)");
            debugMessages.Add($"WARNING: Unbekannt TP_TYPE '{tpType}'. Zurückfall auf Ticks-basiert TP: {unknownTicks} -> {unknownTp:F5}");
            ctx.Logger?.Invoke($"[TpSlCalc] WARNING: Unbekannt TP_TYPE '{tpType}'. Zurückfall auf Ticks-basiert TP: {unknownTicks} -> {unknownTp:F5}");
            return unknownTp;
        }

        private TpSlResult CalculateLevelBasedTpSl(decimal entryPrice, TpSlContext ctx, List<string> debugMessages)
        {
            var setup = ctx.SetupParams;
            var tick = ctx.Tick;
            
            decimal tpPrice = ResolveTpPriceFromSetup(entryPrice, ctx, debugMessages);

            decimal slPrice;
            if (TryResolveAbsoluteStopFromSetup(entryPrice, ctx, out var absSl, out var absReason))
            {
                slPrice = absSl;
                debugMessages.Add($"SL_SOURCE: {absReason} -> {slPrice:F5}");
            }
            else
            {
                slPrice = entryPrice + (ctx.Direction == OrderDirections.Buy ? 
                    -(setup.SlTicks ?? DefaultSlTicks) * tick : (setup.SlTicks ?? DefaultSlTicks) * tick);
                debugMessages.Add($"SL_SOURCE: Ticks (Reason: {absReason})");
            }
            
            debugMessages.Add($"TP_LEVEL_OR_SETUP: TP={tpPrice:F5}");
            debugMessages.Add($"SL_SETUP: SL={slPrice:F5}");

            return new TpSlResult
            {
                EntryPrice = entryPrice,
                TakeProfitPrice = tpPrice,
                StopPrice = slPrice,
                Management = CreateStandardManagementPlan(setup, debugMessages, ctx.Logger)
            };
        }

        private TpSlResult CalculateWegFreiBasedTpSl(decimal entryPrice, TpSlContext ctx, List<string> debugMessages)
        {
            var setup = ctx.SetupParams;
            var tick = ctx.Tick;
            
            decimal tpPrice = ResolveTpPriceFromSetup(entryPrice, ctx, debugMessages);
            decimal tpTicks = (setup.TpTicks ?? DefaultTpTicks) * 0.8m;
            decimal slTicks = (setup.SlTicks ?? DefaultSlTicks) * 1.2m;

            decimal slPrice;
            if (TryResolveAbsoluteStopFromSetup(entryPrice, ctx, out var absSl, out var absReason))
            {
                slPrice = absSl;
                debugMessages.Add($"SL_SOURCE: {absReason} -> {slPrice:F5}");
            }
            else
            {
                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks,
                    ctx.Direction == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy);
                debugMessages.Add($"SL_SOURCE: Ticks (Reason: {absReason})");
            }
            
            debugMessages.Add($"TP_WEG_FREI_BASED (konservativ): {tpTicks} -> {tpPrice:F5}");
            debugMessages.Add($"SL_WEG_FREI_BASED (erweitert): {slTicks} -> {slPrice:F5}");
            
            return new TpSlResult
            {
                EntryPrice = entryPrice,
                TakeProfitPrice = tpPrice,
                StopPrice = slPrice,
                Management = CreateStandardManagementPlan(setup, debugMessages, ctx.Logger)
            };
        }

        private TpSlResult CalculateCombinedTpSl(decimal entryPrice, TpSlContext ctx, List<string> debugMessages)
        {
            var setup = ctx.SetupParams;
            var tick = ctx.Tick;
            
            decimal tpPrice = ResolveTpPriceFromSetup(entryPrice, ctx, debugMessages);
            decimal tpTicks = setup.TpTicks ?? DefaultTpTicks;
            decimal slTicks = (setup.SlTicks ?? DefaultSlTicks) * 0.9m;

            decimal slPrice;
            if (TryResolveAbsoluteStopFromSetup(entryPrice, ctx, out var absSl, out var absReason))
            {
                slPrice = absSl;
                debugMessages.Add($"SL_SOURCE: {absReason} -> {slPrice:F5}");
            }
            else
            {
                slPrice = TpSlHelpers.PriceFromTicks(ctx, entryPrice, slTicks,
                    ctx.Direction == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy);
                debugMessages.Add($"SL_SOURCE: Ticks (Reason: {absReason})");
            }
            
            debugMessages.Add($"TP_COMBINED (Level): {tpTicks} -> {tpPrice:F5}");
            debugMessages.Add($"SL_COMBINED (Weg-Frei optimiert): {slTicks} -> {slPrice:F5}");
            
            return new TpSlResult
            {
                EntryPrice = entryPrice,
                TakeProfitPrice = tpPrice,
                StopPrice = slPrice,
                Management = CreateStandardManagementPlan(setup, debugMessages, ctx.Logger)
            };
        }

        private ManagementPlan CreateStandardManagementPlan(SetupParams setup, List<string> debugMessages, Action<string> logger)
        {
            var plan = new ManagementPlan();
            
            if (setup.BeStage1Trigger.HasValue && setup.BeStage1Offset.HasValue)
            {
                plan.BreakEvenStages.Add(new BreakEvenStage
                {
                    TriggerTicks = setup.BeStage1Trigger.Value,
                    OffsetTicks = setup.BeStage1Offset.Value
                });
                debugMessages.Add($"BE_STAGE1: Trigger={setup.BeStage1Trigger}, Offset={setup.BeStage1Offset}");
            }
            
            if (!string.IsNullOrWhiteSpace(setup.TrailType) && setup.TrailType != "NONE")
            {
                plan.Trailing.TrailType = setup.TrailType switch
                {
                    "CANDLE_HL" => TrailingType.CandleLowHigh,
                    "FIXED_TICKS" => TrailingType.FixedTicks,
                    "ATR" => TrailingType.ATR,
                    _ => TrailingType.None
                };
                plan.Trailing.TrailOffsetTicks = setup.TrailOffset ?? 0;
                plan.Trailing.TrailActivateAfterTicks = setup.TrailActivateAfterTicks ?? 0;
                debugMessages.Add($"TRAILING: Type={setup.TrailType}, Offset={plan.Trailing.TrailOffsetTicks}");
            }

            // NEU: BreakEvenLevelsTrendConfig auswerten
            if (!string.IsNullOrWhiteSpace(setup.BreakEvenLevelsTrendConfig))
            {
                var stages = ParseBreakEvenConfigString(setup.BreakEvenLevelsTrendConfig);
                foreach (var stage in stages)
                {
                    plan.BreakEvenStages.Add(stage);
                }
                debugMessages.Add($"BREAK_EVEN_CONFIG: {setup.BreakEvenLevelsTrendConfig} -> {stages.Count} stages");
            }
            
            if (!string.IsNullOrWhiteSpace(setup.EarlyExitLevels))
            {
                var levels = setup.EarlyExitLevels.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var level in levels)
                {
                    plan.EarlyExitRules.Add(new EarlyExitRule
                    {
                        LevelKey = level.Trim(),
                        CloseAtLevel = setup.EarlyCloseAtLevel ?? false,
                        MaxDistanceTicks = setup.EarlyExitMaxDistTicks ?? decimal.MaxValue
                    });
                }
                debugMessages.Add($"EARLY_EXIT: Levels={setup.EarlyExitLevels}");
            }
            
            foreach (var msg in debugMessages)
            {
                plan.DebugSourceMessages.Add(msg);
                logger?.Invoke($"[TpSlCalc] {msg}");
            }
            
            return plan;
        }

        public (bool shouldExit, string reason, decimal? closePrice) ShouldEarlyExit(TpSlContext ctx)
        {
            if (ctx == null || ctx.SetupParams == null ||
                string.IsNullOrWhiteSpace(ctx.SetupParams.EarlyExitLevels))
                return (false, "", null);

            var currentPrice = ctx.CurrentPrice;
            if (currentPrice <= 0m) return (false, "", null);

            var tick = ctx.Tick;
            var proxTicks = ctx.SetupParams.ProximityTicksForExit ?? 2m;
            var proxPx = proxTicks * tick;
            var closeAtLevel = ctx.SetupParams.EarlyCloseAtLevel ?? false;

            var parts = ctx.SetupParams.EarlyExitLevels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                var levelKey = raw.Trim().ToUpper();
                if (string.IsNullOrEmpty(levelKey)) continue;

                if (levelKey == "VWAPBAND1")
                {
                    levelKey = ctx.Direction == OrderDirections.Buy ? "VWAPBAND1_UPPER" : "VWAPBAND1_LOWER";
                }

                if (TpSlHelpers.TryGetLevelPrice(ctx, levelKey, out var levelPrice) && levelPrice > 0m)
                {
                    var distance = Math.Abs(currentPrice - levelPrice);
                    if (distance <= proxPx)
                    {
                        var reason = $"EarlyExit at {raw.Trim()} (Dist: {distance:F5}, Prox: {proxPx:F5})";
                        ctx.Logger?.Invoke($"[EarlyExit] {reason} (Current: {currentPrice:F5}, Level: {levelPrice:F5}).");

                        var closePrice = closeAtLevel ? levelPrice : (decimal?)null;
                        return (true, reason, closePrice);
                    }
                }
                else
                {
                    ctx.Logger?.Invoke($"[EarlyExit] Konnte Level '{levelKey}' nicht fetchen.");
                }
            }

            return (false, "", null);
        }
    }
}
