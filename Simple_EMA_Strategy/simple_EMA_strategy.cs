#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Custom.CustomClasses;
using System.Net.Http;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using Google.Protobuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
#endregion



namespace NinjaTrader.NinjaScript.Strategies
{
    public class SimpleEMAStrategy : Strategy
    {

        private bool enterLong;
        private int barsSinceEntry;
        private int plotPoint1;
        private Cbi.Order longOrder = null;
        private int accountSize;
        private int quantityCount;
        private int lastTradeId;
        private DateTime exitDate;
        private DateTime enterDate;

        private int counter;

        public bool inPosition;
        public int tradeResult;
        public double customStop;
        public double enterPrice;

        [XmlIgnore]
        public Cbi.Order stopOrder;
        public double FirstStopLossMargin;
        public int OperationQuantity;
        public bool filteredPattern;
        public bool isPointOne;
        public bool isPointTwo;
        public EMA EMA1;
        public EMA EMA3;
        public EMA EMA5;
        public ADX ADX;


        public double exitPrice;
        public bool FirstRiskClosed;
        public bool SecondRiskClosed;
        public bool TotalRiskClosed;

        private RiskReduction riskReduction;
        private PatternFilter patternFilter;
        private CustomTralingStop customTralingStop;
        private PositionManagment positionManagment;
        


        private static readonly HttpClient client = new HttpClient();
        private bool modelPrediction = false; 

        #region Trade Display
        [NinjaScriptProperty]
        [Display(Name = "Stop loss", GroupName = "Parameters", Order = 0)]
        public int StopLoss { get; set; } = 20;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "SimpleEMAStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = true;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                inPosition = false;
                FirstStopLossMargin = 1;
                accountSize = 10000;


            }
            else if (State == State.Configure)
            {
                IsUnmanaged = true;

                riskReduction = new RiskReduction(this);
                patternFilter = new PatternFilter(this);
                customTralingStop = new CustomTralingStop(this);
                positionManagment = new PositionManagment(this);

            }
            else if (State == State.DataLoaded)
            {
                EMA1 = EMA(Close, 1);
                EMA3 = EMA(Close, 3);
                EMA5 = EMA(Close, 5);

                ADX = ADX(14);

                EMA1.Plots[0].Brush = Brushes.Green;
                EMA3.Plots[0].Brush = Brushes.Blue;
                EMA5.Plots[0].Brush = Brushes.Violet;

                AddChartIndicator(EMA1);
                AddChartIndicator(EMA3);
                AddChartIndicator(EMA5);
                AddChartIndicator(ADX);
            }
            
        }

        protected override void OnBarUpdate()
        {

            if (BarsInProgress != 0 || CurrentBars[0] < BarsRequiredToTrade)
            {
                return;
            }


            double sogliaGAP = 6;
            bool isGAP = Math.Abs(Open[0] - Close[1]) >= sogliaGAP;

            List<ChartPattern> patternsToAvoid = new List<ChartPattern>()
            {
                ChartPattern.BearishEngulfing,
                ChartPattern.HangingMan,
                ChartPattern.InvertedHammer,
                ChartPattern.ThreeWhiteSoldiers

            };

            List<int> barsAgo = new List<int>()
            {
                0
            };

            int trendStrenght = 1;

            filteredPattern = patternFilter.AvoidPatterns(patternsToAvoid, barsAgo, trendStrenght);

            if (InputSignal() && IsIncreasing() && !filteredPattern && !isGAP)
            {
                enterLong = true;
            }
            else
            {
                enterLong = false;
            }


            if (Position.MarketPosition == MarketPosition.Flat && enterLong)
            {
                EnterLongOrder();
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                CustomStopManager();
            }

        }

        private bool InputSignal()
        {

            bool isEMA = (EMA1[0] > EMA3[0]) && (EMA3[0] > EMA5[0]) && (EMA1[0] > EMA5[0]);

            return isEMA;
        }

        private bool IsIncreasing()
        {
            double diff0 = EMA3[0] - EMA5[0];
            double diff1 = EMA3[1] - EMA5[1];

            double slope0_1 = diff0 - diff1;

            return slope0_1 > 0;
        }

        public double GetCurrentVolume()
        {
            return Volume[0];
        }


        private void EnterLongOrder()
        {
            FirstRiskClosed = false;
            SecondRiskClosed = false;
            TotalRiskClosed = false;
            isPointOne = false;
            isPointTwo = false;

            OperationQuantity = 1;

            if (OperationQuantity > 0)
            {
                longOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Market, OperationQuantity, 0, 0, "", "LongEntry");
                enterPrice = Close[0];
                Draw.Text(this, $"Long{enterPrice}, data {Time[0]}", $"Long {enterPrice}, data {Time[0]}", -1, (Close[0] + 5), Brushes.Green);
                quantityCount++;

                inPosition = true;
            }

            enterDate = Time[0];

        }

        protected void CustomStopManager()
        {

            if (Position.MarketPosition != MarketPosition.Long)
            {
                return;
            }

            

            if (inPosition)
            {
                double soglia = enterPrice - StopLoss;
                if (Low[0] < soglia && (!FirstRiskClosed || !SecondRiskClosed || !TotalRiskClosed))
                {
                    riskReduction.ImmediateExit();
                }
            }

            barsSinceEntry++;


            if (barsSinceEntry == 1 && inPosition)
            {
                riskReduction.FirstReducerRisk();
            }
            else if (barsSinceEntry == 2 && inPosition)
            {
                riskReduction.SecondReducerRisk();
            }
            else if (barsSinceEntry > 2 && !TotalRiskClosed && Low[0] > enterPrice)
            {
                riskReduction.RiskCloser();
            }
            else if (barsSinceEntry > 3 && TotalRiskClosed && inPosition)
            {
                customTralingStop.CustomTrailingStop();
                positionManagment.PositionManager();
            }
        }


        private double GetRiskPercentage()
        {
            if (accountSize < 5000) return 0.015;
            if (accountSize < 8000) return 0.01;
            if (accountSize < 10000) return 0.02;
            if (accountSize < 20000) return 0.025;
            if (accountSize < 50000) return 0.03;
            if (accountSize < 80000) return 0.035;
            if (accountSize < 100000) return 0.04;
            if (accountSize < 100000) return 0.07;
            
            else
                return 0.02;
        }



        public int GetOperationQuantity()
        {

            double riskPerTrade = accountSize * GetRiskPercentage();
            double riskPerContract = 50 * FirstStopLossMargin;



            if (riskPerContract <= 0)
            {
                return 1;
            }

            double quantity = riskPerTrade / riskPerContract;
            int contracts = Math.Max(1, (int)Math.Floor(quantity));



            double marginPerContract = 1500;
            int maxContracts = (int)(accountSize / marginPerContract);

            contracts = Math.Min(contracts, maxContracts);

            Draw.Text(this, $"riskPerContract{plotPoint1}", $"Contratti finali {contracts}", 1, (High[1] + 5), Brushes.Yellow);
            return contracts > 0 ? contracts : 1;
        }

        

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {

            if(execution.Order == null)
            {
                return;
            }


            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);


            if (execution.Order.Name == "LongEntry")
            {
                enterPrice = execution.Order.AverageFillPrice;
                barsSinceEntry = 0;

            }

            if (execution.Order != null && (execution.Order.Name == "StopLossImmediate" ||
                execution.Order.Name == "ExitForced" ||
                execution.Order.Name == "FirstStop" ||
                 execution.Order.Name == "SecondStop" ||
                 execution.Order.Name == "ClosingStop" ||
                 execution.Order.Name == "ExitPoint2"))
            {

                exitPrice = execution.Order.AverageFillPrice;

                exitDate = Time[0];

                enterPrice = 0;
                barsSinceEntry = 0;
                inPosition = false;
            }


            if (marketPosition == MarketPosition.Flat)
            {
                barsSinceEntry = 0;
                FirstRiskClosed = false;
                SecondRiskClosed = false;
                TotalRiskClosed = false;
                longOrder = null;
            }

        }


        public void LogInfo(string message)
        {
            Log(message, LogLevel.Information);
        }

        public void LogError(string message)
        {
            Log(message, LogLevel.Error);
        }

    }
}