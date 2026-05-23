using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Shared.Messages;

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
                Notification notification = Notification.Create(
                    recipient: command.Recipient,
                    subject: command.Subject,
                    body: command.Body,
                    type: command.Type
                );


                await notificationRepository.AddAsync(notification, cancellationToken);
                await notificationRepository.SaveChangesAsync(cancellationToken);

                var routingKey = $"pulse.{command.Type.ToString().ToLower()}.{command.Priority}";

                var message = new NotificationMessage(
                    notification.Id, command.Recipient, command.Subject,
                    command.Body, command.Type, DateTime.UtcNow, command.Priority);

                await messagePublisher.PublishAsync(message, routingKey, cancellationToken);

                logger.LogInformation(
                    "Notification {NotificationId} published with routing key {RoutingKey}",
                    notification.Id, routingKey);

                return notification.Id;
			}
			catch (Exception ex)
			{
                logger.LogError(ex, "Failed to send notification for recipient {Recipient}", command.Recipient);
                throw;
			}
        }
    }
}
