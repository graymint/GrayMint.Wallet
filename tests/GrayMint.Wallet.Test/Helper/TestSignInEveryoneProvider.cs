using System.Security.Claims;
using GrayMint.Authorization.Abstractions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Server.Security;
using Microsoft.Extensions.Options;

namespace GrayMint.Wallet.Test.Helper;

// The server's provider with its first wall down: everyone signs in with the system's code. What a caller may do
// still comes from the server's own provider, so a test sees the permissions hold on their own.
public class TestSignInEveryoneProvider(IOptions<WalletServerOptions> options) : IAuthorizationProvider
{
    private readonly WalletAuthorizationProvider _serverProvider = new(options);

    public Task<string?> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        return Task.FromResult<string?>(options.Value.SystemAuthorizationCode);
    }

    public Task<string?> GetUserId(ClaimsPrincipal principal)
    {
        return _serverProvider.GetUserId(principal);
    }

    public Task OnAuthenticated(ClaimsPrincipal principal)
    {
        return _serverProvider.OnAuthenticated(principal);
    }

    public Task RestAuthorizationCode(ClaimsPrincipal principal)
    {
        return _serverProvider.RestAuthorizationCode(principal);
    }
}
