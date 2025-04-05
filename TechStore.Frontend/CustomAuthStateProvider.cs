using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());

    public CustomAuthStateProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var username = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "username");
        if (string.IsNullOrEmpty(username))
            return new AuthenticationState(_anonymous);

        // Build claims list
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username)
        };
        if (username.ToLower() == "superuser")
        {
            claims.Add(new Claim(ClaimTypes.Role, "superuser"));
        }

        var identity = new ClaimsIdentity(claims, "CustomAuth");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task MarkUserAsAuthenticated(string username)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "username", username);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username)
        };
        if (username.ToLower() == "superuser")
        {
            claims.Add(new Claim(ClaimTypes.Role, "superuser"));
        }

        var identity = new ClaimsIdentity(claims, "CustomAuth");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "username");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }
}

