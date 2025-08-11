#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript;
using System.Diagnostics.Contracts;
using System.Collections.Generic;
#endregion


namespace NinjaTrader.Custom.CustomClasses
{
    public class PatternFilter
    {

        private CandlestickPattern candlestickPattern;

        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia
        public PatternFilter(NinjaTrader.NinjaScript.Strategies.TestOriginale strategy)
        {
            this.strategyTestOriginale = strategy;
        }


        //looking for patterns in previous and current bars
        //cerco i pattern da evitare, solo per singole barre
        public bool AvoidPatterns(List<ChartPattern> patternsToAvoid, List<int> barsAgo, int trendStrenght)
        {

            if(patternsToAvoid.Count != 0)
            {
                foreach (var pattern in patternsToAvoid)
                {

                    foreach(var bar in barsAgo)
                    {
                        if (strategyTestOriginale.CandlestickPattern(pattern, trendStrenght)[bar] == 1)
                        {
                            strategyTestOriginale.LogInfo($"Pattern da evitare rilevato: {pattern} sulla barra {bar}");
                            return true;
                        }
                    }
                    
                }
            }
            

            return false;
        }

    }

}
