using System.Reflection;
using System.Runtime.Serialization;
using MyNamespace.Strategies;
using Xunit;

namespace Scalping_Strategie.Tests;

public class ImbalanceScoreTests
{
    private static object CreateUninitializedGoldfluss()
    {
        var type = typeof(Geldfluss3_3);
#pragma warning disable SYSLIB0050
        return FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050
    }

    private static object CreateStackedImbParams(int maxDepthTicksAnchored)
    {
        var type = typeof(Geldfluss3_3).GetNestedType("StackedImbParams", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        type!.GetField("MaxDepthTicksAnchored")?.SetValue(instance, maxDepthTicksAnchored);
        return instance!;
    }

    private static object CreateResult(
        int totalPairs,
        int buyCountMax,
        int sellCountMax,
        int buyTopAnchored,
        int sellBottomAnchored,
        decimal avgBuyVol,
        decimal avgSellVol,
        decimal baseVolMedian)
    {
        var type = typeof(Geldfluss3_3).GetNestedType("StackedImbalanceResult", BindingFlags.Public);
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        type!.GetField("TotalPairs")?.SetValue(instance, totalPairs);
        type!.GetField("BuyCountMax")?.SetValue(instance, buyCountMax);
        type!.GetField("SellCountMax")?.SetValue(instance, sellCountMax);
        type!.GetField("BuyCountTopAnchored")?.SetValue(instance, buyTopAnchored);
        type!.GetField("SellCountBottomAnchored")?.SetValue(instance, sellBottomAnchored);
        type!.GetField("AvgBuyImbVolQualified")?.SetValue(instance, avgBuyVol);
        type!.GetField("AvgSellImbVolQualified")?.SetValue(instance, avgSellVol);
        type!.GetField("BuyBaseVolMedian")?.SetValue(instance, baseVolMedian);
        return instance!;
    }

    private static (decimal score, string label) ComputeScore(object instance, object result, object param)
    {
        var method = typeof(Geldfluss3_3).GetMethod("ComputeImbalanceScore", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] args = { result, param, null };
        var score = (decimal)method!.Invoke(instance, args)!;
        var label = (string)args[2]!;
        return (score, label);
    }

    [Fact]
    public void Score_IsZero_WhenInsufficientLevels()
    {
        var instance = CreateUninitializedGoldfluss();
        var result = CreateResult(1, 0, 0, 0, 0, 0m, 0m, 0m);
        var param = CreateStackedImbParams(3);

        var (score, label) = ComputeScore(instance, result, param);

        Assert.Equal(0m, score);
        Assert.Equal("insufficient", label);
    }

    [Fact]
    public void Score_IsStrongBuy_ForDominantBuyStack()
    {
        var instance = CreateUninitializedGoldfluss();
        var result = CreateResult(totalPairs: 10, buyCountMax: 8, sellCountMax: 0, buyTopAnchored: 3, sellBottomAnchored: 0,
            avgBuyVol: 200m, avgSellVol: 0m, baseVolMedian: 100m);
        var param = CreateStackedImbParams(3);

        var (score, label) = ComputeScore(instance, result, param);

        Assert.True(score > 0.5m);
        Assert.Equal("strong_buy", label);
    }

    [Fact]
    public void Score_IsStrongSell_ForDominantSellStack()
    {
        var instance = CreateUninitializedGoldfluss();
        var result = CreateResult(totalPairs: 10, buyCountMax: 0, sellCountMax: 8, buyTopAnchored: 0, sellBottomAnchored: 3,
            avgBuyVol: 0m, avgSellVol: 200m, baseVolMedian: 100m);
        var param = CreateStackedImbParams(3);

        var (score, label) = ComputeScore(instance, result, param);

        Assert.True(score < -0.5m);
        Assert.Equal("strong_sell", label);
    }
}
