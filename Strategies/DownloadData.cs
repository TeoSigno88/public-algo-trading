#region Using declarations
using System;
using System.Data.SqlClient;
using NinjaTrader.Cbi;
using NinjaTrader.Custom.CustomClasses;
using NinjaTrader.NinjaScript.Indicators;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Net.Http;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.CQG.ProtoBuf;
using static NinjaTrader.NinjaScript.Strategies.DownloadData;
using Infragistics.Windows.DataPresenter;
using NinjaTrader.Gui.PropertiesTest;
using NinjaTrader.Server;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Windows.Media;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class DownloadData : Strategy
    {
        private RecoverData recoverData;
        private CandlestickPattern candlestickPattern;
        private CandleStickPatternLogic patternLogic;
        private static readonly HttpClient client = new HttpClient();
        private int maxBarsToCheck = 0;

        private static bool isGAP;
        private static bool previousGAP;

        private struct GapAnalysis
        {
            public bool IsGapUp;
            public bool IsGapDown;
            public double FirstBarHigh;
            public double FirstBarLow;
            public int GapBarIndex;
        }

        private class PreviousTrend
        {
            public int BarIndex;
            public double Close;
            public double High;
            public double Low;
            public double Open;
            public double? Volume;
        }

        private class GapInfo
        {
            DateTime DateTime;
            public int TradeId;
            public int BarIndex;

            public double previousClose;
            public double open;
            public double high;
            public double low;
            public int GAPDirection;
            public double GAPVolatility;
            public double? volume;

            
           public List<PreviousTrend> PreviousTrend = new List<PreviousTrend>();
        }



        private Dictionary<int, GapAnalysis> gapAnalysisMap = new Dictionary<int, GapAnalysis>();
        //private const int maxBarsToCheck = 8;   //dinamicizzare (sono le 8 barre giornaliere su TF H1)
        private Dictionary<int, double> gapTracking = new Dictionary<int, double>();  // Index GAP -> Prezzo da chiudere
        private List<int> gapStartBars = new List<int>(); // Index iniziali dei GAP


        private List<GapInfo> gapInfo = new List<GapInfo>();


        private string connectionString = "Server=MSI;Database=TestSP500;Integrated Security=True;";


        private double sogliaGAP = 6;   //6 tick = 6 * 0.25 = 1.5 punti per S&P500
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"scaricare dati finanziari";
                Name = "DownloadData";
                Calculate = Calculate.OnBarClose;
                BarsRequiredToTrade = 2;
            }
            else if (State == State.Configure)
            {

                recoverData = new RecoverData(this);

                Log("strategia avviata!", LogLevel.Information);

            }


            if (State == State.Terminated)
            {
                Log($"Raccolta dati terminata", LogLevel.Information);
            }


        }


        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
            {
                return;
            }
                

            if (CheckFalseGap())
            {
                return;
            }
                

            //rilevo un nuovo GAP
            isGAP = Math.Abs(Open[0] - Close[1]) >= sogliaGAP;

            if (isGAP)
            {
                bool gapUp = Open[0] > Close[1];
                bool gapDown = Open[0] < Close[1];
                double gapClosePrice = Close[1];


                DateTime date = Time[1];
                int lastTradeId = recoverData.SavetradeId(date);
                var previousTrend = new List<PreviousTrend>();

                //suddivido il mercato in blocchi delimitati dai GAP
                CreateGAPBlock(previousTrend, lastTradeId, gapClosePrice);  //quando la lista è completa allora aggiungerla all'oggetto GapInfo

                foreach (var gapInfo in gapInfo)
                {
                   Log($"TradeId: {gapInfo.TradeId}, BarIndex: {gapInfo.BarIndex}, GapClosePrice: {gapInfo.previousClose}", LogLevel.Information);
                   Log($"BARRE PRECEDENTI", LogLevel.Information);

                    foreach (var previouTrend in gapInfo.PreviousTrend)
                    {
                        Log($"BarIndex: {previouTrend.BarIndex}", LogLevel.Information);
                        Log($"Close: {previouTrend.Close}", LogLevel.Information);
                        Log($"High: {previouTrend.High}", LogLevel.Information);
                        Log($"Low: {previouTrend.Low}", LogLevel.Information);
                        Log($"Open: {previouTrend.Open}", LogLevel.Information);
                    }


                    //importare le info di ogni blocco per TradeId in una tabella SQL
                   
                }

                if (gapUp)
                {
                    Draw.ArrowUp(this, "gapUp" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.Green);
                    Draw.Text(this, "gapUpText_" + lastTradeId, $"ID: {lastTradeId}", 0, Low[0] - TickSize * 4, Brushes.White);

                }
                else if (gapDown)
                {
                    Draw.ArrowDown(this, "gapDown" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);
                    Draw.Text(this, "gapDownText_" + lastTradeId, $"ID: {lastTradeId}", 0, High[0] + TickSize * 4, Brushes.White);

                }

                
                //salva il nuovo GAP
                gapTracking[CurrentBar] = gapClosePrice;
                gapStartBars.Add(CurrentBar);


                //GetDownloadData();
            }
            
        }


        //cerco il GAP precedente per identificare quante barre fa inizia il blocco
        public bool FindStartBlock(int barsAgo)
        {
            if (CurrentBar < barsAgo + 1)
                return false;

            double open = Open[barsAgo];
            double closePrev = Close[barsAgo + 1];
            double gap = Math.Abs(open - closePrev);


            return gap >= sogliaGAP;
        }



        //con questa funzione creo un blocco temporale delimitato tra un GAP e l'altro
        //nel quale racchiudo le info riguardanti il GAP e il trend precedente, per ogni barra
        //praticamente divido il mercato in blocchi di GAP

        //questo ritorna la lista delle barre precedenti al GAP

        private void CreateGAPBlock(List<PreviousTrend> previousTrend, int lastTradeId, double gapClosePrice)
        {
            //trovo quante barre fa c'è stato un GAP precedente così da sapere dove iizia il blocco
            int barsAgo = 1;
            while (!FindStartBlock(barsAgo))
            {
                if (CurrentBar < barsAgo + 1)
                    break;

                barsAgo++;
            }

            //aggiungo i dati delle barre precedenti
            for (int i = 0; i < barsAgo; i++)
            {
                int offset = i + 1;

                if (CurrentBar < offset)
                    break;

                previousTrend.Add(new PreviousTrend
                {
                    BarIndex = CurrentBar - offset,
                    Close = Close[offset],
                    High = High[offset],
                    Low = Low[offset],
                    Open = Open[offset]
                });
            }

            gapInfo.Add(new GapInfo
            {
                TradeId = lastTradeId,
                BarIndex = CurrentBar,
                previousClose = gapClosePrice,
                PreviousTrend = previousTrend
            });

            isGAP = false;
        }



        //se c'è stato un GAP
        //entro la prossima seduta (16:30 alle 22:00 - 8 barre H1)
        //il GAP tende alla chiusura oppure rimane aperto

        //definizione di tendenzza alla chiusura
        //GAP ribassista => durante la successiva contrattazione i prezzi superano il massimo della prima barra di apertura
        //GAP rialzista => durante la successiva contrattazioene i prezzi scendono sotto al minimo della prima barra di apertura




        // 1. Definisci il GAP
        //GAP = prezzo di apertura della sessione cash(15:30) – prezzo di chiusura del giorno precedente(22:00 futures)

        //Può essere gap up(apertura sopra chiusura) o gap down.

        //📍 2. Osserva le statistiche di riempimento del gap
        //Molti gap sull’S&P500 vengono riempiti(cioè il prezzo ritorna verso la chiusura precedente) entro le prime 1–2 ore.

        //Puoi raccogliere dati storici per calcolare:

        //Probabilità di riempimento

        //Tempo medio di riempimento

        //Gap threshold minimo(es.ignorare gap < 0.25%)

        //📍 3. Trading plan base per MES
        //Aspetto Esempio
        //Strumento MES(Micro E-mini S&P 500)
        //Orario Dalle 15:30 alle 16:30 (prima ora cash)
        //Setup Gap up/down > 0.3%
        //Ingresso Alla rottura di un livello intra-15 minuti
        //Target  Chiusura precedente(gap fill)
        //Stop-loss Es. 5–7 punti
        //Size	1–2 MES (massimo rischio ~$35–70 per contratto)

        //💡 Vantaggi dell’uso del MES
        //Puoi gestire la size con precisione e scalare in modo progressivo.

        //Ogni punto vale solo $5, quindi puoi rischiare il 2% (es. €100 su 5000) con stop-loss realistici.

        //Ottimo per backtest e per fare forward testing live senza esagerare con l’esposizione.

        //⚠️ Cose da monitorare
        //Spread e slippage nel primo minuto post apertura

        //Dati economici alle 14:30–15:30 CET che possono distorcere il comportamento normale del gap

        //Volume su MES, usa preferibilmente orari in cui il mercato è liquido

        //✅ Conclusione
        //Cosa vuoi fare  Puoi farlo col MES?	Note
        //Studiare gap tra chiusura e apertura S&P500 ✅	Basati su dati futures
        //Tradarlo su MES ✅	Liquido, economico, preciso
        //Rischiare <2% su ogni trade con €5 000	✅	1 contratto MES = $5/pt


        //aggiorno i dati
        private void UpdateGapTendenza(int gapBar, bool isTendenza)
        {
            // aggiorna tabella DB esistente o altra tabella `gap_followthrough`
            // trade_id = prendi da mappa se serve
        }



        private bool CheckFalseGap()
        {

            return Open[0] >= Low[1] && Open[0] <= High[1];
        }



        private void UpdateGapClosed(int tradeId, bool isClosed)
        {
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"UPDATE gap_matrix
                           SET isGapClosed = @isClosed
                           WHERE trade_id = @tradeId";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("isClosed", isClosed);
                        cmd.Parameters.AddWithValue("tradeId", tradeId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore aggiornando isGapClosed: {ex.Message}");
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





        


        //mantenerla
        public int CalculatePrePatternTrend(ISeries<double> close)
        {
            if (close.Count < 5)
            {
                return 0;
            }

            bool firstUp = close[4] < close[3];
            bool secondUp = close[3] < close[2];
            bool thirdUp = close[2] < close[1];


            if (thirdUp)
            {
                return 1;
            }

            bool firstDown = close[4] > close[3];
            bool secondDown = close[3] > close[2];
            bool thirdDown = close[2] > close[1];

            if (thirdDown)
            {
                return -1;
            }

            return 0;
        }
    }

}

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
