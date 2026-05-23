using Infrastructure.Messaging;
using Infrastructure.Senders;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messages;
using System.Text.Json;

namespace Worker;

public class PushNotificationConsumer(
    IOptions<RabbitMqConfiguration> config,
    PushSender pushSender,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<PushNotificationConsumer> logger) : BackgroundService
{
    private const string QueueName = "push.queue";
    private const string PipelineKey = "push-sender";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = config.Value;

        var factory = new ConnectionFactory
        {
            HostName = cfg.Host,
            Port = cfg.Port,
            UserName = cfg.Username,
            Password = cfg.Password,
            VirtualHost = cfg.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleMessageAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation("PushNotificationConsumer started, listening on {Queue}", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("PushNotificationConsumer stopping");
        }
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        NotificationMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<NotificationMessage>(ea.Body.Span);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize message from {Queue}. Sending to DLQ", QueueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (message is null || !message.IsValid())
        {
            logger.LogError("Invalid message received from {Queue}. Sending to DLQ", QueueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            var pipeline = pipelineProvider.GetPipeline<bool>(PipelineKey);

            await pipeline.ExecuteAsync(async ct =>
            {
                await pushSender.SendAsync(message, ct);
                return true;
            }, stoppingToken);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

            logger.LogInformation(
                "Notification {NotificationId} processed successfully",
                message.NotificationId);
        }
        catch (BrokenCircuitException ex)
        {
            logger.LogWarning(ex,
                "Circuit open for {Pipeline}. Requeuing notification {NotificationId}",
                PipelineKey, message.NotificationId);

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Processing cancelled for notification {NotificationId}. Requeuing",
                message.NotificationId);

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Permanent failure processing notification {NotificationId}. Sending to DLQ",
                message.NotificationId);

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
