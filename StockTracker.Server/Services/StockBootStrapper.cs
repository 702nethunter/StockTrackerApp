using System.Text.Json;
using System.Text.Json.Serialization;  
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class SymbolDto
{
    [JsonPropertyName("symbol")] public string Symbol { get; init; } = "";
    [JsonPropertyName("name")]   public string? Name   { get; init; }
    // add more fields if your parser stores them (MarketCategory, etc.)
}

public class StockBootStrapper:IHostedService
{
    private const string CacheKey = "nasdaq:symbols:stocksymbolname:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly NasdaqListedParser _parser;
    private readonly ILogger<StockBootStrapper> _logger;
    private readonly IConnectionMultiplexer _redis;
    public StockBootStrapper(NasdaqListedParser parser,ILogger<StockBootStrapper> logger,IConnectionMultiplexer redis)
    {
        _logger = logger;
        _parser = parser;
        _redis = redis;

    }
    public async Task StartAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // 1) Try cache
        var cached = await db.StringGetAsync(CacheKey);
        if (!cached.IsNullOrEmpty)
        {
            try
            {
                var symbols = JsonSerializer.Deserialize<List<StockSymbolName>>(cached!, JsonOpts) ?? new();
                _logger.LogInformation("Loaded {Count} symbols from Redis.", symbols.Count);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached symbols. Will refetch.");
            }
        }

        // 2) Fetch from web
        var fromWeb = await _parser.FetchSymbolsAsync(1000, ct); // expected: List<StockSymbolName>

        // 3) Cache as-is (no mapping)
        var json = JsonSerializer.Serialize(fromWeb, JsonOpts);
        await db.StringSetAsync(CacheKey, json, expiry: CacheTtl);

        _logger.LogInformation("Fetched and cached {Count} symbols.", fromWeb.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Background Service is stopping");

        return Task.CompletedTask;
    }

    

}