using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JobForge.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Request is missing the user id claim.");

        return Guid.Parse(value);
    }
}
