using Application.Interfaces;
using Infrastructure.Messaging;
using Infrastructure.Senders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messages;
using System.Text.Json;

namespace Worker;

public class EmailNotificationConsumer(
    IOptions<RabbitMqConfiguration> config,
    [FromKeyedServices("email")] INotificationSender sender,
    ResiliencePipelineProvider<string> pipelineProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailNotificationConsumer> logger) : BackgroundService
{
    private const string QueueName = "email.queue";
    private const string PipelineKey = "email-sender";

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
        consumer.ReceivedAsync += (_, ea) => HandleMessageAsync(channel, ea, CancellationToken.None);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation("EmailNotificationConsumer started, listening on {Queue}", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("EmailNotificationConsumer stopping");
        }
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken cancellationToken)
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
                await sender.SendAsync(message, ct);
                return true;
            }, cancellationToken);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
                var notification = await repository.GetByIdAsync(message.NotificationId, cancellationToken);
                if (notification is not null)
                {
                    notification.MarkAsProcessed();
                    await repository.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to update notification status for {NotificationId}. Message was already ACKed.",
                    message.NotificationId);
            }

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
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Permanent failure processing notification {NotificationId}. Sending to DLQ",
                message.NotificationId);

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
