using NinjaTrader.Cbi;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript;


namespace NinjaTrader.Custom.CustomClasses
{
    internal class RecoverDataDaTestare
    {

        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia
        private NinjaTrader.NinjaScript.Strategies.DownloadData downloadData;
        private NinjaTrader.NinjaScript.Indicators.CandlestickPattern candlestickPattern;
        private string connectionString = "Server=MSI;Database=TestSP500;Integrated Security=True;";

        public void SaveBarMetrics(int tradeID, int gap, string tableName = "PriceBarMetrics")
        {
            if (downloadData == null)
            {
                downloadData.Print("Errore: downloadData non è stato inizializzato!");
                return;
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    string query = $@"INSERT INTO {tableName} (
                TradeID, Open, High, Low, Close,
                BodySize, UpperShadow, LowerShadow, BodyToWickRatio,
                ClosePositionInBar, GapOpen, DiffOC, DiffHL, DiffOO, DiffHH, DiffLL, DiffCC,
                BarRangeRatio, GapOpenLow, GapOpenHigh, CloseInsidePrevBar,
                PercentChange, PriceDirection, GapOfNext, PrevTrend3, NextDirection
            ) VALUES (
                @TradeID, @Open, @High, @Low, @Close,
                @BodySize, @UpperShadow, @LowerShadow, @BodyToWickRatio,
                @ClosePositionInBar, @GapOpen, @DiffOC, @DiffHL, @DiffOO, @DiffHH, @DiffLL, @DiffCC,
                @BarRangeRatio, @GapOpenLow, @GapOpenHigh, @CloseInsidePrevBar,
                @PercentChange, @PriceDirection, @GapOfNext, @PrevTrend3, @NextDirection
            )";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        // Prezzi grezzi
                        double open = downloadData.Open[1];
                        double high = downloadData.High[1];
                        double low = downloadData.Low[1];
                        double close = downloadData.Close[1];

                        // Body / Ombre
                        double bodySize = Math.Abs(close - open);
                        double upperShadow = high - Math.Max(open, close);
                        double lowerShadow = Math.Min(open, close) - low;
                        double bodyToWickRatio = (upperShadow + lowerShadow) > 0 ? bodySize / (upperShadow + lowerShadow) : 0;

                        // Posizione della chiusura
                        double closePositionInBar = (close - low) / (high - low);

                        // Gap e differenze
                        double gapOpen = open - downloadData.Close[2];
                        double diffOC = open - downloadData.Close[2];
                        double diffHL = (high - low) - (downloadData.High[2] - downloadData.Low[2]);
                        double diffOO = open - downloadData.Open[2];
                        double diffHH = high - downloadData.High[2];
                        double diffLL = low - downloadData.Low[2];
                        double diffCC = close - downloadData.Close[2];
                        double barRangeRatio = (high - low) / (downloadData.High[2] - downloadData.Low[2]);

                        double gapOpenLow = open - low;
                        double gapOpenHigh = high - open;

                        int closeInsidePrevBar = (close >= downloadData.Low[2] && close <= downloadData.High[2]) ? 1 : 0;

                        double percentChange = ((close - downloadData.Close[2]) / downloadData.Close[2]) * 100;
                        int priceDirection = close > open ? 1 : (close < open ? -1 : 0);

                        int prevTrend3 = downloadData.Close[2] > downloadData.Close[4] ? 1 : 0;
                        double nextDirection = (downloadData.Close[0] > downloadData.Open[0]) ? 1 : (downloadData.Close[0] < downloadData.Open[0]) ? 0 : 0.5;

                        // Parametri SQL
                        command.Parameters.AddWithValue("@TradeID", tradeID);
                        command.Parameters.AddWithValue("@Open", open);
                        command.Parameters.AddWithValue("@High", high);
                        command.Parameters.AddWithValue("@Low", low);
                        command.Parameters.AddWithValue("@Close", close);

                        command.Parameters.AddWithValue("@BodySize", bodySize);
                        command.Parameters.AddWithValue("@UpperShadow", upperShadow);
                        command.Parameters.AddWithValue("@LowerShadow", lowerShadow);
                        command.Parameters.AddWithValue("@BodyToWickRatio", bodyToWickRatio);
                        command.Parameters.AddWithValue("@ClosePositionInBar", closePositionInBar);

                        command.Parameters.AddWithValue("@GapOpen", gapOpen);
                        command.Parameters.AddWithValue("@DiffOC", diffOC);
                        command.Parameters.AddWithValue("@DiffHL", diffHL);
                        command.Parameters.AddWithValue("@DiffOO", diffOO);
                        command.Parameters.AddWithValue("@DiffHH", diffHH);
                        command.Parameters.AddWithValue("@DiffLL", diffLL);
                        command.Parameters.AddWithValue("@DiffCC", diffCC);
                        command.Parameters.AddWithValue("@BarRangeRatio", barRangeRatio);
                        command.Parameters.AddWithValue("@GapOpenLow", gapOpenLow);
                        command.Parameters.AddWithValue("@GapOpenHigh", gapOpenHigh);
                        command.Parameters.AddWithValue("@CloseInsidePrevBar", closeInsidePrevBar);

                        command.Parameters.AddWithValue("@PercentChange", percentChange);
                        command.Parameters.AddWithValue("@PriceDirection", priceDirection);
                        command.Parameters.AddWithValue("@GapOfNext", gap);
                        command.Parameters.AddWithValue("@PrevTrend3", prevTrend3);
                        command.Parameters.AddWithValue("@NextDirection", nextDirection);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    downloadData.Print($"❌ Errore nel salvataggio dei dati combinati: {ex.Message}");
                }
            }
        }


        //Salvare feature geometriche e alcune metriche di prezzo per una candela specifica.
        public void SaveDailyTrendCandlestickOriginale(int tradeID)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    //Log($"Salvo informazioni di trend", LogLevel.Information);

                    string query = @"INSERT INTO TrendDailyCandlestick (
                        TradeID, TrendDirection, 
                        StartTimestamp, EndTimestamp, 
                        TrendStartDate, TrendEndDate)
                        VALUES (
                        @TradeID, @TrendDirection, 
                        @StartTimestamp, @EndTimestamp, 
                        @TrendStartDate, @TrendEndDate)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        int trendDirection = 0;
                        int trendLength = 3;

                        bool isUptrend = true, isDowntrend = true;
                        for (int i = 1; i <= trendLength; i++)
                        {
                            if (!(downloadData.Open[i] > downloadData.Open[i + 1] && downloadData.Close[i] > downloadData.Close[i + 1]))
                                isUptrend = false;
                            if (!(downloadData.Open[i] < downloadData.Open[i + 1] && downloadData.Close[i] < downloadData.Close[i + 1]))
                                isDowntrend = false;
                        }

                        if (isUptrend) trendDirection = 1;
                        else if (isDowntrend) trendDirection = -1;

                        DateTime trendStart = downloadData.Times[downloadData.BarsInProgress][trendLength];
                        DateTime trendEnd = downloadData.Times[downloadData.BarsInProgress][1];
                        DateTime trendStartDate = trendStart.Date;
                        DateTime trendEndDate = trendEnd.Date;

                        long startTimestamp = ((DateTimeOffset)trendStart).ToUnixTimeSeconds();
                        long endTimestamp = ((DateTimeOffset)trendEnd).ToUnixTimeSeconds();

                        command.Parameters.AddWithValue("@TradeID", tradeID);
                        command.Parameters.AddWithValue("@TrendDirection", trendDirection);
                        command.Parameters.AddWithValue("@StartTimestamp", startTimestamp);
                        command.Parameters.AddWithValue("@EndTimestamp", endTimestamp);
                        command.Parameters.AddWithValue("@TrendStartDate", trendStartDate);
                        command.Parameters.AddWithValue("@TrendEndDate", trendEndDate);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    //Log($"❌ Errore nel salvataggio del trend: {ex.Message}", LogLevel.Error);
                }
            }
        }

    }
}
