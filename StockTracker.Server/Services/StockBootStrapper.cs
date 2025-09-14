public class StockBootStrapper:IHostedService
{
    private readonly string stock_symbolsFile="stock_symbols.json";
    private readonly NasdaqListedParser _parser;
    private readonly ILogger<StockBootStrapper> _logger;
    public StockBootStrapper(NasdaqListedParser parser,ILogger<StockBootStrapper> logger)
    {
        _logger = logger;
        _parser = parser;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching NASDAQ symbols on startup...");

        var symbols = await _parser.FetchSymbolsAsync(1000,cancellationToken);

        _logger.LogInformation("Loaded {Count} Symbols:",symbols.Count);

    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Background Service is stopping");

        return Task.CompletedTask;
    }

    

}