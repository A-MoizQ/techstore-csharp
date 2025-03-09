public class AuthState
{
    public string? Username { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Username);
}