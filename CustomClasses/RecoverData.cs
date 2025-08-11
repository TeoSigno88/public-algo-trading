using NinjaTrader.Cbi;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Npgsql;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.SuperDomColumns;
using System.Data;
using static NinjaTrader.NinjaScript.Strategies.StrategyTester;


namespace NinjaTrader.Custom.CustomClasses
{
    internal class RecoverData
    {

        private NinjaTrader.NinjaScript.Strategies.TestOriginale strategyTestOriginale; //riferimento corretto alla strategia
        private NinjaTrader.NinjaScript.Strategies.DownloadData downloadData;
        private NinjaTrader.NinjaScript.Indicators.CandlestickPattern candlestickPattern;
        private NinjaTrader.NinjaScript.Strategies.StrategyTester strategyTester;

        public RecoverData(NinjaTrader.NinjaScript.Strategies.TestOriginale strategy)
        {
            this.strategyTestOriginale = strategy;
        }

        public RecoverData(NinjaTrader.NinjaScript.Strategies.DownloadData downloadData)
        {

            this.downloadData = downloadData;
        }

        public RecoverData(NinjaTrader.NinjaScript.Indicators.CandlestickPattern candlestickPattern)
        {
            this.candlestickPattern = candlestickPattern;
        }


        public RecoverData(NinjaTrader.NinjaScript.Strategies.StrategyTester strategyTester)
        {

            this.strategyTester = strategyTester;
        }
       
        //SQLServer
        private string connectionString = "Server=MSI;Database=TestSP500;Integrated Security=True;";
        //postgre
        //string connectionString = "Host=localhost;Port=5432;Username=postgres;Password=Pa55w0rd;Database=TradingDataTest";



        public int SavetradeId(DateTime date)
        {
            int tradeID = -1;

            using (SqlConnection connection = new SqlConnection(connectionString))
            //using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = @"INSERT INTO TradeId (Date) 
                                 OUTPUT INSERTED.TradeID 
                                 VALUES (@Date)";

                    //using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
                    using (SqlCommand command = new SqlCommand(query, connection))

                    {
                        command.Parameters.AddWithValue("@Date", date);
                        tradeID = (int)command.ExecuteScalar();
                    }
                }
                catch (Exception ex)
                {
                    downloadData.Print($"Errore nel salvataggio del trade: {ex.Message}");
                }
            }
            return tradeID;
        }

        //aggiorno ExitPrice, ExitDate e TradeResult in TradeResult
        public void TradeResults(int tradeID, DateTime enterDate, double enterPrice, DateTime exitDate, double exitPrice)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    string query = @"
                        IF EXISTS (SELECT 1 FROM TradeResult WHERE TradeID = @TradeID)
                        BEGIN
                            UPDATE TradeResult
                            SET EnterDate = @EnterDate,
                                EnterPrice = @EnterPrice,
                                ExitDate = @ExitDate, 
                                ExitPrice = @ExitPrice
                            WHERE TradeID = @TradeID
                        END
                        ELSE
                        BEGIN
                            INSERT INTO TradeResult (TradeID, EnterDate, EnterPrice, ExitDate, ExitPrice)
                            VALUES (@TradeID, @EnterDate, @EnterPrice, @ExitDate, @ExitPrice)
                        END";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EnterDate", enterDate);
                        command.Parameters.AddWithValue("@EnterPrice", enterPrice);
                        command.Parameters.AddWithValue("@ExitDate", exitDate);
                        command.Parameters.AddWithValue("@ExitPrice", exitPrice);
                        command.Parameters.AddWithValue("@TradeID", tradeID);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    strategyTestOriginale.Print($"Errore nell'aggiornamento di ExitPrice: {ex.Message}");
                }
            }
        }

        //salva i dati in StrategyData
        public void TestOriginaleStrategyData(int tradeID, DateTime time)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = @"INSERT INTO StrategyData 
                            (TradeID, DiffOC_0, DiffHL_0, DiffOO_1, DiffHH_1, DiffLL_1, DiffCC_1, 
                             PercentChange, PriceDirection, GenerateAt)
                            VALUES 
                            (@TradeID, @DiffOC_0, @DiffHL_0, @DiffOO_1, @DiffHH_1, @DiffLL_1, @DiffCC_1, 
                             @PercentChange, @PriceDirection, @GenerateAt)
                            ";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TradeID", tradeID);
                        command.Parameters.AddWithValue("@DiffOC_0", strategyTestOriginale.Open[0] - strategyTestOriginale.Close[0]);
                        command.Parameters.AddWithValue("@DiffHL_0", strategyTestOriginale.High[0] - strategyTestOriginale.Low[0]);
                        command.Parameters.AddWithValue("@DiffOO_1", strategyTestOriginale.Open[0] - strategyTestOriginale.Open[1]);
                        command.Parameters.AddWithValue("@DiffHH_1", strategyTestOriginale.High[0] - strategyTestOriginale.High[1]);
                        command.Parameters.AddWithValue("@DiffLL_1", strategyTestOriginale.Low[0] - strategyTestOriginale.Low[1]);
                        command.Parameters.AddWithValue("@DiffCC_1", strategyTestOriginale.Close[0] - strategyTestOriginale.Close[1]);

                        //percentuale di variazione rispetto alla barra precedente
                        double percentChange = ((strategyTestOriginale.Close[0] - strategyTestOriginale.Close[1]) / strategyTestOriginale.Close[1]) * 100;
                        command.Parameters.AddWithValue("@PercentChange", percentChange);

                        // Direzione della barra (1 = rialzista, -1 = ribassista, 0 = invariata)
                        int priceDirection = strategyTestOriginale.Close[0] > strategyTestOriginale.Open[0] ? 1 : (strategyTestOriginale.Close[0] < strategyTestOriginale.Open[0] ? -1 : 0);
                        command.Parameters.AddWithValue("@PriceDirection", priceDirection);

                        command.Parameters.AddWithValue("@GenerateAt", time);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    strategyTestOriginale.Print($"Errore nel salvataggio dei dati finanziari: {ex.Message}");
                }
            }
        }

        //aggiorna i risultati in RiskData
        public void UpdateRiskData(int tradeID)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = @"
                        IF EXISTS (SELECT 1 FROM RiskData WHERE TradeID = @TradeID)
                        BEGIN
                            UPDATE RiskData
                            SET FirstRiskCloser = @FirstRiskCloser,
                                SecondRiskCloser = @SecondRiskCloser,
                                TotalRiskCloser = @TotalRiskCloser
                            WHERE TradeID = @TradeID
                        END
                        ELSE
                        BEGIN
                            INSERT INTO RiskData (TradeID, FirstRiskCloser, SecondRiskCloser, TotalRiskCloser)
                            VALUES (@TradeID, @FirstRiskCloser, @SecondRiskCloser, @TotalRiskCloser)
                        END";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TradeID", tradeID);

                        bool first = strategyTestOriginale.FirstRiskClosed;
                        bool second = strategyTestOriginale.SecondRiskClosed;
                        bool total = strategyTestOriginale.TotalRiskClosed;

                        if (first) { second = false; total = false; }
                        else if (second) { first = false; total = false; }
                        else if (total) { first = false; second = false; }


                        command.Parameters.AddWithValue("@FirstRiskCloser", first);
                        command.Parameters.AddWithValue("@SecondRiskCloser", second);
                        command.Parameters.AddWithValue("@TotalRiskCloser", total);

                        int rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected == 0)
                        {
                            strategyTestOriginale.Print($"Nessun record aggiornato per TradeID: {tradeID}");
                        }
                        else
                        {
                            strategyTestOriginale.Print($"Dati aggiornati con successo per TradeID: {tradeID}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    strategyTestOriginale.Print($"Errore nell'aggiornamento dei dati finanziari: {ex.Message}");
                }
            }
        }

        public void SaveVolumes(int tradeID, DateTime date, double volume)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = @"INSERT INTO FinancialVolumes
                            (TradeID, Date, Mon, Tue, Wed, Thu, Fri, Sat, Sun, Volume)
                            VALUES 
                            (@TradeID, @Date, @Mon, @Tue, @Wed, @Thu, @Fri, @Sat, 
                             @Sun, @Volume)
                            ";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        int Mon = 0, Tue = 0, Wed = 0, Thu = 0, Fri = 0, Sat = 0, Sun = 0;

                        switch (date.DayOfWeek)
                        {
                            case DayOfWeek.Monday: Mon = 1; break;
                            case DayOfWeek.Tuesday: Tue = 2; break;
                            case DayOfWeek.Wednesday: Wed = 3; break;
                            case DayOfWeek.Thursday: Thu = 4; break;
                            case DayOfWeek.Friday: Fri = 5; break;
                            case DayOfWeek.Saturday: Sat = 6; break;
                            case DayOfWeek.Sunday: Sun = 7; break;
                        }

                        command.Parameters.AddWithValue("@TradeID", tradeID);
                        command.Parameters.AddWithValue("@Date", date);
                        command.Parameters.AddWithValue("@Volume", volume);

                        command.Parameters.AddWithValue("@Mon", Mon);
                        command.Parameters.AddWithValue("@Tue", Tue);
                        command.Parameters.AddWithValue("@Wed", Wed);
                        command.Parameters.AddWithValue("@Thu", Thu);
                        command.Parameters.AddWithValue("@Fri", Fri);
                        command.Parameters.AddWithValue("@Sat", Sat);
                        command.Parameters.AddWithValue("@Sun", Sun);
                        


                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    strategyTestOriginale.Print($"Errore nel salvataggio dei dati finanziari: {ex.Message}");
                }
            }
        }

        //salva metriche derivate per comprendere la dinamica del prezzo tra barre e contesto di mercato, cerco pattern
        public void SavePriceActionMetrics(int tradeID, int gap)
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
                    string query = @"INSERT INTO DirectionalActionMetrics 
                    (TradeID, DiffOC, DiffHL, DiffOO, DiffHH, DiffLL, DiffCC, 
                     PercentChange, BarRangeRatio, GapOpen, GapOpenLow, GapOpenHigh, 
                     ClosePositionInBar, BodyToWickRatio,
                     RealBodyRatio, Momentum3, Engulfing, InsideBar,
                     SMA10, EMA10, BBUpper, BBLower, BBPosition, MACD, MACDSignal, MACDHist, RSI14, PriceDirection)
                    VALUES 
                    (@TradeID, @DiffOC, @DiffHL, @DiffOO, @DiffHH, @DiffLL, @DiffCC, 
                     @PercentChange, @BarRangeRatio, @GapOpen, @GapOpenLow, @GapOpenHigh, 
                     @ClosePositionInBar, @BodyToWickRatio,
                     @RealBodyRatio, @Momentum3, @Engulfing, @InsideBar,
                     @SMA10, @EMA10, @BBUpper, @BBLower, @BBPosition, @MACD, @MACDSignal, @MACDHist, @RSI14, @PriceDirection)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {

                        
                        command.Parameters.AddWithValue("@TradeID", tradeID);

                        ////differenze tra Barre
                        command.Parameters.AddWithValue("@DiffOC", downloadData.Open[1] - downloadData.Close[1]);
                        command.Parameters.AddWithValue("@DiffHL", downloadData.High[1] - downloadData.Low[1]);
                        command.Parameters.AddWithValue("@DiffOO", downloadData.Open[1] - downloadData.Open[2]);
                        command.Parameters.AddWithValue("@DiffHH", downloadData.High[1] - downloadData.High[2]);
                        command.Parameters.AddWithValue("@DiffLL", downloadData.Low[1] - downloadData.Low[2]);
                        command.Parameters.AddWithValue("@DiffCC", downloadData.Close[1] - downloadData.Close[2]);



                        //direzione e Momentum
                        //direzione della barra (1 = rialzista, -1 = ribassista, 0 = invariata)
                        int priceDirection = downloadData.Close[1] > downloadData.Open[1] ? 1 : (downloadData.Close[1] < downloadData.Open[1] ? -1 : 0);
                        command.Parameters.AddWithValue("@PriceDirection", priceDirection);

                        //momentum della chiusura rispetto al range della barra (dove si trova la chiusura attuale rispetto alla barra)
                        double closePositionInBar = (downloadData.Close[1] - downloadData.Low[1]) / (downloadData.High[1] - downloadData.Low[1]);
                        command.Parameters.AddWithValue("@ClosePositionInBar", closePositionInBar);

                        //direzione della barra successiva
                        double nextDirection = (downloadData.Close[0] < downloadData.Open[0]) ? 0 : (downloadData.Close[0] > downloadData.Open[0] ? 1 : 0.5);
                        command.Parameters.AddWithValue("@NextDirection", nextDirection);



                        //range e volatilità
                        //ampiezza della barra attuale rispetto alla precedente (quanto la candela attuale è più grande o più piccola rispetto alla precedente)
                        double previousRange = downloadData.High[2] - downloadData.Low[2];
                        double barRangeRatio = previousRange != 0 ? (downloadData.High[1] - downloadData.Low[1]) / previousRange : 0;
                        command.Parameters.AddWithValue("@BarRangeRatio", barRangeRatio);

                        //rapporto tra corpo e ombra della candela (misura se il corpo è più grande o più piccolo delle ombre)
                        double bodySize = Math.Abs(downloadData.Close[1] - downloadData.Open[1]);
                        double upperShadow = downloadData.High[1] - Math.Max(downloadData.Open[1], downloadData.Close[1]);
                        double lowerShadow = Math.Min(downloadData.Open[1], downloadData.Close[1]) - downloadData.Low[1];
                        double bodyToWickRatio = (upperShadow + lowerShadow) > 0 ? bodySize / (upperShadow + lowerShadow) : 0;
                        command.Parameters.AddWithValue("@BodyToWickRatio", bodyToWickRatio);

                        //percentuale di variazione rispetto alla barra precedente
                        double percentChange = ((downloadData.Close[1] - downloadData.Close[2]) / downloadData.Close[2]) * 100;
                        command.Parameters.AddWithValue("@PercentChange", percentChange);



                        //gap e discontinuità
                        //gap di apertura rispetto alla barra precedente (se il mercato ha aperto sopra o sotto la chiusura precedente)
                        double gapOpen = downloadData.Open[1] - downloadData.Close[2];
                        command.Parameters.AddWithValue("@GapOpen", gapOpen);

                        //gap tra aperura e minimo/massimo della stessa barra (volatilità iniziale della barra)
                        double gapOpenLow = downloadData.Open[1] - downloadData.Low[1];
                        double gapOpenHigh = downloadData.High[1] - downloadData.Open[1];
                        command.Parameters.AddWithValue("@GapOpenLow", gapOpenLow);
                        command.Parameters.AddWithValue("@GapOpenHigh", gapOpenHigh);

                        //TODO sistemare gap
                        int gapOfNext = gap;
                        command.Parameters.AddWithValue("@GapOfNext", gapOfNext);



                        //pattern e relazioni con la candela precedente
                        //chiusura dentro il range della barra precedente (ses il prezzo di chiusura attuale è dentro il range della candela precedente)
                        int closeInsidePrevBar = (downloadData.Close[1] >= downloadData.Low[2] && downloadData.Close[1] <= downloadData.High[2]) ? 1 : 0;
                        command.Parameters.AddWithValue("@CloseInsidePrevBar", closeInsidePrevBar);


                       
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    downloadData.Print($"Errore nel salvataggio dei dati finanziari: {ex.Message}");
                }
            }
        }

        public void SaveFeatures(int tradeID, DateTime date)
        {

            if (downloadData == null)
            {

                downloadData.LogError($"downloadData null");
                //downloadData.Print("Errore: SavePricDirectionalActionMetrics non è stato inizializzato!");
                return;
            }

            downloadData.LogInfo("ok");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = @"INSERT INTO Features
                    (TradeID, DateTime, Weekday, [Open], [High], [Low], [Close], Volume, DiffOC, DiffHL, DiffOO, DiffHH, DiffLL, DiffCC, 
                     PercentChange, BarRangeRatio, GapOpen, GapOpenLow, GapOpenHigh, 
                     ClosePositionInBar, BodyToWickRatio,
                     RealBodyRatio, VolatilityRatio, Volatility20, CloseSlope_delta, Momentum3, Engulfing, InsideBar,
                     SMA10, EMA10, BBWidth, BBUpper, BBLower, BBPosition, MACD, MACDSignal, MACDHist, RSI14,
                     CloseSlope, OpenSlope, HighSlope, LowSlope, IsGapPresent, GapValue, GapDirection, PriceDirection)
                    VALUES 
                    (@TradeID, @DateTime, @Weekday, @Open, @High, @Low, @Close, @Volume, @DiffOC, @DiffHL, @DiffOO, @DiffHH, @DiffLL, @DiffCC, 
                     @PercentChange, @BarRangeRatio, @GapOpen, @GapOpenLow, @GapOpenHigh, 
                     @ClosePositionInBar, @BodyToWickRatio,
                     @RealBodyRatio, @VolatilityRatio, @Volatility20, @CloseSlope_delta, @Momentum3, @Engulfing, @InsideBar,
                     @SMA10, @EMA10, @BBWidth, @BBUpper, @BBLower, @BBPosition, @MACD, @MACDSignal, @MACDHist, @RSI14,
                     @CloseSlope, @OpenSlope, @HighSlope, @LowSlope, @IsGapPresent, @GapValue, @GapDirection, @PriceDirection)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TradeID", tradeID);

                        string weekday = date.ToString("dddd", CultureInfo.InvariantCulture);
                        command.Parameters.AddWithValue("@Weekday", weekday);
                        command.Parameters.AddWithValue("@DateTime", date);

                        command.Parameters.AddWithValue("@Close", downloadData.Close[1]);
                        command.Parameters.AddWithValue("@Open", downloadData.Open[1]);
                        command.Parameters.AddWithValue("@High", downloadData.High[1]);
                        command.Parameters.AddWithValue("@Low", downloadData.Low[1]);
                        command.Parameters.AddWithValue("@Volume", downloadData.Volume[0]);
                        
                        command.Parameters.AddWithValue("@DiffOC", downloadData.Open[1] - downloadData.Close[1]);
                        command.Parameters.AddWithValue("@DiffHL", downloadData.High[1] - downloadData.Low[1]);
                        command.Parameters.AddWithValue("@DiffOO", downloadData.Open[1] - downloadData.Open[2]);
                        command.Parameters.AddWithValue("@DiffHH", downloadData.High[1] - downloadData.High[2]);
                        command.Parameters.AddWithValue("@DiffLL", downloadData.Low[1] - downloadData.Low[2]);
                        command.Parameters.AddWithValue("@DiffCC", downloadData.Close[1] - downloadData.Close[2]);

                        double percentChange = ((downloadData.Close[1] - downloadData.Close[2]) / downloadData.Close[2]) * 100;
                        command.Parameters.AddWithValue("@PercentChange", percentChange);

                        double previousRange = downloadData.High[2] - downloadData.Low[2];
                        double barRangeRatio = previousRange != 0 ? (downloadData.High[1] - downloadData.Low[1]) / previousRange : 0;

                        command.Parameters.AddWithValue("@BarRangeRatio", barRangeRatio);

                        double gapOpen = downloadData.Open[1] - downloadData.Close[2];
                        command.Parameters.AddWithValue("@GapOpen", gapOpen);
                        command.Parameters.AddWithValue("@GapOpenLow", downloadData.Open[1] - downloadData.Low[1]);
                        command.Parameters.AddWithValue("@GapOpenHigh", downloadData.High[1] - downloadData.Open[1]);

                        double barRange = downloadData.High[1] - downloadData.Low[1];
                        double closePositionInBar = barRange != 0 ? (downloadData.Close[1] - downloadData.Low[1]) / barRange : 0;
                        command.Parameters.AddWithValue("@ClosePositionInBar", closePositionInBar);

                        double bodySize = Math.Abs(downloadData.Close[1] - downloadData.Open[1]);
                        double upperShadow = downloadData.High[1] - Math.Max(downloadData.Open[1], downloadData.Close[1]);
                        double lowerShadow = Math.Min(downloadData.Open[1], downloadData.Close[1]) - downloadData.Low[1];
                        double bodyToWickRatio = (upperShadow + lowerShadow) > 0 ? bodySize / (upperShadow + lowerShadow) : 0;
                        command.Parameters.AddWithValue("@BodyToWickRatio", bodyToWickRatio);

                        double range = downloadData.High[1] - downloadData.Low[1];
                        double realBodyRatio = range > 0 ? bodySize / range : 0;
                        command.Parameters.AddWithValue("@RealBodyRatio", realBodyRatio);

                        //poco utile per la direzionalità
                        double prevRange = downloadData.High[2] - downloadData.Low[2];
                        double volatilityRatio = prevRange > 0 ? range / prevRange : 0;
                        command.Parameters.AddWithValue("@VolatilityRatio", volatilityRatio);

                        //deviazione standard mobile delle chiusure, su 20 barre
                        double sumClose = 0, sumCloseSq = 0;
                        for (int i = 1; i <= 20; i++)
                        {
                            sumClose += downloadData.Close[i];
                            sumCloseSq += downloadData.Close[i] * downloadData.Close[i];
                        }
                        double meanClose = sumClose / 20;
                        double variance = (sumCloseSq / 20) - (meanClose * meanClose);
                        double volatility20 = Math.Sqrt(variance);
                        command.Parameters.AddWithValue("@Volatility20", volatility20);


                        //variazione dell'inclinazione tra due barre precedenti
                        double slope_1 = Math.Atan((downloadData.Close[1] - downloadData.Close[2]) / 1.0);
                        double slope_2 = Math.Atan((downloadData.Close[2] - downloadData.Close[3]) / 1.0);
                        double closeSlopeDelta = slope_1 - slope_2;
                        command.Parameters.AddWithValue("@CloseSlope_delta", closeSlopeDelta);


                        double momentum3 = downloadData.Close[1] - downloadData.Close[4];
                        command.Parameters.AddWithValue("@Momentum3", momentum3);

                        int engulfing = (downloadData.Open[1] < downloadData.Close[2] && downloadData.Close[1] > downloadData.Open[2]) ? 1 : 0;
                        command.Parameters.AddWithValue("@Engulfing", engulfing);

                        int insideBar = (downloadData.High[1] <= downloadData.High[2] && downloadData.Low[1] >= downloadData.Low[2]) ? 1 : 0;
                        command.Parameters.AddWithValue("@InsideBar", insideBar);

                        // SMA(10)
                        double sma10 = 0;
                        for (int i = 1; i <= 10; i++)
                            sma10 += downloadData.Close[i];
                        sma10 /= 10;

                        // EMA(10) con inizializzazione a SMA(10)
                        double alpha = 2.0 / (10 + 1);
                        double ema10 = sma10;
                        for (int i = 1; i <= 10; i++) // giusto: avanti nel tempo
                            ema10 = alpha * downloadData.Close[i] + (1 - alpha) * ema10;
                        command.Parameters.AddWithValue("@SMA10", sma10);
                        command.Parameters.AddWithValue("@EMA10", ema10);


                        // Bollinger Bands 20
                        double sum = 0, sumSq = 0;
                        for (int i = 1; i <= 20; i++)
                        {
                            sum += downloadData.Close[i];
                            sumSq += downloadData.Close[i] * downloadData.Close[i];
                        }
                        double mean = sum / 20;
                        double stdDev = Math.Sqrt((sumSq / 20) - (mean * mean));
                        double upperBand = mean + 2 * stdDev;
                        double lowerBand = mean - 2 * stdDev;
                        double bandWidth = upperBand - lowerBand;
                        double positionInBand = (downloadData.Close[1] - lowerBand) / (upperBand - lowerBand);
                        command.Parameters.AddWithValue("@BBUpper", upperBand);
                        command.Parameters.AddWithValue("@BBLower", lowerBand);
                        command.Parameters.AddWithValue("@BBPosition", positionInBand);
                        //più utile per la volatilità
                        command.Parameters.AddWithValue("@BBWidth", bandWidth);

                        // MACD
                        // EMA 12 e 26 iniziali con SMA come punto di partenza
                        double ema12 = 0, ema26 = 0;
                        for (int i = 1; i <= 12; i++) ema12 += downloadData.Close[i];
                        for (int i = 1; i <= 26; i++) ema26 += downloadData.Close[i];
                        ema12 /= 12;
                        ema26 /= 26;

                        double alpha12 = 2.0 / (12 + 1);
                        double alpha26 = 2.0 / (26 + 1);

                        // Calcola EMA12 e EMA26 finali su 26 periodi
                        for (int i = 1; i <= 26; i++)
                        {
                            ema12 = alpha12 * downloadData.Close[i] + (1 - alpha12) * ema12;
                            ema26 = alpha26 * downloadData.Close[i] + (1 - alpha26) * ema26;
                        }

                        double macd = ema12 - ema26;

                        // Calcola la signal line (EMA 9 del MACD)
                        // Calcola la MACD series (9 valori)
                        double[] macdSeries = new double[9];

                        for (int k = 0; k < 9; k++)
                        {
                            // Calcola EMA12 e EMA26 iniziali con SMA su finestra mobile
                            double e12 = 0, e26 = 0;

                            for (int i = 1 + k; i <= 12 + k; i++)
                                e12 += downloadData.Close[i];
                            for (int i = 1 + k; i <= 26 + k; i++)
                                e26 += downloadData.Close[i];

                            e12 /= 12;
                            e26 /= 26;

                            // Applica EMA12 e EMA26 dalla più vecchia alla più recente
                            for (int i = 1 + k; i <= 26 + k; i++)
                            {
                                e12 = alpha12 * downloadData.Close[i] + (1 - alpha12) * e12;
                                e26 = alpha26 * downloadData.Close[i] + (1 - alpha26) * e26;
                            }

                            macdSeries[k] = e12 - e26;
                        }

                        // Calcolo della Signal Line come EMA(9) della MACD series
                        double alphaSignal = 2.0 / (9 + 1);
                        double signal = macdSeries[0];

                        for (int i = 1; i < 9; i++)
                        {
                            signal = alphaSignal * macdSeries[i] + (1 - alphaSignal) * signal;
                        }


                        double histogram = macd - signal;

                        command.Parameters.AddWithValue("@MACD", macd);
                        command.Parameters.AddWithValue("@MACDSignal", signal);
                        command.Parameters.AddWithValue("@MACDHist", histogram);

                        // RSI(14)
                        double gain = 0, loss = 0;
                        double rs = 0;

                        for (int i = 1; i <= 14; i++)
                        {
                            double change = downloadData.Close[i] - downloadData.Close[i + 1];
                            if (change > 0)
                                gain += change;
                            else
                                loss -= change; // attenzione: loss deve restare positivo
                        }

                        if (loss == 0 && gain == 0)
                            rs = 1;  // per ottenere RSI = 50
                        else if (loss == 0)
                            rs = 1000; // simula RSI ≈ 100
                        else
                            rs = gain / loss;
                        double rsi14 = 100 - (100 / (1 + rs));
                        command.Parameters.AddWithValue("@RSI14", rsi14);

                        //inclinazione in radianti
                        double closeSlope = Math.Atan((downloadData.Close[1] - downloadData.Close[2]) / 1.0);
                        double openSlope = Math.Atan((downloadData.Open[1] - downloadData.Open[2]) / 1.0);
                        double highSlope = Math.Atan((downloadData.High[1] - downloadData.High[2]) / 1.0);
                        double lowSlope = Math.Atan((downloadData.Low[1] - downloadData.Low[2]) / 1.0);

                        command.Parameters.AddWithValue("@CloseSlope", closeSlope);
                        command.Parameters.AddWithValue("@OpenSlope", openSlope);
                        command.Parameters.AddWithValue("@HighSlope", highSlope);
                        command.Parameters.AddWithValue("@LowSlope", lowSlope);

                        double gapValue = downloadData.Open[1] - downloadData.Close[2];
                        int isGapPresent = Math.Abs(gapValue) >= 2.0 ? 1 : 0; // soglia di 1 punto
                        int gapDirection = (isGapPresent == 1) ? (gapValue > 0 ? 1 : (gapValue < 0 ? -1 : 0)) : 0;

                        command.Parameters.AddWithValue("@GapValue", gapValue);
                        command.Parameters.AddWithValue("@IsGapPresent", isGapPresent);
                        command.Parameters.AddWithValue("@GapDirection", gapDirection);




                        //chiusura attuale (y)
                        //PriceDirection è la direzione della candela attuale, che sarebbe il target futuro rispetto alle features che guardano i dati un candela indietro
                        int priceDirection = downloadData.Close[1] > downloadData.Close[2] ? 1 : downloadData.Close[1] < downloadData.Close[2] ? 0 : 2;
                        command.Parameters.AddWithValue("@PriceDirection", priceDirection);


                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    downloadData.Print($"Errore nel salvataggio dei dati finanziari: {ex.Message}");
                }
            }
        }

        //controllare CalculatePrePatternTrend
        //salva i pattern candlestick
        public void SaveCandlestickPattern(int tradeID, List<ChartPattern> foundPatterns)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    string query = @"INSERT INTO CandlestickPattern 
                        (TradeID, BearishBeltHold, BearishEngulfing, BearishHarami, BearishHaramiCross,
                         BullishBeltHold, BullishEngulfing, BullishHarami, BullishHaramiCross,
                         DarkCloudCover, Doji, DownsideTasukiGap, EveningStar, FallingThreeMethods,
                         Hammer, HangingMan, InvertedHammer, MorningStar, PiercingLine,
                         RisingThreeMethods, ShootingStar, StickSandwich, ThreeBlackCrows,
                         ThreeWhiteSoldiers, UpsideGapTwoCrows, UpsideTasukiGap, PrePatternTrend, NextDirection)
                        VALUES 
                        (@TradeID, @BearishBeltHold, @BearishEngulfing, @BearishHarami, @BearishHaramiCross,
                         @BullishBeltHold, @BullishEngulfing, @BullishHarami, @BullishHaramiCross,
                         @DarkCloudCover, @Doji, @DownsideTasukiGap, @EveningStar, @FallingThreeMethods,
                         @Hammer, @HangingMan, @InvertedHammer, @MorningStar, @PiercingLine,
                         @RisingThreeMethods, @ShootingStar, @StickSandwich, @ThreeBlackCrows,
                         @ThreeWhiteSoldiers, @UpsideGapTwoCrows, @UpsideTasukiGap, @PrePatternTrend, @NextDirection)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TradeID", tradeID);

                        foreach (ChartPattern pattern in Enum.GetValues(typeof(ChartPattern)))
                        {
                            int value = foundPatterns.Contains(pattern) ? 1 : 0;
                            command.Parameters.AddWithValue("@" + pattern.ToString(), value);
                        }

                        int trend = downloadData.CalculatePrePatternTrend(downloadData.Close);
                        command.Parameters.AddWithValue("@PrePatternTrend", trend);

                        //calcolo direzione successiva
                        int nextDirection = (downloadData.Close[1] < downloadData.Close[0]) ? 1 :
                                            (downloadData.Close[1] > downloadData.Close[0]) ? -1 : 0;
                        command.Parameters.AddWithValue("@NextDirection", nextDirection);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    downloadData.Print($"❌ Errore nel salvataggio dei pattern candlestick: {ex.Message}");
                }
            }
        }

        public void DownloadGAPMatrix(int tradeID)
        {
            try
            {
                // Informazioni GAP
                double prevClose = downloadData.Close[1];
                double currOpen = downloadData.Open[0];
                double gap = currOpen - prevClose;
                int gapDirection = gap > 0 ? 1 : 0;

                DateTime timestamp = downloadData.Time[0];
                string dataEU = timestamp.ToString("dd/MM/yyyy");
                string orario = timestamp.ToString("HH:mm:ss");
                string giornoSettimana = timestamp.DayOfWeek.ToString();

                int trendLength = 5;
                double startPrice = downloadData.Close[trendLength];
                double endPrice = downloadData.Close[1];
                int trendBackDirection = endPrice > startPrice ? 1 : 0;

                double volatilitaPrecedente = downloadData.High[1] - downloadData.Low[1];
                double volumePrecedente = downloadData.Volume[1];

                double mediaVolumeNBarre = 0.0;
                for (int i = 1; i <= trendLength; i++)
                    mediaVolumeNBarre += downloadData.Volume[i];
                mediaVolumeNBarre /= trendLength;

                // 📊 Analisi barre successive (1, 2, 3)
                int numBarre = 3;
                int[] direzioni = new int[numBarre];
                double[] volatilita = new double[numBarre];

                for (int i = 1; i <= numBarre; i++)
                {
                    double open = downloadData.Open[i];
                    double close = downloadData.Close[i];
                    double high = downloadData.High[i];
                    double low = downloadData.Low[i];

                    direzioni[i - 1] = close > open ? 1 : 0;
                    volatilita[i - 1] = high - low;
                }

                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO gap_matrix (
                trade_id, data, orario, giorno_settimana,
                close_precedente, open_attuale,
                gap_direction, gap_ampiezza, 
                trend_precedente,
                volatilita_precedente,
                volume_precedente,
                volume_medio_Nbarre,
                barra1_direzione, barra1_volatilita,
                barra2_direzione, barra2_volatilita,
                barra3_direzione, barra3_volatilita
            ) VALUES (
                @trade_id, @data, @orario, @giorno_settimana,
                @close_precedente, @open_attuale,
                @gap_direction, @gap_ampiezza,
                @trend_precedente,
                @volatilita_precedente,
                @volume_precedente,
                @volume_medio_Nbarre,
                @barra1_direzione, @barra1_volatilita,
                @barra2_direzione, @barra2_volatilita,
                @barra3_direzione, @barra3_volatilita
            )";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("trade_id", tradeID);
                        cmd.Parameters.AddWithValue("data", dataEU);
                        cmd.Parameters.AddWithValue("orario", orario);
                        cmd.Parameters.AddWithValue("giorno_settimana", giornoSettimana);
                        cmd.Parameters.AddWithValue("close_precedente", prevClose);
                        cmd.Parameters.AddWithValue("open_attuale", currOpen);
                        cmd.Parameters.AddWithValue("gap_direction", gapDirection);
                        cmd.Parameters.AddWithValue("gap_ampiezza", Math.Abs(gap));
                        cmd.Parameters.AddWithValue("trend_precedente", trendBackDirection);
                        cmd.Parameters.AddWithValue("volatilita_precedente", volatilitaPrecedente);
                        cmd.Parameters.AddWithValue("volume_precedente", volumePrecedente);
                        cmd.Parameters.AddWithValue("volume_medio_Nbarre", mediaVolumeNBarre);
                        cmd.Parameters.AddWithValue("barra1_direzione", direzioni[0]);
                        cmd.Parameters.AddWithValue("barra1_volatilita", volatilita[0]);
                        cmd.Parameters.AddWithValue("barra2_direzione", direzioni[1]);
                        cmd.Parameters.AddWithValue("barra2_volatilita", volatilita[1]);
                        cmd.Parameters.AddWithValue("barra3_direzione", direzioni[2]);
                        cmd.Parameters.AddWithValue("barra3_volatilita", volatilita[2]);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                downloadData.LogError($"❌ Errore in DownloadGAPMatrix: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void DownloadGAPMatrixOriginal(int tradeID)
        {
            try
            {
                // GAP: apertura attuale rispetto a chiusura precedente
                double prevClose = downloadData.Close[1];
                double currOpen = downloadData.Open[0];
                double gap = currOpen - prevClose;

                // Direzione del GAP
                int gapDirection = gap > 0 ? 1 : 0;

                // Timestamp e info temporali
                DateTime timestamp = downloadData.Time[0];
                string dataEU = timestamp.ToString("dd/MM/yyyy");
                string orario = timestamp.ToString("HH:mm:ss");
                string giornoSettimana = timestamp.DayOfWeek.ToString();

                // Direzione trend N barre precedenti (inclusa ultima)
                int trendLength = 5;
                double startPrice = downloadData.Close[trendLength];
                double endPrice = downloadData.Close[1];
                int trendBackDirection = endPrice > startPrice ? 1 : 0;

                // Volatilità barra precedente
                double volatilitaPrecedente = downloadData.High[1] - downloadData.Low[1];

                // Volume barra prima del GAP
                double volumePrecedente = downloadData.Volume[1];
                double mediaVolumeNBarre = 0.0;
                for (int i = 1; i <= trendLength; i++)
                    mediaVolumeNBarre += downloadData.Volume[i];
                mediaVolumeNBarre /= trendLength;

                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO gap_matrix (
                        trade_id, data, orario, giorno_settimana,
                        close_precedente, open_attuale,
                        gap_direction, gap_ampiezza, 
                        trend_precedente,
                        volatilita_precedente,
                        volume_precedente,
                        volume_medio_Nbarre
                    ) VALUES (
                        @trade_id, @data, @orario, @giorno_settimana,
                        @close_precedente, @open_attuale,
                        @gap_direction, @gap_ampiezza,
                        @trend_precedente,
                        @volatilita_precedente,
                        @volume_precedente,
                        @volume_medio_Nbarre
                    )";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("trade_id", tradeID);
                        cmd.Parameters.AddWithValue("data", dataEU);
                        cmd.Parameters.AddWithValue("orario", orario);
                        cmd.Parameters.AddWithValue("giorno_settimana", giornoSettimana);
                        cmd.Parameters.AddWithValue("close_precedente", prevClose);
                        cmd.Parameters.AddWithValue("open_attuale", currOpen);
                        cmd.Parameters.AddWithValue("gap_direction", gapDirection);
                        cmd.Parameters.AddWithValue("gap_ampiezza", Math.Abs(gap));
                        cmd.Parameters.AddWithValue("trend_precedente", trendBackDirection);
                        cmd.Parameters.AddWithValue("volatilita_precedente", volatilitaPrecedente);
                        cmd.Parameters.AddWithValue("volume_precedente", volumePrecedente);
                        cmd.Parameters.AddWithValue("volume_medio_Nbarre", mediaVolumeNBarre);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                downloadData.LogError($"❌ Errore in DownloadGAPMatrix: {ex.Message}\n{ex.StackTrace}");
            }
        }



        //separare le variabili in base agli orari (pre-market, regular, after-hours e overnight)
        //lo scopo è crrcare se ci sono correlazioni trale features precedenti e l'ombra inferiore e superiore della barra successiva
        //dividere anche in base al tipo di candela precedente (se rialzista o ribassista)
        public void DownloadCandleInfo(Candle candle, SMA volumeSMA)
        {
            try
            {
                DateTime time = candle.Time;
                var current = candle;

                // === Feature base ===
                string session = GetSessionLabel(current.Time);
                string prevDir = current.Close > current.Open ? "Bullish" : "Bearish";
                double bodyRange = Math.Abs(current.Close - current.Open);
                double totalRange = current.High - current.Low;
                double previousClose = current.Close; // equivalente a Close[1]
                double openGap = current.Open - previousClose;
                double volatility = (totalRange / previousClose) * 100.0;
                double bodyToRangeRatio = bodyRange / totalRange;
                double upperShadow = Math.Max(current.High - current.Close, current.High - current.Open);
                double lowerShadowFull = Math.Min(current.Open, current.Close) - current.Low;

                // === Posizione relativa ===
                double closePositionInRange = (current.Close - current.Low) / (current.High - current.Low);
                double openPositionInRange = (current.Open - current.Low) / (current.High - current.Low);
                double bodyCenter = (current.Open + current.Close) / 2;
                double bodyCenterInRange = (bodyCenter - current.Low) / (current.High - current.Low);
                double upperToLowerShadowRatio = upperShadow / (lowerShadowFull + 0.0001);
                double shadowDominance = (upperShadow + lowerShadowFull) / totalRange;

                // === Volume e pattern ===
                double volumeSpike = current.Volume / volumeSMA[1];
                bool isDoji = bodyRange < (0.1 * totalRange);
                bool isMarubozu = upperShadow < 0.01 && lowerShadowFull < 0.01;
                int hour = current.Time.Hour;
                int minute = current.Time.Minute;
                int dayOfWeek = (int)current.Time.DayOfWeek;


                // Feature di trend basate sulle barre precedenti
                double momentum3 = strategyTester.Close[1] - strategyTester.Close[4];                         // Momentum su 3 barre
                double avgBody3 = (Math.Abs(strategyTester.Close[1] - strategyTester.Open[1]) + Math.Abs(strategyTester.Close[2] - strategyTester.Open[2]) + Math.Abs(strategyTester.Close[3] - strategyTester.Open[3])) / 3.0;
                double avgVolume3 = (strategyTester.Volume[1] + strategyTester.Volume[2] + strategyTester.Volume[3]) / 3.0;
                double rollingVolatility3 = (strategyTester.High[1] - strategyTester.Low[1] + strategyTester.High[2] - strategyTester.Low[2] + strategyTester.High[3] - strategyTester.Low[3]) / 3.0;

                // Indicatore direzione recente
                bool closeAbovePrevClose = strategyTester.Close[1] > strategyTester.Close[2];
                int trendStrength3 = (strategyTester.Close[1] > strategyTester.Close[2] ? 1 : 0) + (strategyTester.Close[2] > strategyTester.Close[3] ? 1 : 0) + (strategyTester.Close[3] > strategyTester.Close[4] ? 1 : 0);

                // Posizione relativa di chiusura e apertura
                double range1 = strategyTester.High[1] - strategyTester.Low[1];
                double closeInRange1 = (strategyTester.Close[1] - strategyTester.Low[1]) / range1;
                double openInRange1 = (strategyTester.Open[1] - strategyTester.Low[1]) / range1;


                double currentLowerShadow = current.Close - current.Low;

                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO CandleInfo (
                        time, session, prevDir, prevVolume, bodyRange, totalRange, previousClose,
                        openGap, volatility, bodyToRangeRatio, upperShadow, lowerShadowFull,
                        closePositionInRange, openPositionInRange, bodyCenterInRange, upperToLowerShadowRatio, shadowDominance,
                        volumeSpike, isDoji, isMarubozu, hour, minute, dayOfWeek, momentum3, avgBody3, avgVolume3, rollingVolatility3, closeAbovePrevClose,
                        trendStrength3, range1, closeInRange1, openInRange1, currentLowerShadow
                    ) VALUES (
                        @time, @session, @prevDir, @prevVolume, @bodyRange, @totalRange, @previousClose,
                        @openGap, @volatility, @bodyToRangeRatio, @upperShadow, @lowerShadowFull,
                        @closePositionInRange, @openPositionInRange, @bodyCenterInRange, @upperToLowerShadowRatio, @shadowDominance,
                        @volumeSpike, @isDoji, @isMarubozu, @hour, @minute, @dayOfWeek, @momentum3, @avgBody3, @avgVolume3, @rollingVolatility3,
                        @closeAbovePrevClose, @trendStrength3, @range1, @closeInRange1, @openInRange1, @currentLowerShadow
                    )";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("time", time);
                        cmd.Parameters.AddWithValue("session", session);
                        cmd.Parameters.AddWithValue("prevDir", prevDir);
                        cmd.Parameters.AddWithValue("prevVolume", current.Volume);
                        cmd.Parameters.AddWithValue("bodyRange", bodyRange);
                        cmd.Parameters.AddWithValue("totalRange", totalRange);
                        cmd.Parameters.AddWithValue("previousClose", previousClose);
                        cmd.Parameters.AddWithValue("openGap", openGap);
                        cmd.Parameters.AddWithValue("volatility", volatility);
                        cmd.Parameters.AddWithValue("bodyToRangeRatio", bodyToRangeRatio);
                        cmd.Parameters.AddWithValue("upperShadow", upperShadow);
                        cmd.Parameters.AddWithValue("lowerShadowFull", lowerShadowFull);
                        cmd.Parameters.AddWithValue("closePositionInRange", closePositionInRange);
                        cmd.Parameters.AddWithValue("openPositionInRange", openPositionInRange);
                        cmd.Parameters.AddWithValue("bodyCenterInRange", bodyCenterInRange);
                        cmd.Parameters.AddWithValue("upperToLowerShadowRatio", upperToLowerShadowRatio);
                        cmd.Parameters.AddWithValue("shadowDominance", shadowDominance);
                        cmd.Parameters.AddWithValue("volumeSpike", volumeSpike);
                        cmd.Parameters.AddWithValue("isDoji", isDoji);
                        cmd.Parameters.AddWithValue("isMarubozu", isMarubozu);
                        cmd.Parameters.AddWithValue("hour", hour);
                        cmd.Parameters.AddWithValue("minute", minute);
                        cmd.Parameters.AddWithValue("dayOfWeek", dayOfWeek);

                        cmd.Parameters.AddWithValue("momentum3", momentum3);
                        cmd.Parameters.AddWithValue("avgBody3", avgBody3);
                        cmd.Parameters.AddWithValue("avgVolume3", avgVolume3);
                        cmd.Parameters.AddWithValue("rollingVolatility3", rollingVolatility3);
                        cmd.Parameters.AddWithValue("closeAbovePrevClose", closeAbovePrevClose);
                        cmd.Parameters.AddWithValue("dayOfWcloseAbovePrevCloseeek", closeAbovePrevClose);
                        cmd.Parameters.AddWithValue("trendStrength3", trendStrength3);
                        cmd.Parameters.AddWithValue("range1", range1);
                        cmd.Parameters.AddWithValue("closeInRange1", closeInRange1);
                        cmd.Parameters.AddWithValue("openInRange1", openInRange1);
                        cmd.Parameters.AddWithValue("currentLowerShadow", (object?)currentLowerShadow ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                strategyTester.LogError($"Errore in DownloadCandleInfo: {ex.Message}\n{ex.StackTrace}" +
                    (ex.InnerException != null ? $"\nInner: {ex.InnerException.Message}" : ""));
            }
        }



        private string GetSessionLabel(DateTime time)
        {
            TimeSpan t = time.TimeOfDay;

            if ((t >= strategyTester.OvernightStart) || (t < strategyTester.PreMMarket))
                return "Overnight";
            else if (t >= strategyTester.PreMMarket && t < strategyTester.MarketOpen)
                return "Pre-Market";
            else if (t >= strategyTester.MarketOpen && t < strategyTester.MarketClose)
                return "Market";
            else if (t >= strategyTester.MarketClose && t < strategyTester.AfterHourEnd)
                return "After-Hour";
            else
                return "Pause"; // Fallback
        }




        public void AggregatedTradeDataAndDownloadCSV(List<string> tableNames, string outputPath)
        {
            var aggregatedData = new Dictionary<int, Dictionary<string, object>>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                foreach (var table in tableNames)
                {
                    string query = $"SELECT * FROM {table}";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int tradeId = reader["TradeID"] != DBNull.Value ? Convert.ToInt32(reader["TradeID"]) : -1;

                            if (!aggregatedData.ContainsKey(tradeId))
                                aggregatedData[tradeId] = new Dictionary<string, object>();

                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string columnName = reader.GetName(i);
                                object value = reader.GetValue(i);

                                string uniqueKey = table + "_" + columnName;

                                if (!aggregatedData[tradeId].ContainsKey(uniqueKey))
                                    aggregatedData[tradeId][uniqueKey] = value;
                            }
                        }
                    }
                }

                // Trova tutte le colonne usate
                var allColumns = aggregatedData
                    .SelectMany(kvp => kvp.Value.Keys)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                string separator = ";"; // importante: deve essere string, non char

                // Scrive il file CSV
                using (StreamWriter writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    // Header
                    List<string> headerRow = new List<string> { "TradeID" };
                    headerRow.AddRange(allColumns);
                    writer.WriteLine(string.Join(separator, headerRow));

                    // Riga per ciascun TradeID
                    foreach (var tradeEntry in aggregatedData)
                    {
                        int tradeID = tradeEntry.Key;
                        var values = tradeEntry.Value;

                        List<string> row = new List<string> { tradeID.ToString() };

                        foreach (var col in allColumns)
                        {
                            if (values.TryGetValue(col, out object val))
                            {
                                string cleaned = val?.ToString()?.Replace("\r", "").Replace("\n", "").Replace(separator, ",") ?? "";
                                row.Add($"\"{cleaned}\""); // racchiude tra virgolette per compatibilità
                            }
                            else
                            {
                                row.Add("");
                            }
                        }

                        writer.WriteLine(string.Join(separator, row));
                    }
                }

                strategyTestOriginale.Print("CSV aggregato esportato con intestazioni in: " + outputPath);
            }
        }


        //creato da claude
        public Dictionary<string, double> AnalyzeCandleCorrelations(int minTradeId = 0, int maxTradeId = int.MaxValue)
        {
            var correlations = new Dictionary<string, double>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    string query = @"
                        SELECT 
                            f.DiffOC, f.DiffHL, f.DiffCC, f.PercentChange, f.BarRangeRatio,
                            f.ClosePositionInBar, f.BodyToWickRatio, f.RealBodyRatio, f.Momentum3,
                            f.RSI14, f.MACD, f.MACDHist, f.BBPosition, f.Volatility20,
                            f.PriceDirection, f.GapValue, f.IsGapPresent, f.GapDirection,
                            f.SMA10, f.EMA10, f.Volume, f.Engulfing, f.InsideBar,
                            LEAD(f.PriceDirection) OVER (ORDER BY f.TradeID) as NextDirection,
                            LEAD(f.PercentChange) OVER (ORDER BY f.TradeID) as NextPercentChange,
                            LEAD(f.BarRangeRatio) OVER (ORDER BY f.TradeID) as NextVolatility
                        FROM Features f
                        WHERE f.TradeID BETWEEN @MinTradeId AND @MaxTradeId
                        ORDER BY f.TradeID";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@MinTradeId", minTradeId);
                        command.Parameters.AddWithValue("@MaxTradeId", maxTradeId);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            var data = new List<Dictionary<string, double>>();
                            
                            while (reader.Read())
                            {
                                var row = new Dictionary<string, double>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    if (reader[i] != DBNull.Value)
                                    {
                                        row[reader.GetName(i)] = Convert.ToDouble(reader[i]);
                                    }
                                }
                                data.Add(row);
                            }

                            correlations = CalculateInterestingCorrelations(data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (downloadData != null)
                        downloadData.Print($"Errore nell'analisi delle correlazioni: {ex.Message}");
                }
            }

            return correlations;
        }

        private Dictionary<string, double> CalculateInterestingCorrelations(List<Dictionary<string, double>> data)
        {
            var correlations = new Dictionary<string, double>();
            
            if (data.Count < 2) return correlations;

            var correlationPairs = new List<(string x, string y, string description)>
            {
                ("RSI14", "NextDirection", "RSI vs Direzione Futura"),
                ("MACD", "NextPercentChange", "MACD vs Variazione Futura"),
                ("BodyToWickRatio", "NextVolatility", "Corpo/Ombra vs Volatilità Futura"),
                ("BBPosition", "NextDirection", "Posizione BB vs Direzione Futura"),
                ("GapValue", "NextPercentChange", "Gap vs Variazione Futura"),
                ("Momentum3", "NextDirection", "Momentum vs Direzione Futura"),
                ("ClosePositionInBar", "NextDirection", "Posizione Chiusura vs Direzione Futura"),
                ("PercentChange", "NextPercentChange", "Variazione vs Variazione Futura"),
                ("Volatility20", "NextVolatility", "Volatilità vs Volatilità Futura"),
                ("BarRangeRatio", "NextDirection", "Ampiezza Barra vs Direzione Futura"),
                ("Volume", "NextPercentChange", "Volume vs Variazione Futura"),
                ("Engulfing", "NextDirection", "Pattern Engulfing vs Direzione Futura"),
                ("InsideBar", "NextVolatility", "Inside Bar vs Volatilità Futura"),
                ("DiffCC", "NextDirection", "Differenza Chiusure vs Direzione Futura"),
                ("RealBodyRatio", "NextPercentChange", "Rapporto Corpo vs Variazione Futura")
            };

            foreach (var (x, y, description) in correlationPairs)
            {
                double correlation = CalculatePearsonCorrelation(data, x, y);
                if (!double.IsNaN(correlation) && Math.Abs(correlation) > 0.1)
                {
                    correlations[description] = correlation;
                }
            }

            var strongCorrelations = correlations
                .Where(kvp => Math.Abs(kvp.Value) > 0.3)
                .OrderByDescending(kvp => Math.Abs(kvp.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return strongCorrelations;
        }

        private double CalculatePearsonCorrelation(List<Dictionary<string, double>> data, string xKey, string yKey)
        {
            var validPairs = data
                .Where(row => row.ContainsKey(xKey) && row.ContainsKey(yKey))
                .Select(row => new { X = row[xKey], Y = row[yKey] })
                .ToList();

            if (validPairs.Count < 2) return double.NaN;

            double xMean = validPairs.Average(p => p.X);
            double yMean = validPairs.Average(p => p.Y);

            double numerator = validPairs.Sum(p => (p.X - xMean) * (p.Y - yMean));
            double xSumSquares = validPairs.Sum(p => Math.Pow(p.X - xMean, 2));
            double ySumSquares = validPairs.Sum(p => Math.Pow(p.Y - yMean, 2));

            double denominator = Math.Sqrt(xSumSquares * ySumSquares);
            
            return denominator == 0 ? 0 : numerator / denominator;
        }

        public void PrintCorrelationReport(Dictionary<string, double> correlations)
        {
            if (downloadData != null)
            {
                downloadData.Print("=== ANALISI CORRELAZIONI CANDELE ===");
                downloadData.Print($"Trovate {correlations.Count} correlazioni significative:");
                
                foreach (var correlation in correlations)
                {
                    string strength = Math.Abs(correlation.Value) > 0.7 ? "FORTE" :
                                    Math.Abs(correlation.Value) > 0.5 ? "MEDIA" : "DEBOLE";
                    
                    downloadData.Print($"{correlation.Key}: {correlation.Value:F3} ({strength})");
                }
                downloadData.Print("=====================================");
            }
        }


    }
}
