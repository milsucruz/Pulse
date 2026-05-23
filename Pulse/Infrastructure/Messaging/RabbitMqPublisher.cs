using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Messaging
{
    public class RabbitMqPublisher : IMessagePublisher, IDisposable
    {
        private readonly IConnection connection;
        private readonly IChannel channel;
        private readonly RabbitMqConfiguration config;
        private readonly ILogger<RabbitMqPublisher> logger;

        public RabbitMqPublisher(
            IOptions<RabbitMqConfiguration> config,
            ILogger<RabbitMqPublisher> logger)
        {
            this.config = config.Value;
            this.logger = logger;

            var factory = new ConnectionFactory
            {
                HostName = this.config.Host,
                Port = this.config.Port,
                UserName = this.config.Username,
                Password = this.config.Password,
                VirtualHost = this.config.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            channel = connection.CreateChannelAsync().GetAwaiter().GetResult();
        }

        public async Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
            where T : class
        {
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

        public void Dispose()
        {
            channel?.CloseAsync().GetAwaiter().GetResult();
            connection?.CloseAsync().GetAwaiter().GetResult();
        }
    }
}
