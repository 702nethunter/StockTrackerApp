public interface  IQuoteProvider
{
    Task<(string? Name,decimal LastPrice,DateTime MarketTime,double Volume)>
    GetQuoteAsync(string symbol,CancellationToken ct);
}