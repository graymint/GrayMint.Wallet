using System.Security.Claims;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.PermissionAuthorizations;
using Microsoft.Extensions.Options;

namespace GrayMint.Wallet.Server.Security;

// The server's one caller is the system: a service that holds the server's signing secret, or a token made with
// it. Any other signed-in caller may do nothing.
public class WalletAuthorizationProvider(IOptions<WalletServerOptions> options) : IAuthorizationProvider
{
    public Task<string?> GetUserId(ClaimsPrincipal principal)
    {
        return Task.FromResult(principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    public async Task<string?> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        var userId = await GetUserId(principal);
        return userId == AuthorizationConstants.SystemUserId ? options.Value.SystemAuthorizationCode : null;
    }

    public async Task OnAuthenticated(ClaimsPrincipal principal)
    {
        if (await GetUserId(principal) != AuthorizationConstants.SystemUserId)
            return;

        var claimsIdentity = principal.Identities.First();
        claimsIdentity.AddClaim(PermissionAuthorization.BuildPermissionClaim(AuthorizationConstants.RootResourceId,
            WalletPermissions.Read));
        claimsIdentity.AddClaim(PermissionAuthorization.BuildPermissionClaim(AuthorizationConstants.RootResourceId,
            WalletPermissions.Write));
    }

    // the system's code is in the settings, where whoever runs the server changes it
    public Task RestAuthorizationCode(ClaimsPrincipal principal)
    {
        throw new NotSupportedException("The system's authorization code is changed in the server's settings.");
    }
}
