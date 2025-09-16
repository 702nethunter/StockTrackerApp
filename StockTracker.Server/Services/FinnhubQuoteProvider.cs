using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading;
public sealed class FinnhubQuoteProvider:IQuoteProvider
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly RateLimiter _limiter;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public FinnhubQuoteProvider(HttpClient http,IConfiguration cfg)
    {
        _http = http;
        _token = cfg["Finnhub:Token"]?? throw new InvalidOperationException("Finnhub:Token missing");

        var perMinute = Math.Max(1,cfg.GetValue<int?>("Finnhub:RatePerMinute")??50);

        _limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions{
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit =10_000
        });

        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
    public async Task<(string? Name,decimal LastPrice,DateTime MarketTime,double Volume)>
    GetQuoteAsync(string symbol, CancellationToken ct)
    {
        using var lease = await _limiter.AcquireAsync(1,ct);
        var url = $"https://finnhub.io/api/v1/quote?symbol={symbol}&token={_token}";
        
        const int maxAttempts=5;
        for(int attempt=0;attempt<maxAttempts;attempt++)
        {
            using var resp = await _http.GetAsync(url,ct);
            if(resp.StatusCode == (HttpStatusCode)429)
            {
                var delay = GetRetryAfter(resp)??Backoff(attempt);
                await Task.Delay(delay,ct);
                continue;
            }

            if((int)resp.StatusCode>=500)
            {
                await Task.Delay(Backoff(attempt),ct);
                continue;
            }

            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsByteArrayAsync(ct));
            var root = doc.RootElement;

            var price = root.TryGetProperty("c",out var cProp) && cProp.TryGetDecimal(out var cVal) ? cVal: 0m;
            var tsSec = root.TryGetProperty("t",out var tProp) && tProp.TryGetInt64(out var tVal) ? tVal: DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var vol = root.TryGetProperty("v",out var vProp) && vProp.TryGetDouble(out var vVal) ? vVal: 0d;

            return (Name:null,LastPrice:price,MarketTime:DateTimeOffset.FromUnixTimeSeconds(tsSec).UtcDateTime,Volume:vol);
        }
        //if we got here after retries, surface a friendly error
        throw new HttpRequestException($"Finnhub rate limit or server errors persisted for symbol {symbol}.");
    }
    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if(ra==null) return null;
        if(ra.Delta is TimeSpan delta && delta >TimeSpan.Zero) return delta;
        if(ra.Date is DateTimeOffset date)
        {
            var diff = date-DateTimeOffset.UtcNow;
            if(diff>TimeSpan.Zero) return diff;
        }
        return null;
    }
    private static TimeSpan Backoff(int attempt)
    {
        var baseMs = (int)Math.Min(30_000,500*Math.Pow(2,attempt));
        var jitter = Random.Shared.NextDouble()*baseMs;
        return TimeSpan.FromMilliseconds(baseMs/2+jitter);
    }
    public void Dispose()=>_limiter.Dispose();
}