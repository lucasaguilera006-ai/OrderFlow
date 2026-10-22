using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Inventory.Api.Dtos;

public sealed class CreateProductRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; init; }
}

public sealed record ProductResponse(int Id, string Name, int StockQuantity);