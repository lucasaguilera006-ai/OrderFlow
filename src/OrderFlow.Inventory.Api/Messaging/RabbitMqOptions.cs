namespace OrderFlow.Inventory.Api.Messaging;

public class RabbitMqOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}