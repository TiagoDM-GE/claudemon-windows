namespace ClaudemonWin.Core;

/// <summary>Classification of <c>claude</c> CLI failures.</summary>
public static class ClaudeDiagnostics
{
    // Deliberately auth-specific: no bare "login" (would match "logging").
    private static readonly string[] AuthNeedles =
    [
        "/login",
        "log in",
        "not logged in",
        "please run",
        "sign in",
        "signin",
        "unauthorized",
        "authentication",
        "authenticate",
        "not authenticated",
        "no subscription",
        "credentials",
        "invalid api key",
        "token has expired",
    ];

    public static bool LooksLikeAuthFailure(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var s = text.ToLowerInvariant();
        return AuthNeedles.Any(s.Contains);
    }
}
