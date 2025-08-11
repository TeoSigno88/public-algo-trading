using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Indicators;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui;
using System.IO;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EMAPullbackSmartTrail : Strategy
    {
        private EMA fastEMA;
        private EMA slowEMA;
        private SMA volumeSMA;

        private DateTime lastExitTime = Core.Globals.MinDate;
        private bool inPosition = false;

        private double entryPrice;
        private int trailStep;
        
        private int stopLossPriceCounter;
        private int breakEvenCounter;

        private int firstTrailCounter;
        private int secondTrailCounter;
        private int thirdTrailCounter;
        private int lastTrailCounter;
        
        private bool isFirstBar;

        private double unrealizedPnL;

        private int trailBarCounterStep3 = 0;   //update the third trail stop every 2 bars
        private double trailPrice;

        private string logFilePath;
        private bool isCsvHeaderWritten = false;

        #region Trade Display
        [NinjaScriptProperty]
        [Display(Name = "Draw trade details", GroupName = "Parameters", Order = 0)]
        public bool DrawDetails { get; set; } = true;
        #endregion


        #region
        [NinjaScriptProperty]
        [Display(Name = "Allow Long", GroupName = "Parameters", Order = 1)]
        public bool AllowLong { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Allow Short", GroupName = "Parameters", Order = 2)]
        public bool AllowShort { get; set; } = true;
        #endregion


        #region Signal
        [NinjaScriptProperty]
        [Display(Name = "Fast EMA", GroupName = "Parameters", Order = 3)]
        public int FastEMA { get; set; } = 9;

        [NinjaScriptProperty]
        [Display(Name = "Slow EMA", GroupName = "Parameters", Order = 4)]
        public int SlowEMA { get; set; } = 21;
        #endregion

        #region Time
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "Parameters", Order = 5)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time", GroupName = "Parameters", Order = 6)]
        public TimeSpan StartTime { get; set; } = new TimeSpan(9, 30, 0);

        [NinjaScriptProperty]
        [Display(Name = "End Time", GroupName = "Parameters", Order = 7)]
        public TimeSpan EndTime { get; set; } = new TimeSpan(15, 30, 0);

        [NinjaScriptProperty]
        [Display(Name = "Cooldown (minutes)", GroupName = "Parameters", Order = 8)]
        public int MinEntryDelayMinutes { get; set; } = 3;

        #endregion


        #region Filters
        [NinjaScriptProperty]
        [Display(Name = "Previous volumes", GroupName = "Parameters", Order = 9)]
        public int PreviousVolumes { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Volume comparison", GroupName = "Parameters", Order = 10)]
        public double VolumeComparison { get; set; } = 0.8;


        [NinjaScriptProperty]
        [Display(Name = "EMA distance", GroupName = "Parameters", Order = 11)]
        public int EMADistance { get; set; } = 4;


        [NinjaScriptProperty]
        [Display(Name = "Confirm direction (min 2 bars)", GroupName = "Parameters", Order = 12)]
        public bool ConfirmDirection { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Touched", GroupName = "Parameters", Order = 13)]
        public bool TouchedCheck { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Check slope", GroupName = "Parameters", Order = 14)]
        public bool CheckSlope { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Check reversal", GroupName = "Parameters", Order = 15)]
        public bool Reversal { get; set; } = false;
        #endregion




        #region Risk Management Settings 
        [NinjaScriptProperty]
        [Display(Name = "Initial stop loss in ticks", GroupName = "Parameters", Order = 16)]
        public int InitialStopLossInTick { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Break even level", GroupName = "Parameters", Order = 17)]
        public int BreakEvenLevel { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "First trail stop", GroupName = "Parameters", Order = 18)]
        public int FirstTrailStop { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Second trail stop", GroupName = "Parameters", Order = 19)]
        public int SecondTrailStop { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Third trail stop", GroupName = "Parameters", Order = 20)]
        public int ThirdTrailStop { get; set; } = 70;


        [NinjaScriptProperty]
        [Display(Name = "Last trail stop", GroupName = "Parameters", Order = 21)]
        public int LasttrailStop { get; set; } = 5;
        #endregion

        #region
        [NinjaScriptProperty]
        [Display(Name = "Create CSV File", GroupName = "Parameters", Order = 22)]
        public bool PrintCSVFile { get; set; } = false;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                IsInstantiatedOnEachOptimizationIteration = false;


                Description = @"EMA Pullback Smart Trail with Smart Trailing Stop";
                Name = "EMA_pullback_Entry";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                trailPrice = 0;
                BarsRequiredToTrade = 30;
                IsInstantiatedOnEachOptimizationIteration = true;

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                logFilePath = Path.Combine(desktopPath, "EMA_Trail_Log.csv");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                fastEMA = EMA(FastEMA);
                slowEMA = EMA(SlowEMA);
                volumeSMA = SMA(Volumes[0], PreviousVolumes);

                fastEMA.Plots[0].Brush = Brushes.Violet;
                slowEMA.Plots[0].Brush = Brushes.White;

                AddChartIndicator(fastEMA);
                AddChartIndicator(slowEMA);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
            {
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat && inPosition)
            {
                DateTime time = Times[0][0];
                unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Ticks, Close[0]);
               
                if (isFirstBar)
                {
                    SetStopLoss();
                    isFirstBar = false;
                    return;
                }

                if (!isFirstBar)
                {
                    ManageTrailingStop();
                }
                 
            }


            if (Position.MarketPosition == MarketPosition.Flat && !inPosition)
            {
                if (Times[0][0] < lastExitTime.AddMinutes(MinEntryDelayMinutes))
                {
                    return;
                }

                //looking for long pullback
                if (AllowLong  && FindLongPullBack())
                {

                    //if I find a long pullback, I'll check entry conditions
                    if (CheckEntryConditions("bull"))
                    {
                        
                        EnterLong(1, "Long");
                        entryPrice = Close[0];
                        trailStep = 0;
                        inPosition = true;
                        isFirstBar = true;

                        return;

                    }

                    LogTradeEvent("Entry", "Long", entryPrice);
                }

                if (AllowShort && FindShortPullBack())
                {

                    //if I find a short pullback, I'll check entry conditions
                    if (CheckEntryConditions("bear"))
                    {

                        EnterShort(1, "Short");
                        entryPrice = Close[0];
                        trailStep = 0;
                        inPosition = true;
                        isFirstBar = true;

                        inPosition = true;
                        return;

                    }

                    LogTradeEvent("Entry", "Short", entryPrice);

                }


            }
        }

       
        private bool FindLongPullBack()
        {
            int trendLength = 3;
            int pullbackMin = 2;
            int pullbackMax = 5;

            if (CurrentBar < pullbackMax + trendLength + 1)
            {
                return false;
            }


            //check for a "Soft" Uptrend: at least 2 green candles above the last 'trendLength'
            int greenBarCount = 0;
            for (int i = pullbackMax + 1; i <= pullbackMax + trendLength; i++)
            {
                if (Close[i] > Open[i])
                {
                    greenBarCount++;
                }
                    
            }

            if (greenBarCount < 2)
            {
                return false;
            }

            //check "Soft" Pullback: at least 2 red candles, max 1 below EMA 21
            int redBars = 0;
            int belowslowEMA = 0;

            for (int i = pullbackMax; i >= pullbackMin; i--)
            {
                bool isPullback = true;
                belowslowEMA = 0;

                for (int j = 0; j < i; j++)
                {
                    int barIndex = i - j;
                    if (Close[barIndex] > Open[barIndex]) isPullback = false;
                    if (Close[barIndex] < slowEMA[barIndex]) belowslowEMA++;
                }

                if (isPullback && belowslowEMA <= 1)
                {
                    redBars = i;
                    break;
                }
            }

            if (redBars == 0)
            {
                return false;
            }

            //entry Confirmation: Green candle closes above or near EMA 9
            if (Close[0] > Open[0] && Close[0] >= fastEMA[0] * 0.995)
            {
                return true;
            }

            return false;
        }


        private bool FindShortPullBack()
        {
            int trendLength = 3;
            int pullbackMin = 2;
            int pullbackMax = 5;

            if (CurrentBar < pullbackMax + trendLength + 1)
                return false;

            //bearish trend: at least 2 red candles
            int redBarCount = 0;
            for (int i = pullbackMax + 1; i <= pullbackMax + trendLength; i++)
                if (Close[i] < Open[i]) redBarCount++;

            if (redBarCount < 2) return false;

            //bullish pullback: green candles, max 1 above the EMA
            int greenBars = 0;
            int aboveSlowEMA = 0;

            for (int i = pullbackMax; i >= pullbackMin; i--)
            {
                bool isPullback = true;
                aboveSlowEMA = 0;

                for (int j = 0; j < i; j++)
                {
                    int barIndex = i - j;
                    if (Close[barIndex] < Open[barIndex]) isPullback = false;
                    if (Close[barIndex] > slowEMA[barIndex]) aboveSlowEMA++;
                }

                if (isPullback && aboveSlowEMA <= 1)
                {
                    greenBars = i;
                    break;
                }
            }

            if (greenBars == 0) return false;

            if (Close[0] < Open[0] && Close[0] <= fastEMA[0] * 1.005)
                return true;

            return false;
        }

        private bool CheckEntryConditions(string pullbackType)
        {

            if (EnableTimeFilter && (Times[0][0].TimeOfDay < StartTime || Times[0][0].TimeOfDay > EndTime))
            {
                return false;
            }

            //type of trend and distance
            bool isBullTrend = fastEMA[0] > slowEMA[0] && (fastEMA[0] - slowEMA[0]) > EMADistance * TickSize;
            bool isBearTrend = fastEMA[0] < slowEMA[0] && (slowEMA[0] - fastEMA[0]) > EMADistance * TickSize;

            //slope
            bool isBullSloped = fastEMA[0] > fastEMA[3];
            bool isBearSloped = fastEMA[0] < fastEMA[3];

            //touch
            bool isTouchBull = (Low[1] <= slowEMA[1] && High[1] >= slowEMA[1]) || (Low[1] <= fastEMA[1] && High[1] >= fastEMA[1]);
            bool isTouchBear = (Low[1] >= slowEMA[1] && High[1] <= slowEMA[1]) || (Low[1] >= fastEMA[1] && High[1] <= fastEMA[1]);
            if (DrawDetails && TouchedCheck && (isTouchBull || isTouchBear))
            {
                Draw.Dot(this, $"Touch_{CurrentBar}", true, 1, Close[1], Brushes.White);
            }

           

            bool bullishReversal = Close[1] < fastEMA[1] && Close[0] > Close[1];
            bool bearishReversal = Close[1] > fastEMA[1] && Close[0] < Close[1];
            if (DrawDetails && TouchedCheck && (bullishReversal || bearishReversal))
            {
                Draw.ArrowUp(this, $"BullRev_{CurrentBar}", true, 0, Low[0] - TickSize * 3, Brushes.LimeGreen);
            }

            //volume
            bool volumeOk = Volume[0] > (volumeSMA[1] * VolumeComparison);

            bool bullDirection = ConfirmCandleDirection("bull");
            bool bearDirection = ConfirmCandleDirection("bear");
            if (DrawDetails && ConfirmDirection && (bullDirection || bearDirection))
            {
                double avgPrice = (Low[3] + High[1]) / 2;
                Draw.Line(this, $"ConfirmLine_{CurrentBar}", false, 3, avgPrice - 3, 0, avgPrice - 3, Brushes.White, DashStyleHelper.Solid, 2);
            }


            //some entry requirements (there is a bull pullback - pullbackType, we are in a bull trend - isBullTrend and the volumes are ok - volumeOk) are essential
            //other entry requirements are only required if selected by the user
            if (pullbackType == "bull" && isBullTrend && volumeOk && (!CheckSlope || (CheckSlope && isBullSloped)) && (!TouchedCheck || (TouchedCheck && isTouchBull)) && (!ConfirmDirection || (ConfirmDirection && bullDirection)) && (!Reversal || (Reversal && bullishReversal)))
            {
                return true;
            }


            if (pullbackType == "bear" && isBearTrend && volumeOk && (!CheckSlope || (CheckSlope && isBearSloped)) && (!TouchedCheck || (TouchedCheck && isTouchBear)) && (!ConfirmDirection || (ConfirmDirection && bearDirection)) && (!Reversal || (Reversal && bearishReversal)))
            {
                return true;
            }
            return false;
        }

        private bool ConfirmCandleDirection(string trend)
        {
            int count = 0;
            if (trend == "bull")
            {
                if (Close[1] > Close[2]) count++;
                if (Close[2] > Close[3]) count++;
                if (Close[3] > Close[4]) count++;
            }
            else if (trend == "bear")
            {
                if (Close[1] < Close[2]) count++;
                if (Close[2] < Close[3]) count++;
                if (Close[3] < Close[4]) count++;
            }

            return count >= 2;
        }

        private void SetStopLoss()
        {
            //I set the stop loss by calculating the correspondence in ticks for the MES (1 point = 4 ticks)
            double stopLossPrice = entryPrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                stopLossPrice -= (InitialStopLossInTick * TickSize);
            }
                
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                stopLossPrice += (InitialStopLossInTick * TickSize);
            }
                

            SetStopLoss(CalculationMode.Price, stopLossPrice);


            if (DrawDetails)
            {
                DateTime time = Times[0][0];
                Draw.Text(this, "StopLoss details", $"SL @{stopLossPrice:F2}, time: {time.ToString("HH:mm:ss")}", -2, stopLossPrice +1, Brushes.Red);
                Draw.Line(this, $"breakEvenLine_{stopLossPriceCounter}", false, 0, stopLossPrice, -1, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
                stopLossPriceCounter++;
            }

            LogTradeEvent("Initial Stop", Position.MarketPosition.ToString(), stopLossPrice);
            return;
        }


        private void ManageTrailingStop()
        {

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            bool isShort = Position.MarketPosition == MarketPosition.Short;

            if (trailStep == 0 && unrealizedPnL >= BreakEvenLevel)
            {
                SetStopLoss(CalculationMode.Price, entryPrice); //break-even
                trailPrice = entryPrice;
                trailStep++; 

                if (DrawDetails)
                {
                    DateTime time = Times[0][0];
                    Draw.Text(this, "breakEven", $"Break event @{entryPrice:F2}, time: {time:HH:mm:ss}", -2, entryPrice + (isLong ? 3 : -3), Brushes.Orange);
                    Draw.Line(this, $"breakEvenCounter_{breakEvenCounter}", false, 0, entryPrice, -1, entryPrice, Brushes.Orange, DashStyleHelper.Solid, 2);
                    breakEvenCounter++;
                }

                LogTradeEvent("Break Even", Position.MarketPosition.ToString(), entryPrice);

                return;
            }

            //STEP 1 first trail stop
            if (trailStep == 1 && unrealizedPnL >= FirstTrailStop)
            {
                double proposedTrail = isLong ? Close[1] - (FirstTrailStop * TickSize) : Close[1] + (FirstTrailStop * TickSize);
                bool isvalid = isLong ? proposedTrail > trailPrice : proposedTrail < trailPrice;

                if (isvalid)
                {
                    trailPrice = proposedTrail;
                    SetStopLoss(CalculationMode.Price, trailPrice);
                    trailStep++;

                    if (DrawDetails)
                    {
                        DateTime time = Times[0][0];
                        Draw.Text(this, "firstTrail", $"TS 1 @ {trailPrice:F2}, unrealized: {unrealizedPnL}, time: {time:HH:mm:ss}", -2, trailPrice + (isLong ? 3 : -3), Brushes.Green);
                        Draw.Line(this, $"firstTrailCounter_{firstTrailCounter}", false, 0, trailPrice, -1, trailPrice, Brushes.Green, DashStyleHelper.Solid, 2);
                        firstTrailCounter++;
                    }
                }

                LogTradeEvent("Trail Step 1", Position.MarketPosition.ToString(), trailPrice);
                return;
            }

            //STEP 2 second trail stop
            if (trailStep == 2 && unrealizedPnL >= SecondTrailStop)
            {
                double proposedTrail = isLong ? Close[1] - (SecondTrailStop * TickSize) : Close[1] + (SecondTrailStop * TickSize);
                bool isvalid = isLong ? proposedTrail > trailPrice : proposedTrail < trailPrice;

                if (isvalid)
                {
                    trailPrice = Close[1];
                    SetStopLoss(CalculationMode.Price, trailPrice);
                    trailStep++;

                    if (DrawDetails)
                    {
                        DateTime time = Times[0][0];
                        Draw.Text(this, "second trail", $"TS 2 @ {trailPrice:F2}, unrealized: {unrealizedPnL}, time: {time:HH:mm:ss}", -2, trailPrice + (isLong ? 3 : -3), Brushes.Violet);
                        Draw.Line(this, $"secondTrailCounter_{secondTrailCounter}", false, 0, trailPrice, -1, trailPrice, Brushes.Violet, DashStyleHelper.Solid, 2);
                        secondTrailCounter++;
                    }
                }

                LogTradeEvent("Trail Step 2", Position.MarketPosition.ToString(), trailPrice);
                return;
            }

            //STEP 3 third trail stop
            if (trailStep == 3 && unrealizedPnL >= ThirdTrailStop)
            {
                trailBarCounterStep3 = 0;

                double proposedTrail = isLong ? Close[1] - (ThirdTrailStop * TickSize) : Close[1] + (ThirdTrailStop * TickSize);
                bool isvalid = isLong ? proposedTrail > trailPrice : proposedTrail < trailPrice;

                if (isvalid)
                {
                    trailPrice = Close[1];
                    SetStopLoss(CalculationMode.Price, trailPrice);
                    trailStep++;

                    if (DrawDetails)
                    {
                        DateTime time = Times[0][0];
                        Draw.Text(this, "third trail", $"TS 3 @ {trailPrice:F2}, time: {time:HH:mm:ss}", -2, trailPrice + (isLong ? 3 : -3), Brushes.White);
                        Draw.Line(this, $"thirdTrailCounter_{thirdTrailCounter}", false, -1, trailPrice, 2, trailPrice, Brushes.White, DashStyleHelper.Solid, 2);
                        thirdTrailCounter++;
                    }
                }

                LogTradeEvent("Trail Step 3", Position.MarketPosition.ToString(), trailPrice);
                return;
            }

            //STEP 4 last trail stop
            if (trailStep == 4)
            {
                trailBarCounterStep3++;

                if (trailBarCounterStep3 >= 2)
                {
                    trailBarCounterStep3 = 0;

                    double proposedTrail = isLong ? Close[1] - (LasttrailStop * TickSize) : Close[1] + (LasttrailStop * TickSize);
                    bool isvalid = isLong ? proposedTrail > trailPrice : proposedTrail < trailPrice;

                    if (isvalid)
                    {
                        trailPrice = proposedTrail;
                        SetStopLoss(CalculationMode.Price, trailPrice);

                        if (DrawDetails)
                        {
                            DateTime time = Times[0][0];
                            Draw.Text(this, "last trail", $"TS final @ {trailPrice:F2}, time: {time:HH:mm:ss}", -2, trailPrice + (isLong ? 3 : -3), Brushes.Yellow);
                            Draw.Line(this, $"lastTrailCounter_{lastTrailCounter}", false, -1, trailPrice, 2, trailPrice, Brushes.Yellow, DashStyleHelper.Solid, 2);
                            lastTrailCounter++;
                        }

                        LogTradeEvent("Last trail step", Position.MarketPosition.ToString(), trailPrice);
                    }
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                if (Position.MarketPosition == MarketPosition.Long || Position.MarketPosition == MarketPosition.Short)
                {
                    entryPrice = execution.Price;
                    isFirstBar = true;
                }

                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    lastExitTime = time;
                    inPosition = false;
                    inPosition = false;
                    trailPrice = 0;


                    LogTradeEvent("Exit", execution.Order.OrderAction.ToString(), price, $"PnL: {unrealizedPnL}");

                }
            }
        }




        private void LogTradeEvent(string eventType, string direction, double price, string extraInfo = "")
        {

            if (PrintCSVFile)
            {
                DateTime time = Times[0][0];
                string logLine = $"{time:yyyy-MM-dd HH:mm:ss},{eventType},{direction},{price:F2},{extraInfo}";

                Print(logLine);

                try
                {
                    if (!isCsvHeaderWritten && !File.Exists(logFilePath))
                    {
                        File.AppendAllText(logFilePath, "Timestamp,Event,Direction,Price,Note\n");
                        isCsvHeaderWritten = true;
                    }

                    File.AppendAllText(logFilePath, logLine + "\n");
                }
                catch (Exception ex)
                {
                    Print("CSV writing error: " + ex.Message);
                }
            }
            
        }


    }
}