# Simple EMA trading strategy based on SP500, 5-year backtest on H1 time frame (from 01/01/2020 to 01/08/2025) - just Long position

** Important Notice**  
This strategy references **external classes** required for its proper operation, including:
- `RiskReduction`
- `PatternFilter`
- `CustomTralingStop`
- `PositionManagment`
- `RecoverData`

These components are **not included in this public version** of the code.  
If you are interested in accessing these private parts, please **contact me directly** for further discussion.

---

## Description
**PublicFirst** is an algorithmic trading strategy developed for **NinjaTrader 8**, which uses technical indicators and pattern filters to detect and manage long entry opportunities in the market.

The logic is based on:
- **Exponential Moving Averages (EMA)** for entry signals.
- **Pattern filters** to avoid unfavorable chart setups.
- **Risk management** through dynamic stop loss and progressive risk reduction.
- **Advanced position management** with a custom trailing stop.

---

## How It Works
1. **Initial Setup** (`OnStateChange`)
   - Strategy configuration.
   - Initialization of indicators (EMA1, EMA3, EMA5, ADX).
   - Integration with external classes for risk and position management.

2. **Market Analysis** (`OnBarUpdate`)
   - Checks for price gaps between bars.
   - Filters out certain chart patterns to avoid risky setups.
   - Confirms EMA conditions for an uptrend.
   - Opens **long** positions when all conditions are met.

3. **Trade Management**
   - Progressive risk reduction.
   - Dynamic stop loss and custom trailing stop.
   - Trade data logging for analysis and backtesting.

---

## Indicators Used
- **EMA (1, 3, 5 periods)** → trend direction signal.
- **ADX (14 periods)** → trend strength measurement.
- **Pattern filters** → to avoid risky market setups.

---

## Private Components
The following classes are not included in the public repository:
- `RiskReduction`
- `PatternFilter`
- `CustomTralingStop`
- `PositionManagment`
- `RecoverData`

These components implement advanced risk and position management logic, along with trade data storage systems.  
To gain access to these parts, **contact me** directly.

---

## Requirements
- **NinjaTrader 8**
- Basic knowledge of C# and NT8 strategy development.
- (Optional) Access to the external classes for full functionality.

---

## License
This code is shared for educational and example purposes only.  
It is not guaranteed for production use. Trading in financial markets is **at your own risk**.

---

## Contact
- **LinkedIn**: [Matteo Signorotti](https://www.linkedin.com/in/matteo-signorotti-668334176/)

---

