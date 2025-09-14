using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;


public class NasdaqListedParser
{
    private const string NasdaqListedUrl = "https://www.nasdaqtrader.com/dynamic/SymDir/nasdaqlisted.txt";

    private readonly ILogger<NasdaqListedParser> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly HttpClient _http;

    // Prefer injecting HttpClient (HttpClientFactory) so sockets are reused.
    public NasdaqListedParser(ILogger<NasdaqListedParser> logger, HttpClient httpClient)
    {
        _logger = logger;
        _http = httpClient;

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2,4,8,16,32s
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    var reason = outcome.Exception?.Message
                                 ?? $"HTTP {(int)outcome.Result.StatusCode} {outcome.Result.StatusCode}";
                    _logger.LogInformation("Retry {RetryAttempt} after {Delay}s due to: {Reason}",
                                           retryAttempt, timespan.TotalSeconds, reason);
                });
    }

    /// <summary>
    /// Fetches the NASDAQ listed file and returns up to maxItems of (Symbol, Name).
    /// </summary>
    public async Task<List<StockSymbolName>> FetchSymbolsAsync(int maxItems = 1000, CancellationToken ct = default)
    {
        // Execute the GET with Polly retries
        var response = await _retryPolicy.ExecuteAsync(
            async token => await _http.GetAsync(NasdaqListedUrl, token),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch NASDAQ listed file after retries. Status: {Status}",
                             response.StatusCode);
            return new List<StockSymbolName>();
        }

        var content = await response.Content.ReadAsStringAsync(ct);

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<StockSymbolName>(Math.Min(maxItems, 1024));

        bool headerSkipped = false;
        foreach (var rawLine in lines)
        {
            if (!headerSkipped)
            {
                // Header: Symbol|Security Name|Market Category|Test Issue|Financial Status|Round Lot Size|ETF|NextShares
                headerSkipped = true;
                continue;
            }

            var line = rawLine.Trim();
            if (line.StartsWith("File Creation Time", StringComparison.OrdinalIgnoreCase))
                break; // footer in this feed

            var parts = line.Split('|');
            if (parts.Length < 8) continue;

            var symbol = parts[0]?.Trim();
            var name = parts[1]?.Trim();
            var testIssue = parts[3]; // "Y" or "N"
            var etf = parts[6];       // "Y" or "N"

            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(name))
                continue;

            // Optional: keep only non-test, non-ETF common equities
            if (string.Equals(testIssue, "Y", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(etf, "Y", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new StockSymbolName(symbol, name));

            if (result.Count >= maxItems)
                break;
        }

        _logger.LogInformation("Parsed {Count} symbols from NASDAQ listed file.", result.Count);
        return result;
    }
}
