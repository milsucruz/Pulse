using Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Requests
{
    public class SendNotificationRequest
    {
        [Required, EmailAddress]
        public string Recipient { get; set; }

        [Required, MaxLength(200)]
        public string Subject { get; set; }

        [Required, MaxLength(5000)]
        public string Body { get; set; }

        [Required]
        public NotificationTypeEnum Type { get; set; }

        public string? Priority { get; set; } = "high";
    }
}
