namespace StockTracker.Models;

public sealed record ClientDataModel
{
    public long HostId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string ClientIP { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public DateTime ClientAddedDate { get; init; }
}