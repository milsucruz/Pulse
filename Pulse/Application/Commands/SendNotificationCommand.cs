using Domain.Enums;

namespace Application.Commands
{
    public record SendNotificationCommand(
        string Recipient,
        string Subject,
        string Body,
        NotificationTypeEnum Type,
        string Priority
    );
}
