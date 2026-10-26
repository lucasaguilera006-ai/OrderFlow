namespace OrderFlow.Contracts;

public record StockRejected(Guid OrderId, string Reason);