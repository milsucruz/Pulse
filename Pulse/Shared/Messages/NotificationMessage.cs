using Domain.Enums;
using System.Text.Json.Serialization;

namespace Shared.Messages;
public sealed record NotificationMessage
{
    public Guid NotificationId { get; init; }
    public string Recipient { get; init; }
    public string Subject { get; init; }
    public string Body { get; init; }
    public NotificationTypeEnum Type { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Priority { get; init; }
    public int RetryCount { get; init; }

    [JsonConstructor]
    public NotificationMessage(
        Guid notificationId,
        string recipient,
        string subject,
        string body,
        NotificationTypeEnum type,
        DateTime createdAt,
        string priority = "high",
        int retryCount = 0)
    {
        NotificationId = notificationId;
        Recipient = recipient;
        Subject = subject;
        Body = body;
        Type = type;
        CreatedAt = createdAt;
        Priority = priority;
        RetryCount = retryCount;
    }

    public NotificationMessage WithIncrementedRetry()
        => this with { RetryCount = RetryCount + 1 };

    public bool IsValid() =>
        NotificationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(Recipient) &&
        !string.IsNullOrWhiteSpace(Subject) &&
        !string.IsNullOrWhiteSpace(Body);
}