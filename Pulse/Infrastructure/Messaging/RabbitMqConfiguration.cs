using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Messaging
{
    public class RabbitMqConfiguration
    {
        [Required, MinLength(1)] public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        [Required, MinLength(1)] public string Username { get; set; } = "guest";
        [Required, MinLength(1)] public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        [Required, MinLength(1)] public string ExchangeName { get; set; } = "pulse.topic";
        [Required, MinLength(1)] public string DeadLetterExchange { get; set; } = "pulse.dlx";
        [Required, MinLength(1)] public string DeadLetterQueue { get; set; } = "pulse.dlq";
    }
}
