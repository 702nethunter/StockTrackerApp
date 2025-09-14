using Grpc.Core;
using StockTracker.Proto;
using Google.Protobuf.WellKnownTypes;

namespace StockTracker.Server.Services;

public class StockTrackerServiceGrpc:StockTrackerService.StockTrackerServiceBase
{
    private readonly ILogger<StockTrackerServiceGrpc> _logger;
    public StockTrackerServiceGrpc(ILogger<StockTrackerServiceGrpc> logger)
    {   
        _logger = logger;
    }
}