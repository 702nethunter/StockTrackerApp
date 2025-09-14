using StockTracker.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using Microsoft.AspNetCore.Builder;  
using Microsoft.AspNetCore.Hosting;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.log", rollingInterval: RollingInterval.Day, shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Redis connection (e.g., localhost:6379 or your container hostname)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddHttpClient<NasdaqListedParser>();
builder.Services.AddHostedService<StockBootStrapper>();
// HTTP/2 for gRPC (dev: cleartext; prod: use HTTPS)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5003, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2; // Force HTTP/2
        listenOptions.UseHttps(); // Use development certificate
    });
    
    // Optional: Also listen on HTTP/1.1 for REST endpoints
    options.ListenLocalhost(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
});

var host = builder.Build();
host.Run();
