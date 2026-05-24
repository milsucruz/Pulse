using Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Requests
{
    public class SendNotificationRequest
    {
        [Required, EmailAddress]
        public required string Recipient { get; set; }

        [Required, MaxLength(200)]
        public required string Subject { get; set; }

        [Required, MaxLength(5000)]
        public required string Body { get; set; }

        [Required]
        [EnumDataType(typeof(NotificationTypeEnum))]
        public NotificationTypeEnum? Type { get; set; }

        [AllowedValues("low", "medium", "high")]
        public string? Priority { get; set; }
    }
}
