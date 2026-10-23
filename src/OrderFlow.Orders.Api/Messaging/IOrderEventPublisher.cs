using OrderFlow.Contracts;

namespace OrderFlow.Orders.Api.Messaging;

public interface IOrderEventPublisher
{
    Task PublishOrderCreatedAsync(OrderCreated message, CancellationToken ct = default);
}