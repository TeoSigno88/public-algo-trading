#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion


namespace NinjaTrader.Custom.CustomClasses
{
    internal class RiskReduction
    {
        private double customStop;
        private int plotStopLossCounter;
        private int plotFirstStopLossCounter;
        private int plotSecondStopLossCounter;
        private int plotRiskCloserCounter;

        private ATR atrValue;

        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia

        public RiskReduction(NinjaTrader.NinjaScript.Strategies.TestOriginale strategy)
        {
            this.strategyTestOriginale = strategy;
        }


        //ATTENZIONE
        //NinjaTrader rifiuta un ordine se il prezzo è fuori dal range operativo realistico della barra o del mercato
        //ordini troppo distanti vengono ignorati

        public void ImmediateExit()
        {
            //se il prezzo attuale è inferiore a quello di ingresso compresa la soglia di stopLoss allora esco subito e chiudo qualsiasi tipo di ordine aperto
            double soglia = strategyTestOriginale.Close[1] - strategyTestOriginale.StopLoss;

            if (strategyTestOriginale.Low[0] < soglia)
            {
                if (strategyTestOriginale.Position.MarketPosition != MarketPosition.Long)
                {
                    return;
                }

                //forzo la chiusura
                foreach (Cbi.Order order in strategyTestOriginale.Orders)
                {
                    if (order != null &&
                        order.OrderState == OrderState.Working &&
                        order.Name != "StopLossImmediate")
                    {
                        strategyTestOriginale.CancelOrder(order);
                        strategyTestOriginale.LogInfo($"[StopLossImmediate] Cancellato ordine pendente: {order.Name}");
                    }
                }

                strategyTestOriginale.stopOrder = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, strategyTestOriginale.OperationQuantity, 0, 0, "", "StopLossImmediate");
                strategyTestOriginale.inPosition = false;

                strategyTestOriginale.LogInfo($"[StopLossImmediate] Uscita immediata a mercato perché Low[0] = {strategyTestOriginale.Low[0]} < Close[1] - 20 ({soglia}) alle {strategyTestOriginale.Time[0]}");

                Draw.Text(strategyTestOriginale, $"_ImmediateStop{plotStopLossCounter}", $"Exit Immediate @ {strategyTestOriginale.Low[0]}, time: {strategyTestOriginale.Time[0].ToString("HH:mm:ss")}", -1, strategyTestOriginale.Low[0] - 6, Brushes.Red);
                plotStopLossCounter++;

                return;
            }
        }

        public void DinamicStopLoss()
        {
            if (strategyTestOriginale.Position.MarketPosition != MarketPosition.Long || strategyTestOriginale.CurrentBar < atrValue.Period)
                return;

            // Calcolo uno stop dinamico in base alla volatilità (ATR a 14 periodi)
            double stopBar = strategyTestOriginale.Low[0] - atrValue[0] * 0.8; // moltiplicatore personalizzabile

            // Validazione del livello stop
            if (stopBar >= strategyTestOriginale.Close[0])
            {
                strategyTestOriginale.LogError("Stop loss troppo vicino al prezzo attuale, ignorato");
                return;
            }

            Cbi.Order stopLoss = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, strategyTestOriginale.OperationQuantity, 0, stopBar, "", "StopLoss");

            if (stopLoss == null)
            {
                strategyTestOriginale.LogError($"Impossibile inserire lo stop loss dinamico");
                return;
            }

            strategyTestOriginale.stopOrder = stopLoss;
            strategyTestOriginale.LogInfo($"Stop dinamico inserito al prezzo {stopBar} (ATR: {atrValue})");

            Draw.Line(strategyTestOriginale, $"stopLossLine_{plotStopLossCounter}", false, -1, stopBar, -2, stopBar, Brushes.Orange, DashStyleHelper.Dot, 2);
            Draw.Text(strategyTestOriginale, $"stopLossText_{plotStopLossCounter}", $"ATR Stop {Math.Round(stopBar, 2)}", -1, stopBar - 1, Brushes.Orange);
            plotStopLossCounter++;
        }
        public void FirstReducerRisk()
        {
            //uso strategy.Position perché eredito da strategy
            if (strategyTestOriginale.Position.MarketPosition == MarketPosition.Long) //uso il riferimento alla strategia
            {
               
                if (strategyTestOriginale.Low[1] <= strategyTestOriginale.Low[2])
                {

                    Draw.Line(strategyTestOriginale, $"FirstReducer_{plotFirstStopLossCounter}", false, 0, customStop, 1, customStop, Brushes.White, DashStyleHelper.Solid, 2);
                    plotFirstStopLossCounter++;
                    return;
                }

                customStop = strategyTestOriginale.Low[1] - strategyTestOriginale.FirstStopLossMargin;

                if (customStop < strategyTestOriginale.Low[1])
                {
                    // Se esiste già ed è filled, non toccare nulla
                    if (strategyTestOriginale.stopOrder != null &&
                        strategyTestOriginale.stopOrder.OrderState == OrderState.Filled)
                        return;

                    // Se esiste ed è in lavorazione/accettato, cancellalo prima
                    if (strategyTestOriginale.stopOrder != null &&
                        (strategyTestOriginale.stopOrder.OrderState == OrderState.Working ||
                         strategyTestOriginale.stopOrder.OrderState == OrderState.Accepted))
                    {
                        strategyTestOriginale.CancelOrder(strategyTestOriginale.stopOrder);
                    }

                    var newStop = strategyTestOriginale.SubmitOrderUnmanaged(
                        0, OrderAction.Sell, OrderType.StopMarket,
                        strategyTestOriginale.OperationQuantity, 0, customStop, "", "FirstStop");

                    if (newStop != null)
                    {
                        strategyTestOriginale.stopOrder = newStop;
                        strategyTestOriginale.FirstRiskClosed = true;

                        strategyTestOriginale.LogInfo($"FirstReducerRisk() al prezzo {customStop} in data {strategyTestOriginale.Time[0]}");
                        Draw.Line(strategyTestOriginale, $"firstReducerRiskLine_{Guid.NewGuid()}", false, -1, customStop, -2, customStop, Brushes.Orange, DashStyleHelper.Dot, 2);
                        Draw.Text(strategyTestOriginale, $"firstReducerRiskText_{Guid.NewGuid()}", $"ReducerRisk_1 {customStop}", -1, customStop - 1, Brushes.Orange);
                    }
                    else
                    {
                        strategyTestOriginale.LogError($"SubmitOrderUnmanaged ha restituito null (FirstStop). customStop={customStop}, qty={strategyTestOriginale.OperationQuantity}, time={strategyTestOriginale.Time[0]}");
                    }
                }

            }
        }

        public void SecondReducerRisk()
        {
            if (strategyTestOriginale.Position.MarketPosition == MarketPosition.Long)
            {

                //se il di riferimento della barra appena conclusa (Low[1]) è inferiore o uguale al minimo precedente allora non stringo lo stopLoss
                if (strategyTestOriginale.Low[1] <= strategyTestOriginale.Low[2])
                {
                    Draw.Line(strategyTestOriginale, $"SecondReducer_{plotSecondStopLossCounter}", false, 0, customStop, 1, customStop, Brushes.White, DashStyleHelper.Solid, 2);
                    plotSecondStopLossCounter++;
                    return;
                }

                //creo un nuovo ordine e se è valido allora elimino l'ordine precedente
                customStop = strategyTestOriginale.Low[1] - strategyTestOriginale.FirstStopLossMargin;
                Cbi.Order newStopOrder = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, strategyTestOriginale.OperationQuantity, 0, customStop, "", "SecondStop");

                if (newStopOrder != null)
                {
                    //cancello il vecchio ordine e gli associo un nuovo ordine
                    if (strategyTestOriginale.stopOrder != null &&
                           (strategyTestOriginale.stopOrder.OrderState == OrderState.Working ||
                            strategyTestOriginale.stopOrder.OrderState == OrderState.Accepted))
                    {
                        strategyTestOriginale.CancelOrder(strategyTestOriginale.stopOrder);

                    }
                    //associo il nuovo ordine
                    strategyTestOriginale.stopOrder = newStopOrder;
                    strategyTestOriginale.SecondRiskClosed = true;

                    strategyTestOriginale.LogInfo($"SecondReducerRisk() al prezzo {customStop} in data {strategyTestOriginale.Time[0]}");
                    Draw.Line(strategyTestOriginale, $"secondReducerRiskLine {plotSecondStopLossCounter}", false, -1, customStop, -2, customStop, Brushes.Yellow, DashStyleHelper.Dot, 2);
                    Draw.Text(strategyTestOriginale, $"secondReducerRiskText {plotSecondStopLossCounter}", $"ReducerRisk_2 {customStop}", -1, customStop - 1, Brushes.Yellow);
                    plotSecondStopLossCounter++;
                }
                else
                {

                    //se l'ordine è stato rifiutato mantengo il vecchio ordine esistente
                    //stampo l'errore nel caso in cui il nuovo ordine stop non sia stato gestito correttamente
                    Draw.Text(strategyTestOriginale, $"Text_Error_{plotSecondStopLossCounter}", "Errore nell'apertura del secondo stop loss", 0, customStop - 10, Brushes.Red);

                    strategyTestOriginale.LogError($"Errore: SubmitOrderUnmanaged ha restituito null. customStop={customStop}, time: {strategyTestOriginale.Time[0]}, quantity={strategyTestOriginale.OperationQuantity}");
                    plotSecondStopLossCounter++;
                }
            }
        }


        public void RiskCloser()
        {
            if (strategyTestOriginale.Position.MarketPosition == MarketPosition.Long)
            {

                //se il di riferimento della barra appena conclusa (Low[1]) è inferiore o uguale al minimo precedente allora non stringo lo stopLoss
                //if (strategyTestOriginale.Low[1] <= strategyTestOriginale.Low[2])
                //{
                //    Draw.Line(strategyTestOriginale, $"TotalReducerRisk_{plotRiskCloserCounter}", false, 0, customStop, 1, customStop, Brushes.White, DashStyleHelper.Solid, 2);
                //    plotRiskCloserCounter++;
                //    return;
                //}
                if (strategyTestOriginale.Low[0] > strategyTestOriginale.enterPrice)
                {

                    customStop = strategyTestOriginale.enterPrice + 0.1;
                    Cbi.Order newStopOrder = strategyTestOriginale.SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, strategyTestOriginale.OperationQuantity, 0, customStop, "", "ClosingStop");

                    //verifico che il nuovo ordine sia stato creato con successo
                    if (newStopOrder != null)
                    {
                        if (strategyTestOriginale.stopOrder != null &&
                           (strategyTestOriginale.stopOrder.OrderState == OrderState.Working ||
                            strategyTestOriginale.stopOrder.OrderState == OrderState.Accepted))
                        {
                            strategyTestOriginale.CancelOrder(strategyTestOriginale.stopOrder);
                        }

                        strategyTestOriginale.stopOrder = newStopOrder;
                        strategyTestOriginale.TotalRiskClosed = true;

                        strategyTestOriginale.LogInfo($"RiskCloser_() al prezzo {customStop} in data {strategyTestOriginale.Time[0]}");
                        Draw.Line(strategyTestOriginale, $"RiskCloserLine {plotRiskCloserCounter}", false, -1, customStop, -2, customStop, Brushes.Green, DashStyleHelper.Dash, 2);
                        Draw.Text(strategyTestOriginale, $"RiskCloserText {plotRiskCloserCounter}", $"RiskCloser_ {customStop}", -1, customStop - 1, Brushes.Green);
                        plotRiskCloserCounter++;
                    }
                    else
                    {
                        Draw.Text(strategyTestOriginale, $"Text_Error_{plotRiskCloserCounter}", "Errore nell'apertura del risk closer", 0, customStop - 5, Brushes.Red);
                        plotRiskCloserCounter++;

                    }
                }
            }
        }
    }
}
