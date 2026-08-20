using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Api.Contracts.Audit;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Common.Security;
using UserManagement.Application.Features.AuditLogs.GetAuditLogs;

namespace UserManagement.Api.Controllers;

/// <summary>
/// The audit trail.
/// </summary>
/// <remarks>
/// Read-only, and there is deliberately no route that writes, edits or deletes an entry. The trail is written
/// by a persistence interceptor, so it records what happened rather than what a caller chose to report.
/// </remarks>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = Policies.ViewAuditLogs)]
[Produces("application/json")]
public sealed class AuditLogsController(
    IQueryHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>> getAuditLogs) : ControllerBase
{
    /// <summary>Lists audit entries, newest first. Admin only.</summary>
    /// <response code="200">A page of audit entries.</response>
    /// <response code="400">Invalid paging, an inverted date range, or an unknown sort field.</response>
    /// <response code="403">The caller is not an administrator.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAuditLogs(
        [FromQuery] AuditLogQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await getAuditLogs.HandleAsync(
            new GetAuditLogsQuery(
                parameters.PageNumber,
                parameters.PageSize,
                parameters.EntityName,
                parameters.EntityId,
                parameters.Action,
                parameters.PerformedByUserId,
                parameters.FromUtc,
                parameters.ToUtc,
                parameters.SortBy,
                parameters.SortDirection),
            cancellationToken);

        return Ok(result);
    }
}
