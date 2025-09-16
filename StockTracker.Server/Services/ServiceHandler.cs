using System.Collections;
using System.Collections.Generic;
using StockTracker.Models;
using System.Collections.Concurrent;
using StackExchange.Redis;
using System.Text.Json;
namespace StockTracker.Services;
public class ServiceHandler
{
    private readonly ILogger<ServiceHandler> _logger;
    private readonly ConcurrentDictionary<long,ClientDataModel> _clientMapping = new();
    private readonly ConcurrentDictionary<long,StockDataModel> _clientStockMapping = new();
    private readonly ConcurrentDictionary<string, StockDataModel> _latestBySymbol = new(StringComparer.OrdinalIgnoreCase);
    
    private ConcurrentQueue<StockDataModel> _stockQueue= new();
    private object mappingLock = new();
    private IConnectionMultiplexer _redis;
    private const string SymbolsKey = "nasdaq:symbols:stocksymbolname:v1";
    private readonly IQuoteProvider _quotes;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true // turn on if your model has public fields instead of properties
    };
    public ServiceHandler(ILogger<ServiceHandler> logger,IConnectionMultiplexer redis,IQuoteProvider quotes)
    {
        _logger = logger;
        _redis = redis;
        _quotes = quotes;
    }
   
    public async Task InitAsync(CancellationToken ct=default)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(SymbolsKey);
        if(json.IsNullOrEmpty)
        {
            _logger.LogWarning("No Cached Symbols found at {Key},Skipping hydration.",SymbolsKey);
            return;
        }

        var symbols = new List<(string Symbol,string? Name)>();
        try
        {
            using var doc = JsonDocument.Parse((string)json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Array)
            {
                _logger.LogWarning("Unexpected JSON at {Key}. Expected Array",SymbolsKey);
                return;
            }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if(el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if(!string.IsNullOrWhiteSpace(s))
                        symbols.Add((s.Trim().ToUpperInvariant(),s));
                }
                else if (el.ValueKind==JsonValueKind.Object)
                {
                    var (sym,name) = ExtractSymbolAndName(el);
                    if(!string.IsNullOrWhiteSpace(sym))
                        symbols.Add((sym!.Trim().ToUpperInvariant(),name));
                }
                
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex,"Failed to read symbols from {Key}",SymbolsKey);
            throw ex;
        }

        // de-dupe…
        var unique = symbols
            .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Symbol: g.Key, Name: g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))))
            .ToList();

        if (unique.Count == 0)
        {
            // helpful debug sample
            try
            {
                using var doc = JsonDocument.Parse(json.ToString());
                var sample = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                    ? doc.RootElement[0].GetRawText()
                    : "<empty array>";
                _logger.LogWarning("No symbols extracted from {Key}. First element sample: {Sample}", SymbolsKey, sample);
            }
            catch { /* ignore */ }

            _logger.LogInformation("Symbol list empty after parsing; nothing to do");
            return;
        }

        var dop = Math.Min(Environment.ProcessorCount*4,24);
        using var gate =new SemaphoreSlim(dop);

        var tasks = new List<Task>(unique.Count);
        foreach(var (symbol,nameFromList) in unique)
        {
            await gate.WaitAsync(ct);
            tasks.Add(Task.Run(async()=>
            {
                try
                {
                    var (name,price,time,vol) = await _quotes.GetQuoteAsync(symbol,ct);

                    var sdm = new StockDataModel
                    {
                        StockId = StableId(symbol),
                        StockSymbol = symbol,
                        StockName= string.IsNullOrWhiteSpace(name)? (nameFromList??symbol):name!,
                        LastPrice = price,
                        MarketTime = time== default?DateTime.UtcNow:time,
                        Volume = vol
                    };

                    _latestBySymbol[sdm.StockSymbol] =sdm;
                    _stockQueue.Enqueue(sdm);
                }
                catch(Exception ex)
                {
                    _logger.LogWarning(ex,"Failed to fetch quote for {Symbol}",symbol);
                }
                finally
                {
                    gate.Release();
                }
            },ct));
        }
        await Task.WhenAll(tasks);
        _logger.LogInformation("Hydration complete. Loaded {Count} latest quotes.",_latestBySymbol.Count);

    }
    public bool TryGetLatest(string symbol,out StockDataModel sdm)=>_latestBySymbol.TryGetValue(symbol,out sdm!);

    private static string? TryGetString(JsonElement el,string name)
    => el.TryGetProperty(name,out var p)?p.GetString():null;

    private static long StableId(string symbol)
    {
        unchecked
        {
            const long FnvOffset = 1469598103934665603;
            const long FnvPrime = 1099511628211;
            long hash = FnvOffset;
            foreach(var ch in symbol.ToUpperInvariant())
            {
                hash ^= ch;
                hash *= FnvPrime;
            }
            return hash;
        }
    }
    private static string? GetStringPropertyCI(JsonElement obj, params string[] candidates)
    {
        foreach (var prop in obj.EnumerateObject())
            foreach (var cand in candidates)
                if (string.Equals(prop.Name, cand, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
        return null;
    }

    private static (string? Symbol, string? Name) ExtractSymbolAndName(JsonElement el)
    {
        // 1) Known keys first
        var sym = GetStringPropertyCI(el, "StockSymbol", "Symbol", "Ticker", "SecuritySymbol", "CqsSymbol", "ACTSymbol");
        var name = GetStringPropertyCI(el, "StockName", "Name", "SecurityName", "CompanyName");

        // 2) Heuristic fallback: any key containing "symbol"/"name"
        if (sym is null || name is null)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    if (sym is null && prop.Name.Contains("symbol", StringComparison.OrdinalIgnoreCase))
                        sym = prop.Value.GetString();
                    if (name is null && prop.Name.Contains("name", StringComparison.OrdinalIgnoreCase))
                        name = prop.Value.GetString();
                }
            }
        }
        return (sym, name);
    }


    public void Start(CancellationToken token)
    {

    }
    public async Task<int> RegisterClient(ClientDataModel clientData)
    {
        if(!_clientMapping.ContainsKey(clientData.HostId))
        {
            return 0;

        }
        else{
             _clientMapping.TryAdd(clientData.HostId,clientData);
             _logger.LogInformation($"Added Host Id:{clientData.HostId} to cache");
            return 0;
        }
    }
    public async Task<StockDataModel> MapStockSymbol(long HostId)
    {
        StockDataModel stockDataModel=null;
        if(_clientStockMapping.ContainsKey(HostId))
        {
            _logger.LogInformation($"Stock Symbol is already assigned for the Host{HostId}");
            return _clientStockMapping[HostId];
        }
        else{
            lock(mappingLock)
            {
                //from the queue pick up the next symbol
                StockDataModel stockModel =null;
               if( _stockQueue.TryDequeue(out stockModel))
               {
                    _clientStockMapping.TryAdd(HostId,stockModel);
                    _logger.LogInformation($"Mapping Stock Symbol{stockModel.StockSymbol} to HostId:{HostId}");
               }
               else{
                 _logger.LogWarning($"There are no stock symbols available for HostID:{HostId}");
               }
                
                
            }
            return _clientStockMapping[HostId];
             
        }
    }

}