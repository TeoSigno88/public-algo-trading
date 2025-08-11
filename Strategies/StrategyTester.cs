#region Using declarations
using System;
using System.Data.SqlClient;
using NinjaTrader.Cbi;
using NinjaTrader.Custom.CustomClasses;
using NinjaTrader.NinjaScript.Indicators;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using System.Linq;
using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
using NinjaTrader.NinjaScript;
using System.Net.Http;
using NinjaTrader.NinjaScript.SuperDomColumns;
using System.Security.Cryptography;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript.Indicators;
#endregion


namespace NinjaTrader.NinjaScript.Strategies
{
    public class StrategyTester : Strategy
    {
        
        #region Trade Display
        [NinjaScriptProperty]
        [Display(Name = "Pre-Market", GroupName = "Parameters", Order = 0)]
        public TimeSpan PreMMarket { get; set; } = new TimeSpan(4, 0, 0);

        [NinjaScriptProperty]
        [Display(Name = "Market Open", GroupName = "Parameters", Order = 0)]
        public TimeSpan MarketOpen { get; set; } = new TimeSpan(9, 30, 0);

        [NinjaScriptProperty]
        [Display(Name = "Market Close", GroupName = "Parameters", Order = 0)]
        public TimeSpan MarketClose { get; set; } = new TimeSpan(16, 00, 0);

        [NinjaScriptProperty]
        [Display(Name = "After-Hour End", GroupName = "Parameters", Order = 0)]
        public TimeSpan AfterHourEnd { get; set; } = new TimeSpan(18, 00, 0);

        [NinjaScriptProperty]
        [Display(Name = "Overnight Start", GroupName = "Parameters", Order = 4)]
        public TimeSpan OvernightStart { get; set; } = new TimeSpan(18, 0, 0);

        [NinjaScriptProperty]
        [Display(Name = "Overnight End", GroupName = "Parameters", Order = 5)]
        public TimeSpan OvernightEnd { get; set; } = new TimeSpan(4, 0, 0);

        [NinjaScriptProperty]
        [Display(Name = "Correlation Analysis Interval (bars)", GroupName = "Parameters", Order = 6)]
        public int CorrelationAnalysisInterval { get; set; } = 100;

        [NinjaScriptProperty]
        [Display(Name = "Enable Correlation Analysis", GroupName = "Parameters", Order = 7)]
        public bool EnableCorrelationAnalysis { get; set; } = true;
        #endregion

        private SMA volumeSMA;

        private RecoverData recoverData;
        private int lastCorrelationAnalysisBar = -1;



        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "StrategyTester";
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.Configure)
            {
                recoverData = new RecoverData(this);
            }
            else if (State == State.DataLoaded)
            {
                volumeSMA = SMA(Volumes[0], 4);

                if (recoverData == null)
                {
                    LogError("recoverData è NULL al bar " + CurrentBar);
                    return;
                }

            }

        }



        public class Candle
        {
            public DateTime Time { get; set; }
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public double Close { get; set; }
            public double Volume { get; set; }

            public Candle(DateTime time, double open, double high, double low, double close, double volume)
            {
                Time = time;
                Open = open;
                High = high;
                Low = low;
                Close = close;
                Volume = volume;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5 || recoverData == null || volumeSMA == null)
                return;

            Candle candleList = new Candle(Time[1], Open[1], High[1], Low[1], Close[1], Volume[1]);

            //double? currentLowerShadow = null;
            //if (Open[0] < Close[0])
            //{
            //    currentLowerShadow = Open[0] - Low[0];
            //}
            //utilizzare DownloadCandleInfo per le candele[1] precedenti solo per la candela[0] riferita al target è rialzista

            double actualDirection = (Close[0] - Open[0]) > 1 ? 1 : 0;
            recoverData.DownloadCandleInfo(candleList, volumeSMA);
            
            if (EnableCorrelationAnalysis && ShouldRunCorrelationAnalysis())
            {
                RunCorrelationAnalysis();
            }
            
            LogInfo($"DownloadCandleInfo chiamata");
        }


        public void LogInfo(string message)
        {
            Log(message, LogLevel.Information);
        }

        public void LogError(string message)
        {
            Log(message, LogLevel.Error);
        }

        private bool ShouldRunCorrelationAnalysis()
        {
            return CurrentBar - lastCorrelationAnalysisBar >= CorrelationAnalysisInterval;
        }

        private void RunCorrelationAnalysis()
        {
            try
            {
                LogInfo("=== Avvio Analisi Correlazioni Candele ===");
                
                var correlations = recoverData.AnalyzeCandleCorrelations();
                
                if (correlations.Count > 0)
                {
                    LogInfo($"Trovate {correlations.Count} correlazioni significative (>0.3):");
                    
                    foreach (var correlation in correlations)
                    {
                        string strength = Math.Abs(correlation.Value) > 0.7 ? "FORTE" :
                                        Math.Abs(correlation.Value) > 0.5 ? "MEDIA" : "DEBOLE";
                        
                        LogInfo($"{correlation.Key}: {correlation.Value:F3} ({strength})");
                    }
                }
                else
                {
                    LogInfo("Nessuna correlazione significativa trovata");
                }
                
                lastCorrelationAnalysisBar = CurrentBar;
                LogInfo("=== Fine Analisi Correlazioni ===");
            }
            catch (Exception ex)
            {
                LogError($"Errore durante analisi correlazioni: {ex.Message}");
            }
        }

        public void RunManualCorrelationAnalysis()
        {
            RunCorrelationAnalysis();
        }

    }
}





//indicatore LLHL
//private Dictionary<string, int> lastSignalBars = new Dictionary<string, int>();
//private void SetBarsSinceMarker(string key, int bar)
//{
//    lastSignalBars[key] = bar;
//}

//private int BarsSinceMarker(string key)
//{
//    return lastSignalBars.ContainsKey(key) ? CurrentBar - lastSignalBars[key] : int.MaxValue;
//}

//protected override void OnStateChange()
//{
//    if (State == State.SetDefaults)
//    {
//        Name = "StrategyTester";
//        Description = "Rileva HH→LH e LL→HL con buona precisione";
//        Calculate = Calculate.OnBarClose;
//        IsOverlay = true;
//        BarsRequiredToTrade = 5;
//    }
//    else if (State == State.Configure)
//    {
//        AddChartIndicator(SMA(Close, 1)); // Forza disegno su grafico
//    }
//}

//protected override void OnBarUpdate()
//{
//    if (CurrentBar < 4)
//        return;

//    double atr = ATR(5)[1];
//    double minStrength = atr * 0.25;
//    int barsSinceLastSignal = 10;

//    ---HH → LH(TOP)-- -
//   bool isTop = High[2] > High[3] && High[2] > High[1];
//    bool pullbackValid = Close[3] < High[2] && Close[1] < High[2];
//    bool strongTop = (High[2] - Math.Max(High[3], High[1])) > minStrength;
//    bool notCloseToTop = Math.Abs(Close[3] - High[2]) > minStrength / 2 && Math.Abs(Close[1] - High[2]) > minStrength / 2;

//    if (isTop && pullbackValid && strongTop && notCloseToTop && BarsSinceMarker("hh") > barsSinceLastSignal)
//    {
//        Draw.ArrowDown(this, "hh_" + CurrentBar, false, 2, High[2] + TickSize * 5, Brushes.Red);
//        SetBarsSinceMarker("hh", CurrentBar - 2);
//        Log("🔺 HH→LH valido", LogLevel.Information);
//    }

//    ---LL → HL(BOTTOM)-- -
//   bool isBottom = Low[2] < Low[3] && Low[2] < Low[1];
//    bool pullupValid = High[3] > Low[2] && High[1] > Low[2];
//    bool strongBottom = (Math.Min(Low[3], Low[1]) - Low[2]) > minStrength;
//    bool notCloseToBottom = Math.Abs(High[3] - Low[2]) > minStrength / 2 && Math.Abs(High[1] - Low[2]) > minStrength / 2;

//    if (isBottom && pullupValid && strongBottom && notCloseToBottom && BarsSinceMarker("ll") > barsSinceLastSignal)
//    {
//        Draw.ArrowUp(this, "ll_" + CurrentBar, false, 2, Low[2] - TickSize * 5, Brushes.Green);
//        SetBarsSinceMarker("ll", CurrentBar - 2);
//        Log("🔻 LL→HL valido", LogLevel.Information);
//    }
//}
