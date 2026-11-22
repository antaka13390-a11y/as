using System.Net;
using CyberHub.ServerService.Configuration;
using CyberHub.Shared.Security;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Security;

public sealed class AdminWorkstationMiddleware
{
    private static readonly HashSet<string> AnonymousAdminRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/admin/status",
        "/api/admin/setup",
        "/api/admin/login"
    };

    private readonly RequestDelegate _next;
    private readonly TokenService _tokenService;
    private readonly ServerOptions _options;
    private readonly ILogger<AdminWorkstationMiddleware> _logger;

    public AdminWorkstationMiddleware(
        RequestDelegate next,
        TokenService tokenService,
        IOptions<ServerOptions> options,
        ILogger<AdminWorkstationMiddleware> logger)
    {
        _next = next;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string clientIp = NormalizeIp(context.Connection.RemoteIpAddress);
        string workstation = context.Request.Headers["X-CyberHub-Workstation"].FirstOrDefault() ?? string.Empty;

        if (!IsWorkstationAuthorized(clientIp, workstation))
        {
            _logger.LogWarning("Blocked unauthorized admin API access from IP {Ip} Workstation {Workstation}", clientIp, workstation);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Administrative access is strictly restricted to the designated administration workstation."
            });
            return;
        }

        string path = context.Request.Path.Value ?? string.Empty;
        if (!AnonymousAdminRoutes.Contains(path))
        {
            string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Administrator authentication required." });
                return;
            }

            string token = authHeader["Bearer ".Length..].Trim();
            if (!_tokenService.ValidateToken(token, clientIp, out TokenPayload? payload) || payload is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired administrator session." });
                return;
            }

            context.Items["AdminWorkstation"] = payload.Workstation;
            context.Items["AdminSourceIp"] = clientIp;
        }

        await _next(context);
    }

    public bool IsWorkstationAuthorized(string normalizedIp, string workstationHeader)
    {
        bool ipAllowed = _options.AllowedAdminIps.Any(allowed =>
            string.Equals(NormalizeIpString(allowed), normalizedIp, StringComparison.OrdinalIgnoreCase));

        if (!ipAllowed)
        {
            return false;
        }

        if (_options.AllowedAdminWorkstations.Length == 0)
        {
            return true;
        }

        if (normalizedIp is "127.0.0.1" or "::1")
        {
            return true;
        }

        return _options.AllowedAdminWorkstations.Any(ws =>
            string.Equals(ws, workstationHeader, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeIp(IPAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }
        return address.ToString();
    }

    private static string NormalizeIpString(string raw)
    {
        return IPAddress.TryParse(raw, out IPAddress? ip) ? NormalizeIp(ip) : raw.Trim();
    }
}
