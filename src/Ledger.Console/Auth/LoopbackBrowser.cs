using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using IdentityModel.OidcClient.Browser;

namespace Ledger.Console.Auth;

/// <summary>
/// Opens the system browser on the Keycloak login page and waits for the redirect on a
/// loopback address (OAuth 2.0 for native apps, RFC 8252).
/// </summary>
public sealed class LoopbackBrowser(int port) : IBrowser
{
    public string RedirectUri => $"http://127.0.0.1:{port}/callback/";

    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        OpenBrowser(options.StartUrl);

        var contextTask = listener.GetContextAsync();
        var completed = await Task.WhenAny(contextTask, Task.Delay(options.Timeout, cancellationToken));

        if (completed != contextTask)
            return new BrowserResult { ResultType = BrowserResultType.Timeout, Error = "Login timed out." };

        var context = await contextTask;

        var html = Encoding.UTF8.GetBytes(
            "<html><body style='font-family:sans-serif'><h2>Ledger</h2>" +
            "<p>You are logged in. You can close this window and go back to the console.</p></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html, cancellationToken);
        context.Response.Close();

        return new BrowserResult
        {
            ResultType = BrowserResultType.Success,
            Response = context.Request.Url!.ToString()
        };
    }

    private static void OpenBrowser(string url)
    {
        System.Console.WriteLine($"Opening the browser to log in. If it does not open, visit:\n{url}\n");

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch
        {
            // The URL is printed above.
        }
    }
}
