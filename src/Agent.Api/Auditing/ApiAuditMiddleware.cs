using System.Diagnostics;
using Agent.Core.Audit;

namespace Agent.Api.Auditing;

/// <summary>
/// Writes one audit entry per API call (FR-8.7). Records the caller, method, path, status, and trace ID.
/// Query strings, headers, and bodies are not recorded, so tokens and prompts never reach the log.
/// </summary>
public sealed class ApiAuditMiddleware(RequestDelegate next, TimeProvider clock)
{
    public async Task InvokeAsync(HttpContext context, IAuditLog audit)
    {
        await next(context).ConfigureAwait(false);

        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var actor = context.User.Identity?.Name ?? "anonymous";
        var entry = new AuditEntry(
            Guid.CreateVersion7(),
            clock.GetUtcNow(),
            actor,
            $"http.{context.Request.Method}",
            context.Request.Path.Value ?? string.Empty,
            Tier: null,
            Result: context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TraceId: Activity.Current?.TraceId.ToString());

        await audit.AppendAsync(entry, context.RequestAborted).ConfigureAwait(false);
    }
}
