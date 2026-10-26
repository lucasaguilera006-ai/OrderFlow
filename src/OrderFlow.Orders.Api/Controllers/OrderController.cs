using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts;
using OrderFlow.Orders.Api.Data;
using OrderFlow.Orders.Api.Domain;
using OrderFlow.Orders.Api.Messaging;

namespace OrderFlow.Orders.Api.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly OrdersDbContext _db;
    private readonly IOrderEventPublisher _publisher;

    public OrdersController(OrdersDbContext db, IOrderEventPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        await _publisher.PublishOrderCreatedAsync(
            new OrderCreated(order.Id, order.ProductId, order.Quantity, order.CreatedAtUtc));

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var order = await _db.Orders.FindAsync(id);
        return order is null ? NotFound() : Ok(order);
    }
}