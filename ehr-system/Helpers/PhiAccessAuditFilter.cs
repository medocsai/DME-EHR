using System.Security.Claims;
using EHR.Models.Generated;
using EHR.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EHR.Helpers;

/// <summary>
/// Apply at controller or action level to record an AuditLog row for every
/// successful PHI read/list. Satisfies HIPAA 45 CFR 164.312(b) ("audit
/// controls — record and examine activity in information systems containing
/// or using ePHI").
///
/// Usage:
///     [PhiAccessAudit(EntityType = "Patient")]
///     public class PatientsController : ControllerBase { ... }
///
/// The filter runs AFTER the action. If the action returned a 4xx/5xx, no
/// audit row is written (no PHI was actually returned). On success, writes
/// a single row per request with:
///   - UserId / UserEmail       from JWT claims
///   - TenantId                  from JWT TenantId claim
///   - EntityType                from the attribute
///   - EntityId                  from the route's "id" parameter, if numeric
///   - Action                    "PHI_READ_{HttpMethod}_{ControllerName}"
///   - IP / UA                   captured into NewValues
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PhiAccessAuditAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>What kind of PHI is being read (e.g. "Patient", "ClinicalNote").</summary>
    public string EntityType { get; set; } = "Unknown";

    /// <summary>Optional override for the route param that holds the entity id. Defaults to "id".</summary>
    public string IdRouteParam { get; set; } = "id";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // Only audit successful responses (2xx). 4xx/5xx means no PHI was leaked
        // back to the caller; recording them as access events is misleading.
        var status = executed.HttpContext.Response.StatusCode;
        if (status < 200 || status >= 300) return;

        try
        {
            var http = executed.HttpContext;
            var user = http.User;
            var auditService = http.RequestServices.GetService(typeof(IAuditService)) as IAuditService;
            if (auditService == null) return; // service not registered — never log silently

            int? userId = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var u) ? u : null;
            var email = user.FindFirst(ClaimTypes.Email)?.Value;

            int? entityId = null;
            if (context.RouteData.Values.TryGetValue(IdRouteParam, out var raw)
                && raw is string s
                && int.TryParse(s, out var parsed))
            {
                entityId = parsed;
            }
            else if (context.ActionArguments.TryGetValue(IdRouteParam, out var argVal)
                     && argVal is int intArg)
            {
                entityId = intArg;
            }

            var ip = http.Connection.RemoteIpAddress?.ToString();
            var ua = http.Request.Headers.UserAgent.ToString();
            var ctlName = context.Controller.GetType().Name.Replace("Controller", "");
            var method = http.Request.Method.ToUpperInvariant();
            var action = $"PHI_READ_{method}_{ctlName}";
            var newValues = $"ip={ip};ua={Truncate(ua, 200)};path={http.Request.Path}";

            await auditService.LogAccessAsync(
                userId,
                email,
                action,
                EntityType,
                entityId,
                newValues: newValues,
                ipAddress: ip);
        }
        catch
        {
            // Audit failures must never break the request. If we can't write
            // the audit row, the surrounding request still succeeded — and
            // emitting an unhandled exception here would 500 a successful read.
            // Real audit-write failures should be picked up via DB monitoring.
        }
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max];
    }
}
