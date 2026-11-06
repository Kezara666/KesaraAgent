using Agent.Api.Auth;
using Agent.Core.Devices;

namespace Agent.Api.Endpoints;

public static class DeviceEndpoints
{
    /// <summary>Parameters are short text. Larger payloads are refused before they reach a device.</summary>
    private const int MaxParameterCharacters = 4096;

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/devices").WithTags("Devices");

        group.MapPost("/", RegisterAsync)
            .RequireAuthorization(ApiPolicies.Admin)
            .WithName("RegisterDevice")
            .WithSummary("Register a device and the capabilities it offers. Each capability has a safety tier.");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("ListDevices");

        group.MapGet("/{id}", GetAsync)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("GetDevice");

        group.MapPost("/{id}/commands", CommandAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("SendDeviceCommand")
            .WithSummary("Ask a device to do something. Read-only actions run now. Others wait for approval.");

        return app;
    }

    private static async Task<IResult> RegisterAsync(RegisterDeviceRequest request, IDeviceStore store, CancellationToken cancellationToken)
    {
        if (request.Capabilities is null)
        {
            return Results.Problem(title: "The device needs capabilities.", statusCode: StatusCodes.Status400BadRequest);
        }

        var capabilities = new List<DeviceCapability>();
        foreach (var item in request.Capabilities)
        {
            if (!Enum.TryParse<SafetyTier>(item.Tier, ignoreCase: false, out var tier))
            {
                return Results.Problem(
                    title: "A capability has an unknown safety tier.",
                    detail: "Use ReadOnly, Reversible, or SafetyCritical.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            capabilities.Add(new DeviceCapability(item.Name ?? string.Empty, item.Action ?? string.Empty, tier));
        }

        var device = new DeviceManifest(request.Id ?? string.Empty, request.Name ?? string.Empty, request.Endpoint ?? string.Empty, capabilities);
        var problems = DeviceRegistry.Validate(device);
        if (problems.Count > 0)
        {
            return Results.Problem(title: "The device was refused.", detail: string.Join(' ', problems), statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await store.AddAsync(device, cancellationToken).ConfigureAwait(false))
        {
            return Results.Problem(title: "A device with that id already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Created($"/api/v1/devices/{device.Id}", DeviceResponse.From(device));
    }

    private static async Task<IResult> ListAsync(IDeviceStore store, CancellationToken cancellationToken)
    {
        var devices = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(devices.Select(DeviceResponse.From).ToList());
    }

    private static async Task<IResult> GetAsync(string id, IDeviceStore store, CancellationToken cancellationToken)
    {
        var device = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return device is null
            ? Results.Problem(title: "Device not found.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(DeviceResponse.From(device));
    }

    private static async Task<IResult> CommandAsync(
        string id,
        DeviceCommandRequest request,
        HttpContext httpContext,
        DeviceCommandService service,
        CancellationToken cancellationToken)
    {
        var parameters = request.Parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters.Sum(pair => pair.Key.Length + pair.Value.Length) > MaxParameterCharacters)
        {
            return Results.Problem(title: "The parameters are too large.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var outcome = await service.RequestAsync(
                    id,
                    request.Capability ?? string.Empty,
                    parameters,
                    httpContext.User.Identity?.Name ?? "anonymous",
                    System.Diagnostics.Activity.Current?.TraceId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);

            return outcome switch
            {
                DeviceCommandOutcome.Executed executed => Results.Ok(new DeviceCommandResponse("Executed", executed.Answer, null)),
                DeviceCommandOutcome.PendingApproval pending => Results.Accepted(
                    $"/api/v1/approvals/{pending.ApprovalId}",
                    new DeviceCommandResponse("PendingApproval", null, pending.ApprovalId)),
                _ => Results.Problem(title: "The device returned an unknown outcome.", statusCode: StatusCodes.Status500InternalServerError),
            };
        }
        catch (DeviceNotFoundException)
        {
            return Results.Problem(title: "Device not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(title: "The command was refused.", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (DeviceTransportException exception)
        {
            return Results.Problem(title: "The device did not complete the action.", detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed record RegisterDeviceRequest(string? Id, string? Name, string? Endpoint, IReadOnlyList<CapabilityRequest>? Capabilities);

public sealed record CapabilityRequest(string? Name, string? Action, string? Tier);

public sealed record DeviceCommandRequest(string? Capability, IReadOnlyDictionary<string, string>? Parameters);

public sealed record DeviceResponse(string Id, string Name, string Endpoint, IReadOnlyList<CapabilityResponse> Capabilities)
{
    public static DeviceResponse From(DeviceManifest device) => new(
        device.Id,
        device.Name,
        device.Endpoint,
        device.Capabilities.Select(capability => new CapabilityResponse(capability.Name, capability.Tier.ToString())).ToList());
}

public sealed record CapabilityResponse(string Name, string Tier);

public sealed record DeviceCommandResponse(string Status, string? Answer, Guid? ApprovalId);
