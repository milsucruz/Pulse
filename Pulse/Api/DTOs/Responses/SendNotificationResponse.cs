namespace Api.DTOs.Responses
{
    public record SendNotificationResponse
    {
        public Guid NotificationId { get; init; }
    }
}
