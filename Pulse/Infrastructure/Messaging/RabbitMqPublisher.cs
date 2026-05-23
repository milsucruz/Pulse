using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Messaging
{
    public class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
    {
        private readonly ConnectionFactory factory;
        private readonly RabbitMqConfiguration config;
        private readonly ILogger<RabbitMqPublisher> logger;
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private IConnection? connection;
        private IChannel? channel;

        public RabbitMqPublisher(
            IOptions<RabbitMqConfiguration> config,
            ILogger<RabbitMqPublisher> logger)
        {
            this.config = config.Value;
            this.logger = logger;

            factory = new ConnectionFactory
            {
                HostName = this.config.Host,
                Port = this.config.Port,
                UserName = this.config.Username,
                Password = this.config.Password,
                VirtualHost = this.config.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
        }

        public async Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
            where T : class
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (channel is null)
                {
                    connection = await factory.CreateConnectionAsync(ct);
                    channel = await connection.CreateChannelAsync(cancellationToken: ct);
                }

                var json = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(json);

                var props = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString(),
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    Headers = new Dictionary<string, object?> { ["x-retry-count"] = 0 }
                };

                await channel.BasicPublishAsync(
                    exchange: config.ExchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: props,
                    body: body,
                    cancellationToken: ct);

                logger.LogDebug("Published {MessageType} to {RoutingKey}", typeof(T).Name, routingKey);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            semaphore.Dispose();
            if (channel is not null) await channel.CloseAsync();
            if (connection is not null) await connection.CloseAsync();
        }
    }
}
