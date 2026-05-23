using Microsoft.Extensions.Logging;
using Shared.Messages;

namespace Infrastructure.Senders;

public class EmailSender(ILogger<EmailSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        logger.LogInformation(
            "Email sent to {Recipient} with subject {Subject} for notification {NotificationId}",
            message.Recipient, message.Subject, message.NotificationId);
    }
}
