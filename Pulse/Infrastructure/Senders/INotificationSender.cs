using Shared.Messages;

namespace Infrastructure.Senders;

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
