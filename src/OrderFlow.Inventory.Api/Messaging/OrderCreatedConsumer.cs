using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Data;
using RabbitMQ.Client.Events;

namespace OrderFlow.Inventory.Api.Messaging;

public sealed class OrderCreatedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    private const string ExchangeName = "orderflow.events";
    private const string RoutingKey = "order.created";
    private const string QueueName = "inventory.order-created";

    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(
            ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            QueueName, ExchangeName, RoutingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<OrderCreated>(ea.Body.Span);
                if (message is null)
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                    return;
                }

                await HandleAsync(message, stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing OrderCreated message");
                await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Listening on queue {Queue}", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var updated = await db.Products
            .Where(p => p.Id == message.ProductId && p.StockQuantity >= message.Quantity)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - message.Quantity),
                ct);

        if (updated == 1)
            logger.LogInformation(
                "Stock deducted: order {OrderId}, product {ProductId}, quantity {Quantity}",
                message.OrderId, message.ProductId, message.Quantity);
        else
            logger.LogWarning(
                "Could not reserve stock: order {OrderId}, product {ProductId} does not exist or has insufficient stock for quantity {Quantity}",
                message.OrderId, message.ProductId, message.Quantity);
    }
}