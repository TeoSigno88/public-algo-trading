using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;




namespace NinjaTrader.Custom.CustomClasses
{
    internal class CustomTralingStop
    {
        
        public CustomTralingStop(NinjaTrader.NinjaScript.Strategies.TestOriginale strategy)
        {
            this.strategyTestOriginale = strategy;
        }
        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia
        private int plotExitPosition;
        public void CustomTrailingStop()
        {
            if (strategyTestOriginale.TotalRiskClosed && strategyTestOriginale.Position.MarketPosition == MarketPosition.Long)
            {
                //se la chiusura attuale è maggiore del prezzo d'ingresso più il 4% allora esce
                if (strategyTestOriginale.Low[0] > (strategyTestOriginale.enterPrice * 1.04))
                {
                    strategyTestOriginale.customStop = strategyTestOriginale.Low[1] - 1;
                    if (strategyTestOriginale.stopOrder != null)
                    {
                        strategyTestOriginale.CancelOrder(strategyTestOriginale.stopOrder);
                    }
                    strategyTestOriginale.stopOrder = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, strategyTestOriginale.OperationQuantity, 0, strategyTestOriginale.customStop, "", "Exit by CustomTralingStop");

                    Draw.Line(strategyTestOriginale, $"SubsequentStopLoss_{plotExitPosition}", false, 0, strategyTestOriginale.Low[0], 1, strategyTestOriginale.Low[0], Brushes.Yellow, DashStyleHelper.Solid, 2);
                    plotExitPosition++;
                }

            }

        }
    }
}
