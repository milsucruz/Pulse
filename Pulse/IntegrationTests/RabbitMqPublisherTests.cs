using Domain.Enums;
using IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Shared.Messages;
using System.Text.Json;

namespace IntegrationTests;

[Collection("RabbitMq")]
public class RabbitMqPublisherTests : IAsyncLifetime
{
    private readonly RabbitMqFixture fixture;
    private IConnection connection = null!;
    private IChannel channel = null!;

    public RabbitMqPublisherTests(RabbitMqFixture fixture)
    {
        this.fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        connection = await fixture.CreateConnectionAsync();
        channel = await connection.CreateChannelAsync();
        await channel.BasicQosAsync(0, 1, false);
        await channel.QueuePurgeAsync(RabbitMqFixture.TestQueue);
    }

    public async Task DisposeAsync()
    {
        await channel.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_DeliversMessageToQueue()
    {
        var message = new NotificationMessage(
            Guid.NewGuid(), "user@test.com", "Subject", "Body",
            NotificationTypeEnum.Email, DateTime.UtcNow, "high");

        await using var publisher = fixture.CreatePublisher();
        await publisher.PublishAsync(message, RabbitMqFixture.RoutingKey, CancellationToken.None);

        var received = await PollQueueAsync();

        Assert.NotNull(received);
        var deserialized = JsonSerializer.Deserialize<NotificationMessage>(received.Body.Span);
        Assert.NotNull(deserialized);
        Assert.Equal(message.NotificationId, deserialized.NotificationId);
        Assert.Equal(message.Recipient, deserialized.Recipient);
        Assert.Equal(message.Priority, deserialized.Priority);
    }

    [Fact]
    public async Task PublishAsync_SetsMessageIdAndTimestamp()
    {
        var message = new NotificationMessage(
            Guid.NewGuid(), "user@test.com", "Subject", "Body",
            NotificationTypeEnum.Email, DateTime.UtcNow, "high");

        await using var publisher = fixture.CreatePublisher();
        await publisher.PublishAsync(message, RabbitMqFixture.RoutingKey, CancellationToken.None);

        var received = await PollQueueAsync();

        Assert.NotNull(received);
        Assert.False(string.IsNullOrEmpty(received.BasicProperties.MessageId));
        Assert.NotEqual(0, received.BasicProperties.Timestamp.UnixTime);
    }

    private async Task<BasicGetResult?> PollQueueAsync(int attempts = 10, int delayMs = 200)
    {
        for (var i = 0; i < attempts; i++)
        {
            var result = await channel.BasicGetAsync(RabbitMqFixture.TestQueue, autoAck: true);
            if (result is not null) return result;
            await Task.Delay(delayMs);
        }
        return null;
    }
}
