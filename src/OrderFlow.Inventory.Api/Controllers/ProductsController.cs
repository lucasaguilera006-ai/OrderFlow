using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Dtos;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Inventory.Api.Data;

namespace OrderFlow.Inventory.Api.Controllers;

[ApiController]
[Route("products")]
public class ProductsController(InventoryDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product
        {
            Name = request.Name.Trim(),
            StockQuantity = request.StockQuantity
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToResponse(product));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> GetById(int id, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
            return NotFound();

        return ToResponse(product);
    }

    private static ProductResponse ToResponse(Product p) =>
        new(p.Id, p.Name, p.StockQuantity);
}