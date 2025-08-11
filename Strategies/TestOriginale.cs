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



//controllare stopOrder in riskReduction

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class TestOriginale : Strategy
    {

        private bool enterLong;
        private int barsSinceEntry;
        private int plotPoint1;
        private Cbi.Order longOrder = null;
        private int accountSize;
        private int quantityCount;
        private int lastTradeId; // Memorizza l'ID della trade pi� recente, -1 indica che non � ancora stato assegnato
        private DateTime exitDate;
        private DateTime enterDate;

        private int counter;

        public bool inPosition; //tag globale per gestire la posizione in caso venga chiamata l'uscita immediata
        public int tradeResult;// 1 = profitto, 0 = perdita
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

        //classi custom
        private RiskReduction riskReduction;
        private PatternFilter patternFilter;
        private CustomTralingStop customTralingStop;
        private PositionManagment positionManagment;
        private RecoverData recoverData;
        


        private static readonly HttpClient client = new HttpClient();
        private bool modelPrediction = false;  // Variabile globale per memorizzare la previsione del modello


        #region Trade Display
        [NinjaScriptProperty]
        [Display(Name = "Stop loss", GroupName = "Parameters", Order = 0)]
        public int StopLoss { get; set; } = 20;
        #endregion

        protected override void OnStateChange()
        {
            //impostazione dei valori predefiniti senza nessuna connessione ai dati di mercato
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "TestOriginale";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                //Triggers the exit on close function 30 seconds prior to trading day end
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                //IsFillLimitOnTouch per ordini limite, se true, l'ordine viene riempito appena il prezzo tocca il livello specificato, se false, il prezzo deve superare il livello dell'ordine per garantire l'esecuzione
                IsFillLimitOnTouch = true;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;  //per quanto tempo l�ordine resta attivo => finch� non viene eseguito o annullato
                TraceOrders = false;    //stampa i log dettagliati di tutti gli ordini utile per il debug
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;  //cosa succede in caso di errore in tempo realte => blocca la strategia, cancella gli ordini
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                inPosition = false;
                FirstStopLossMargin = 1; //stop loss fisso in punti
                accountSize = 10000;


            }
            else if (State == State.Configure)
            {
                IsUnmanaged = true;

                //passo la strategia alle classi custom
                riskReduction = new RiskReduction(this);
                patternFilter = new PatternFilter(this);
                customTralingStop = new CustomTralingStop(this);
                positionManagment = new PositionManagment(this);
                recoverData = new RecoverData(this);

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
            else if(State == State.Terminated)
            {

                List<string> aggregateTable = new List<string>
                {
                    "TestOriginaleStrategyData",
                    "TradeResult",
                    "RiskData"
                };

                string autputPath = "C:\\Users\\volam\\Desktop\\Trading\\trades\\aggregated_trades.csv";

                recoverData.AggregatedTradeDataAndDownloadCSV(aggregateTable, autputPath);
            }
        }

        protected override void OnBarUpdate()
        {

            if (BarsInProgress != 0 || CurrentBars[0] < BarsRequiredToTrade)
            {
                return;
            }


            double sogliaGAP = 6;
            //rilevo un nuovo GAP
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

            //ottengo la previsione dal modello
            //modelPrediction = GetModelPrediction();
            //modelPrediction = true;
            //Apertura della posizione SOLO SE il modello XGBoost prevede True (modelPrediction)
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
            //input su orario giornaliero dalle 02:30 alle 17:00 fuso orario 7 ore indietro
            //TimeSpan startTime = new TimeSpan(15, 30, 0);
            //TimeSpan endTime = new TimeSpan(10, 00, 0);
            //TimeSpan currentTime = Time[0].TimeOfDay;
            //bool isInSessionTime = currentTime >= startTime & currentTime <= endTime;
            //bool isStrongTrend = true; // ADX[0] >= 20 && ADX[0] <= 85;//ADX[0] < 50;


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
            //imposto le variabili per la gestione del rischio
            FirstRiskClosed = false;
            SecondRiskClosed = false;
            TotalRiskClosed = false;
            isPointOne = false;
            isPointTwo = false;

            //OperationQuantity = GetOperationQuantity();
            OperationQuantity = 1;

            if (OperationQuantity > 0)
            {
                longOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Market, OperationQuantity, 0, 0, "", "LongEntry");
                enterPrice = Close[0];
                Draw.Text(this, $"Long{enterPrice}, data {Time[0]}", $"Long {enterPrice}, data {Time[0]}", -1, (Close[0] + 5), Brushes.Green);
                quantityCount++;

                inPosition = true;
            }

            //data di ingresso
            enterDate = Time[0];

            //salvo i dati
            lastTradeId = recoverData.SavetradeId(enterDate);
            DateTime time = Time[0];
            recoverData.TestOriginaleStrategyData(lastTradeId, time);

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
            //if (accountSize < 5000) return 0.015;
            //if (accountSize < 8000) return 0.01;
            //if (accountSize < 10000) return 0.02;
            //if (accountSize < 20000) return 0.025;
            //if (accountSize < 50000) return 0.03;
            //if (accountSize < 80000) return 0.035;
            //if (accountSize < 100000) return 0.04;
            //if (accountSize < 100000) return 0.07;
            //else
            return 0.02;
        }



        public int GetOperationQuantity()
        {

            //double riskPerContract = Close[0] * FirstStopLossMargin; //rischio per contratto

            double riskPerTrade = accountSize * GetRiskPercentage(); // 2% del capitale
            double riskPerContract = 50 * FirstStopLossMargin; //l'ES ha un valore di tick di 50$ per contratto, ossia 12,5$ per tick e ogni piunto sono 4 tick



            if (riskPerContract <= 0)
            {
                return 1; //evito divisioni per zero
            }

            double quantity = riskPerTrade / riskPerContract;   //calcolo il numero di contratti
            int contracts = Math.Max(1, (int)Math.Floor(quantity));


            //verifico che il margine richiesto non superi il capitale disponibile
            double marginPerContract = 1500; //ES richiede circa 5000$ di margine per contratto (dipende dal broker)
            int maxContracts = (int)(accountSize / marginPerContract);

            contracts = Math.Min(contracts, maxContracts);

            Draw.Text(this, $"riskPerContract{plotPoint1}", $"Contratti finali {contracts}", 1, (High[1] + 5), Brushes.Yellow);
            return contracts > 0 ? contracts : 1; //mi assicuro almeno 1 contratto
        }

        

        //viene chiamato ogni volta che un ordine viene eseguito (parzialmente o completamente) per aggiornare
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {

            if(execution.Order == null)
            {
                return;
            }


            //base.OnExecutionUpdate chiama il metodo della classe base (Strategy) ereditata.
            //� una buona pratica chiamare i metodi della classe base quando si sovrascrivono onde evitare che qualsiasi logica predefinita venga eseguita assieme al metodo custom
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);


            //if (execution.Order.OrderState == OrderState.Filled)

            if (execution.Order.Name == "LongEntry")
            {
                //aggiorno enterPrice, AvarageFillPrice � il prezzo medio al quale l'ordine � stato eseguito, utile in caso di eseguiti parziali
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

                //calcolo exitPrice
                exitPrice = execution.Order.AverageFillPrice;

                //calcolo profitto e perdita e aggiorno il bilancio
                //double tradeResult = exitPrice - enterPrice;
                //double tradePL = tradeResult * execution.Order.Quantity;
                //accountSize += (int)Math.Round(tradePL);

                //aggiorno i dati da salvare nel DB
                exitDate = Time[0];
                recoverData.TradeResults(lastTradeId, enterDate, enterPrice, exitDate, exitPrice);
                recoverData.UpdateRiskData(lastTradeId);

                //azzero i valori e reimposto saveTradeId su false
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

//TODO
//controllare le correlazioni tra le inclinazioni dei segnali e i risultati
//evitare ingressi con volatilit� alta
//se la candela precedente o l'attuale � una inverted hammer allora non entro o se sono entrato esco appena mi ripago la commissione

#region Wizard settings, neither change nor remove
/*@
<?xml version="1.0"?>
<ScriptProperties xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Calculate>OnBarClose</Calculate>
  <ConditionalActions>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Enter long position</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-07-13T16:30:21.5896903</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>Set Enter long position</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2024-07-13T16:30:21.5896903</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>Enter</ActionType>
          <Command>
            <Command>EnterLong</Command>
            <Parameters>
              <string>quantity</string>
              <string>signalName</string>
            </Parameters>
          </Command>
        </WizardAction>
      </Actions>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>EMA</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>EMA</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties>
                    <item>
                      <key>
                        <string>Period</string>
                      </key>
                      <value>
                        <anyType xsi:type="NumberBuilder">
                          <LiveValue xsi:type="xsd:string">3</LiveValue>
                          <BindingValue xsi:type="xsd:string">3</BindingValue>
                          <DefaultValue>0</DefaultValue>
                          <IsInt>true</IsInt>
                          <IsLiteral>true</IsLiteral>
                        </anyType>
                      </value>
                    </item>
                  </CustomProperties>
                  <IndicatorHolder>
                    <IndicatorName>EMA</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FF228B22&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>EMA</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>true</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-07-13T16:29:42.2823879</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>EMA</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>EMA</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties>
                    <item>
                      <key>
                        <string>Period</string>
                      </key>
                      <value>
                        <anyType xsi:type="NumberBuilder">
                          <LiveValue xsi:type="xsd:string">5</LiveValue>
                          <BindingValue xsi:type="xsd:string">5</BindingValue>
                          <DefaultValue>0</DefaultValue>
                          <IsInt>true</IsInt>
                          <IsLiteral>true</IsLiteral>
                        </anyType>
                      </value>
                    </item>
                  </CustomProperties>
                  <IndicatorHolder>
                    <IndicatorName>EMA</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FFDAA520&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>EMA</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>true</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-07-13T16:29:42.2953863</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>EMA(3)[0] &gt; EMA(5)[0]</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>EMA</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>EMA</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties>
                    <item>
                      <key>
                        <string>Period</string>
                      </key>
                      <value>
                        <anyType xsi:type="NumberBuilder">
                          <LiveValue xsi:type="xsd:string">5</LiveValue>
                          <BindingValue xsi:type="xsd:string">5</BindingValue>
                          <DefaultValue>0</DefaultValue>
                          <IsInt>true</IsInt>
                          <IsLiteral>true</IsLiteral>
                        </anyType>
                      </value>
                    </item>
                  </CustomProperties>
                  <IndicatorHolder>
                    <IndicatorName>EMA</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FFDAA520&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>EMA</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-07-13T16:30:39.3598126</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>EMA</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>EMA</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties>
                    <item>
                      <key>
                        <string>Period</string>
                      </key>
                      <value>
                        <anyType xsi:type="NumberBuilder">
                          <LiveValue xsi:type="xsd:string">7</LiveValue>
                          <BindingValue xsi:type="xsd:string">7</BindingValue>
                          <DefaultValue>0</DefaultValue>
                          <IsInt>true</IsInt>
                          <IsLiteral>true</IsLiteral>
                        </anyType>
                      </value>
                    </item>
                  </CustomProperties>
                  <IndicatorHolder>
                    <IndicatorName>EMA</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FFFFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>EMA</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>true</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-07-13T16:30:39.3628159</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>EMA(5)[0] &gt; EMA(7)[0]</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 1</SetName>
      <SetNumber>1</SetNumber>
    </ConditionalAction>
  </ConditionalActions>
  <CustomSeries />
  <DataSeries />
  <Description>Enter the description for your new custom Strategy here.</Description>
  <DisplayInDataBox>true</DisplayInDataBox>
  <DrawHorizontalGridLines>true</DrawHorizontalGridLines>
  <DrawOnPricePanel>true</DrawOnPricePanel>
  <DrawVerticalGridLines>true</DrawVerticalGridLines>
  <EntriesPerDirection>1</EntriesPerDirection>
  <EntryHandling>AllEntries</EntryHandling>
  <ExitOnSessionClose>false</ExitOnSessionClose>
  <ExitOnSessionCloseSeconds>30</ExitOnSessionCloseSeconds>
  <FillLimitOrdersOnTouch>false</FillLimitOrdersOnTouch>
  <InputParameters />
  <IsTradingHoursBreakLineVisible>true</IsTradingHoursBreakLineVisible>
  <IsInstantiatedOnEachOptimizationIteration>true</IsInstantiatedOnEachOptimizationIteration>
  <MaximumBarsLookBack>Infinite</MaximumBarsLookBack>
  <MinimumBarsRequired>20</MinimumBarsRequired>
  <OrderFillResolution>Standard</OrderFillResolution>
  <OrderFillResolutionValue>1</OrderFillResolutionValue>
  <OrderFillResolutionType>Minute</OrderFillResolutionType>
  <OverlayOnPrice>false</OverlayOnPrice>
  <PaintPriceMarkers>true</PaintPriceMarkers>
  <PlotParameters />
  <RealTimeErrorHandling>StopCancelClose</RealTimeErrorHandling>
  <ScaleJustification>Right</ScaleJustification>
  <ScriptType>Strategy</ScriptType>
  <Slippage>0</Slippage>
  <StartBehavior>WaitUntilFlat</StartBehavior>
  <StopsAndTargets>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Stop loss</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>true</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Currency</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2024-07-13T16:31:37.3955915</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <Tag>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>Set Stop loss</StringValue>
            </NinjaScriptString>
          </Strings>
        </Tag>
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">StopLoss</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>StopLoss</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>StopLoss</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2024-07-13T16:31:57.8214158</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">StopLoss</LiveValue>
        </Value>
        <VariableDateTime>2024-07-13T16:31:37.3955915</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetStopLoss</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
          <string>isSimulatedStop</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Profit target</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Currency</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2024-07-13T16:32:04.6992505</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <Tag>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>Set Profit target</StringValue>
            </NinjaScriptString>
          </Strings>
        </Tag>
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">TakeProfit</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>TakeProfit</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>TakeProfit</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2024-07-13T16:32:09.9594225</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">TakeProfit</LiveValue>
        </Value>
        <VariableDateTime>2024-07-13T16:32:04.6992505</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetProfitTarget</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
        </Parameters>
      </Command>
    </WizardAction>
  </StopsAndTargets>
  <StopTargetHandling>PerEntryExecution</StopTargetHandling>
  <TimeInForce>Gtc</TimeInForce>
  <TraceOrders>false</TraceOrders>
  <UseOnAddTradeEvent>false</UseOnAddTradeEvent>
  <UseOnAuthorizeAccountEvent>false</UseOnAuthorizeAccountEvent>
  <UseAccountItemUpdate>false</UseAccountItemUpdate>
  <UseOnCalculatePerformanceValuesEvent>true</UseOnCalculatePerformanceValuesEvent>
  <UseOnConnectionEvent>false</UseOnConnectionEvent>
  <UseOnDataPointEvent>true</UseOnDataPointEvent>
  <UseOnFundamentalDataEvent>false</UseOnFundamentalDataEvent>
  <UseOnExecutionEvent>false</UseOnExecutionEvent>
  <UseOnMouseDown>true</UseOnMouseDown>
  <UseOnMouseMove>true</UseOnMouseMove>
  <UseOnMouseUp>true</UseOnMouseUp>
  <UseOnMarketDataEvent>false</UseOnMarketDataEvent>
  <UseOnMarketDepthEvent>false</UseOnMarketDepthEvent>
  <UseOnMergePerformanceMetricEvent>false</UseOnMergePerformanceMetricEvent>
  <UseOnNextDataPointEvent>true</UseOnNextDataPointEvent>
  <UseOnNextInstrumentEvent>true</UseOnNextInstrumentEvent>
  <UseOnOptimizeEvent>true</UseOnOptimizeEvent>
  <UseOnOrderUpdateEvent>false</UseOnOrderUpdateEvent>
  <UseOnPositionUpdateEvent>false</UseOnPositionUpdateEvent>
  <UseOnRenderEvent>true</UseOnRenderEvent>
  <UseOnRestoreValuesEvent>false</UseOnRestoreValuesEvent>
  <UseOnShareEvent>true</UseOnShareEvent>
  <UseOnWindowCreatedEvent>false</UseOnWindowCreatedEvent>
  <UseOnWindowDestroyedEvent>false</UseOnWindowDestroyedEvent>
  <Variables>
    <InputParameter>
      <Default>20</Default>
      <Name>StopLoss</Name>
      <Type>int</Type>
    </InputParameter>
    <InputParameter>
      <Default>20</Default>
      <Name>TakeProfit</Name>
      <Type>int</Type>
    </InputParameter>
  </Variables>
  <Name>TestOriginale</Name>
</ScriptProperties>
@*/
#endregion
