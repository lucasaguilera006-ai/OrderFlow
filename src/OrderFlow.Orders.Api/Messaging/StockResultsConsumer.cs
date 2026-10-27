using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Api.Data;
using RabbitMQ.Client.Events;
using OrderFlow.Orders.Api.Domain;

namespace OrderFlow.Orders.Api.Messaging;

public sealed class StockResultsConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<StockResultsConsumer> logger) : BackgroundService
{
    private const string ExchangeName = "orderflow.events";
    private const string StockReservedRoutingKey = "stock.reserved";
    private const string StockRejectedRoutingKey = "stock.rejected";
    private const string QueueName = "orders.stock-results";

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
            QueueName, ExchangeName, StockReservedRoutingKey, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            QueueName, ExchangeName, StockRejectedRoutingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var handled = await HandleAsync(ea.RoutingKey, ea.Body, stoppingToken);

                if (handled)
                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                else
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing stock result message ({RoutingKey})", ea.RoutingKey);
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

    private async Task<bool> HandleAsync(string routingKey, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        switch (routingKey)
        {
            case StockReservedRoutingKey:
                var reserved = JsonSerializer.Deserialize<StockReserved>(body.Span);
                if (reserved is null) return false;
                await UpdateStatusAsync(reserved.OrderId, OrderStatus.Confirmed, ct);
                return true;

            case StockRejectedRoutingKey:
                var rejected = JsonSerializer.Deserialize<StockRejected>(body.Span);
                if (rejected is null) return false;
                logger.LogWarning(
                    "Order {OrderId} rejected by inventory: {Reason}", rejected.OrderId, rejected.Reason);
                await UpdateStatusAsync(rejected.OrderId, OrderStatus.Rejected, ct);
                return true;

            default:
                logger.LogWarning("Unexpected routing key {RoutingKey}", routingKey);
                return false;
        }
    }

    private async Task UpdateStatusAsync(Guid orderId, OrderStatus newStatus, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // Only pending orders are updated, so a redelivered message does not overwrite a final status.
        var updated = await db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, newStatus), ct);

        if (updated == 1)
            logger.LogInformation("Order {OrderId} moved to {Status}", orderId, newStatus);
        else
            logger.LogWarning(
                "Order {OrderId} not updated: it does not exist or is no longer pending", orderId);
    }
}