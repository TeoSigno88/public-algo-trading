using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NinjaTrader.Custom.CustomClasses
{
    internal class PositionManagment
    {
        public PositionManagment(NinjaTrader.NinjaScript.Strategies.TestOriginale strategy)
        {
            this.strategyTestOriginale = strategy;
        }
        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia
        private int plotExitPosition;

        int plotPoint1;
        int plotPoint2;


        public void PositionManager()
        {

            if (strategyTestOriginale.TotalRiskClosed && strategyTestOriginale.Position.MarketPosition == MarketPosition.Long)
            {
                FindPointOne();


                if (strategyTestOriginale.isPointOne)
                {
                    FindPointTwo();
                }
            }
                
            
        }

        public bool FindPointOne()
        {
            if (strategyTestOriginale.High[2] < strategyTestOriginale.High[1] && strategyTestOriginale.High[1] > strategyTestOriginale.High[0])
            {
                strategyTestOriginale.isPointOne = true;

                Draw.Text(strategyTestOriginale, $"Point_1_{plotPoint1}", "Point 1", 1, (strategyTestOriginale.High[1] + 5), Brushes.Yellow);
                plotPoint1++;

                FindPointTwo();
                return true;
            }

            return false;
        }

        public bool FindPointTwo()
        {
            if (strategyTestOriginale.Position.MarketPosition == MarketPosition.Long)
            {
                strategyTestOriginale.isPointTwo = (strategyTestOriginale.Low[2] > strategyTestOriginale.Low[1] && strategyTestOriginale.Low[1] <= strategyTestOriginale.Low[0] && strategyTestOriginale.High[1] <= strategyTestOriginale.High[0]);

                if (strategyTestOriginale.TotalRiskClosed && strategyTestOriginale.isPointOne && strategyTestOriginale.isPointTwo)
                {


                    strategyTestOriginale.customStop = strategyTestOriginale.Low[1];
                    Cbi.Order newStopOrder = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, strategyTestOriginale.OperationQuantity, 0, strategyTestOriginale.customStop, "", "ExitPoint2");
                    plotPoint2++;

                    if (newStopOrder != null)
                    {
                        if (strategyTestOriginale.stopOrder != null)
                        {
                            strategyTestOriginale.CancelOrder(strategyTestOriginale.stopOrder);
                        }

                        strategyTestOriginale.stopOrder = newStopOrder;
                        strategyTestOriginale.isPointTwo = true;
                    }
                    else
                    {
                        Draw.Text(strategyTestOriginale, $"Error_Point2_{plotPoint2}", "Errore nella creazione del nuovo ordine stop", 1, strategyTestOriginale.Low[1] - 5, Brushes.Red);
                        plotPoint2++;
                    }
                }
            }

            return strategyTestOriginale.isPointTwo;
        }
    }
}
