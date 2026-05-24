using Domain.Enums;

namespace Domain.Entities
{
    public class Notification
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Recipient { get; private set; } = default!;
        public string Subject { get; private set; } = default!;
        public string Body { get; private set; } = default!;
        public string Priority { get; private set; } = "high";
        public NotificationTypeEnum Type { get; private set; }
        public NotificationStatusEnum Status { get; private set; } = NotificationStatusEnum.Pending;
        public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; private set; }
        public string? ErrorMessage { get; private set; }

        public bool IsDispatched { get; private set; }

        public static Notification Create(
            string recipient, string subject, string body,
            NotificationTypeEnum type, string priority = "high")
            => new()
            {
                Recipient = recipient,
                Subject   = subject,
                Body      = body,
                Type      = type,
                Priority  = priority
            };

        public void MarkAsProcessed()
        {
            if (Status == NotificationStatusEnum.Sent) return;
            if (Status != NotificationStatusEnum.Pending)
                throw new InvalidOperationException($"Cannot transition to Sent from status {Status}.");
            (Status, ProcessedAt) = (NotificationStatusEnum.Sent, DateTime.UtcNow);
        }

        public void MarkAsFailed(string error)
        {
            if (Status != NotificationStatusEnum.Pending)
                throw new InvalidOperationException($"Cannot transition to Failed from status {Status}.");
            (Status, ErrorMessage) = (NotificationStatusEnum.Failed, error);
        }

        public void MarkAsDispatched() => IsDispatched = true;
    }
}
