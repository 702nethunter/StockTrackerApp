using System.Collections;
using System.Collections.Generic;
using StockTracker.Models;
using System.Collections.Concurrent;
namespace StockTracker.Services;
public class ServiceHandler
{
    private readonly ILogger<ServiceHandler> _logger;
    private ConcurrentDictionary<long,ClientDataModel> _clientMapping = new ConcurrentDictionary<long,ClientDataModel>();
    private ConcurrentDictionary<long,StockDataModel> _clientStockMapping = new ConcurrentDictionary<long,StockDataModel>();
    private StockCache _stockCache;
    private ConcurrentQueue<StockDataModel> _stockQueue=new ConcurrentQueue<StockDataModel>();
    private object mappingLock = new ();
    public ServiceHandler(ILogger<ServiceHandler> logger,StockCache stockCache)
    {
        _logger = logger;
    }
    public void Init()
    {
       
        var stockList = _stockCache.GetStockList;

        if (stockList != null)
        {
            foreach (var stock in stockList)
            {
                _stockQueue.Enqueue(stock);
            }
        }
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