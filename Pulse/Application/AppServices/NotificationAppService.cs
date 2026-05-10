using Application.Commands;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.AppServices
{
    public class NotificationAppService : INotificationAppService
    {
		private readonly IMessagePublisher messagePublisher;
		private readonly ILogger<NotificationAppService> logger;
		private readonly INotificationRepository notificationRepository;

        public NotificationAppService(
            IMessagePublisher messagePublisher,
            ILogger<NotificationAppService> logger,
            INotificationRepository notificationRepository)
        {
            this.messagePublisher = messagePublisher;
            this.logger = logger;
            this.notificationRepository = notificationRepository;
        }

        public async Task<Guid> SendAsync(SendNotificationCommand command, CancellationToken cancellationToken)
        {
			try
			{
                //Create a new notification entity

                // repository add and save changes

                //routing key

                //the message to be published

                //publisher invoke

                logger.LogInformation(
                    "Notification {NotificationId} published with routing key {RoutingKey}",
                    notification.Id, routingKey);
                //return the notification id

                return new Guid();
			}
			catch (Exception ex)
			{
                logger.LogError(ex, "Failed to send notification for recipient {Recipient}", command.Recipient);
                throw;
			}
        }
    }
}
