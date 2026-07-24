using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

public sealed class EntraAuthOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string TenantId { get; init; } = "organizations";
    public string CallbackPath { get; init; } = "/signin-oidc";
    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";
}

public sealed record AuthMeResponse(string UserId, string? DisplayName, string? Email);

public static class AuthIdentity
{
    public static string? ResolveUserId(ClaimsPrincipal principal)
    {
        var appUserId = principal.FindFirstValue("app_user_id");
        if (!string.IsNullOrWhiteSpace(appUserId)) return appUserId;

        var tenantId = principal.FindFirstValue("tid");
        var objectId = principal.FindFirstValue("oid");
        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(objectId))
        {
            return $"{tenantId}.{objectId}";
        }

        return principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue(ClaimTypes.Upn)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
    }

    public static void AttachAppUserIdClaim(TokenValidatedContext ctx)
    {
        var userId = ResolveUserId(ctx.Principal!);
        if (string.IsNullOrWhiteSpace(userId)) return;
        if (ctx.Principal!.HasClaim(c => c.Type == "app_user_id")) return;

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("app_user_id", userId));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        ctx.Principal.AddIdentity(identity);
    }
}
