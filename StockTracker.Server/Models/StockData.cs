namespace StockTracker.Models;

public sealed record StockDataModel
{
    public long StockId { get; init; }
    public string StockSymbol { get; init; } = default!;
    public string StockName { get; init; } = default!;
    public decimal LastPrice { get; init; }
    public DateTime MarketTime { get; init; }
    public double Volume { get; init; }
}

public sealed record PriceTick(
    string Symbol,
    decimal Price,
    DateTimeOffset TimeStamp,
    double? Volume=null
);