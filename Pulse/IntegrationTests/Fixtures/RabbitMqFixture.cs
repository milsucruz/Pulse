using Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using InfraRabbitMqConfig = Infrastructure.Messaging.RabbitMqConfiguration;

namespace IntegrationTests.Fixtures;

[CollectionDefinition("RabbitMq")]
public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture> { }

public class RabbitMqFixture : IAsyncLifetime
{
    public const string ExchangeName = "notifications.topic";
    public const string TestQueue = "test.email.queue";
    public const string RoutingKey = "notifications.email.high";

    private readonly RabbitMqContainer container = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

    public InfraRabbitMqConfig Configuration { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        Configuration = new InfraRabbitMqConfig
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            VirtualHost = "/",
            ExchangeName = ExchangeName,
            DeadLetterExchange = "notifications.dlx",
            DeadLetterQueue = "notifications.dlq"
        };

        await using var connection = await CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(TestQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(TestQueue, ExchangeName, "notifications.email.#");
    }

    public RabbitMqPublisher CreatePublisher() =>
        new(Options.Create(Configuration), NullLogger<RabbitMqPublisher>.Instance);

    public async Task<IConnection> CreateConnectionAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = Configuration.Host,
            Port = Configuration.Port,
            UserName = Configuration.Username,
            Password = Configuration.Password,
            VirtualHost = Configuration.VirtualHost
        };
        return await factory.CreateConnectionAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}
