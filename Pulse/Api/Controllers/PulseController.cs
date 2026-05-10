using Api.DTOs.Requests;
using Api.DTOs.Responses;
using Application.Commands;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PulseController : ControllerBase
    {
        private readonly INotificationAppService notificationAppService;
            
        public PulseController(INotificationAppService notificationAppService)
        {
            this.notificationAppService = notificationAppService;
        }

        /// <summary>
        /// Receives a notification request and initiates the process of sending a notification asynchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(typeof(SendNotificationResponse), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> SendNotificationAsync([FromBody] SendNotificationRequest request, CancellationToken cancellationToken)
        {
            SendNotificationCommand command = new(
                    request.Recipient,
                    request.Subject,
                    request.Body,
                    request.Type,
                    request.Priority ?? "high");

            Guid response = await notificationAppService.SendAsync(command,cancellationToken);

            return Accepted(response);
        }
    }
}
