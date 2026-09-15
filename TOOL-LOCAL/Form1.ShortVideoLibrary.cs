using Microsoft.Web.WebView2.Core;
using TOOL_LOCAL.Generation;

namespace TOOL_LOCAL;

public partial class Form1
{
    private async void WebViewOnShortLibraryRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_webView?.CoreWebView2 is not { } web || !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.Host != ShortVideoAssetLibraryService.HostName) return;
        using var deferral = args.GetDeferral();
        try
        {
            var user = _sessionManager?.Current?.User.UserId;
            var org = _generationClient?.SelectedOrganizationId;
            if (args.Request.Method != "GET" || user is null || org is null || _licenseManager?.IsLocked != false || _shortVideoOutfit?.Enabled != true)
                throw new UnauthorizedAccessException();
            var (bytes, mime) = await _shortVideoOutfit.Library.OpenPreviewAsync(user, org.Value, uri, _shutdown.Token);
            if (_sessionManager?.Current?.User.UserId != user || _generationClient?.SelectedOrganizationId != org || _licenseManager.IsLocked)
                throw new UnauthorizedAccessException();
            args.Response = web.Environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK",
                $"Content-Type: {mime}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!_shutdown.IsCancellationRequested)
                args.Response = web.Environment.CreateWebResourceResponse(Stream.Null, 404, "Not Found", "Content-Length: 0\r\nCache-Control: no-store\r\n");
        }
    }
}
