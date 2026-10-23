namespace OrderFlow.Contracts;

public sealed record OrderCreated(
    Guid OrderId,
    int ProductId,
    int Quantity,
    DateTime CreatedAtUtc);