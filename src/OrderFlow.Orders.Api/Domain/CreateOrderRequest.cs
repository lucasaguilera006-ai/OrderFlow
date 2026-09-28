namespace OrderFlow.Orders.Api.Domain;

public record CreateOrderRequest(int ProductId, int Quantity);