namespace Infrastructure.Messaging
{
    public class RabbitMqConfiguration
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        public string ExchangeName { get; set; } = "pulse.topic";
        public string DeadLetterExchange { get; set; } = "pulse.dlx";
        public string DeadLetterQueue { get; set; } = "pulse.dlq";
    }
}
