using Application.Commands;

namespace Application.Interfaces
{
    public interface INotificationAppService
    {
        Task<Guid> SendAsync(SendNotificationCommand command, CancellationToken cancellationToken);
    }
}
