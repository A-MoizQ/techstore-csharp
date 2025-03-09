using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthState _authState;

    public CustomAuthStateProvider(AuthState authState)
    {
        _authState = authState;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = _authState.IsAuthenticated
            ? new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, _authState.Username ?? ""),
                new Claim(ClaimTypes.Role, _authState.Username == "superuser" ? "superuser" : "user")
            }, "custom")
            : new ClaimsIdentity();

        var user = new ClaimsPrincipal(identity);
        return Task.FromResult(new AuthenticationState(user));
    }

    public void MarkUserAsAuthenticated(string username)
    {
        _authState.Username = username;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void MarkUserAsLoggedOut()
    {
        _authState.Username = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
