using Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Requests
{
    public class SendNotificationRequest
    {
        [EmailAddress]
        public required string Recipient { get; set; }

        [MaxLength(200)]
        public required string Subject { get; set; }

        [MaxLength(5000)]
        public required string Body { get; set; }

        [Required]
        public NotificationTypeEnum Type { get; set; }

        public string? Priority { get; set; } = "high";
    }
}
