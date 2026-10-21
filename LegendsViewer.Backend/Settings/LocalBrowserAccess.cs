namespace LegendsViewer.Backend.Settings;

public static class LocalBrowserAccess
{
    public static readonly string[] Origins = ["http://localhost:15422", "http://localhost:15421",
        "http://localhost:5173", "http://127.0.0.1:15422", "http://127.0.0.1:15421", "http://127.0.0.1:5173"];

    public static bool IsAllowed(HttpRequest request)
    {
        // Host validation also prevents DNS-rebinding requests to the loopback server.
        if (request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]")) return false;
        var origin = request.Headers.Origin.ToString();
        return (origin.Length == 0 || Origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            && request.Headers["Sec-Fetch-Site"] != "cross-site";
    }
}
