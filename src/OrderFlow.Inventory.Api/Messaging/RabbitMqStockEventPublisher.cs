using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using RabbitMQ.Client;

namespace OrderFlow.Inventory.Api.Messaging;

public sealed class RabbitMqStockEventPublisher(IOptions<RabbitMqOptions> options)
    : IStockEventPublisher, IAsyncDisposable
{
    public const string ExchangeName = "orderflow.events";
    public const string StockReservedRoutingKey = "stock.reserved";
    public const string StockRejectedRoutingKey = "stock.rejected";

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public Task PublishStockReservedAsync(StockReserved message, CancellationToken ct = default)
        => PublishAsync(StockReservedRoutingKey, message, ct);

    public Task PublishStockRejectedAsync(StockRejected message, CancellationToken ct = default)
        => PublishAsync(StockRejectedRoutingKey, message, ct);

    private async Task PublishAsync<T>(string routingKey, T message, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
        };

        await channel.BasicPublishAsync(
            ExchangeName, routingKey,
            mandatory: false, basicProperties: props, body: body,
            cancellationToken: ct);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
            };

            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _lock.Dispose();
    }
}