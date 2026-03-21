using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public interface ICandleProvider
    {
        // Liefert eine konkrete Kerze (ATAS IndicatorCandle)
        IndicatorCandle GetCandle(int index);
        int DataSeriesCount { get; }
    }

    public interface ISessionProvider
    {
        // Muss die Methode IsNewSession(int barIndex) Ihrer Basisklasse abbilden
        bool IsNewSession(int barIndex);
    }

    public interface IInstrumentInfoProvider
    {
        object InstrumentInfo { get; }
    }

    public interface ITickConverter
    {
        // Muss die Methoden zur Tick-Konvertierung Ihrer Basisklasse abbilden
        int ToTickIndex(decimal price);
        decimal FromTickIndex(int tickIndex);
        decimal RoundToTick(decimal price);
    }
}


