using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Inventory.Api.Domain;


public class Product
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
}