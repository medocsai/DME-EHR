using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient communications and notifications.
/// Handles email, SMS, and in-app notification delivery.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ITenantProvider _tenantProvider;

    public NotificationsController(INotificationService notificationService, ITenantProvider tenantProvider)
    {
        _notificationService = notificationService;
        _tenantProvider = tenantProvider;
    }

    /// <summary>
    /// Sends a no-show notification to a patient via email and SMS.
    /// </summary>
    [HttpPost("no-show/{appointmentId}")]
    public async Task<ActionResult<NotificationResultDto>> SendNoShowNotification(int appointmentId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return BadRequest(new { message = "Tenant context required" });

        var result = await _notificationService.SendNoShowNotificationAsync(appointmentId);

        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        return Ok(result);
    }
}
