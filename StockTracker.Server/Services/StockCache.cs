using System.Collections.Concurrent;
using System.Threading.Tasks;
using StockTracker.Models;
namespace StockTracker.Services;
public class StockCache
{
    private ConcurrentDictionary<long,StockDataModel> _loadedStockData = new ConcurrentDictionary<long,StockDataModel>();
    public  Task LoadStockData(List<StockDataModel> stockList)
    {
        Parallel.ForEach(stockList,i=>{
            _loadedStockData.TryAdd(i.StockId,i);
        });
        return Task.CompletedTask;
    }
    public IList<StockDataModel> GetStockList 
    {
        get {
            return _loadedStockData.Values.ToList();
        }
    }
}