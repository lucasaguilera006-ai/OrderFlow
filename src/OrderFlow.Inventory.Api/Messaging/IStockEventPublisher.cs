using OrderFlow.Contracts;

namespace OrderFlow.Inventory.Api.Messaging;

public interface IStockEventPublisher
{
    Task PublishStockRejectedAsync(StockRejected message, CancellationToken ct = default);
    Task PublishStockReservedAsync(StockReserved message, CancellationToken ct = default);
}