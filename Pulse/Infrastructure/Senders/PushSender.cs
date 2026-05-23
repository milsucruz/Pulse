using Microsoft.Extensions.Logging;
using Shared.Messages;

namespace Infrastructure.Senders;

public class PushSender(ILogger<PushSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        logger.LogInformation(
            "Push notification sent to {Recipient} for notification {NotificationId}",
            message.Recipient, message.NotificationId);
    }
}
