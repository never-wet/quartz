using System.Net;
using Quartz.Models;

namespace Quartz.Services;

internal static class AddressInterpreter
{
    public static Uri? ToNavigationUri(string? input, SearchEngine searchEngine)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Equals(BrowserSettings.BlankPage, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(BrowserSettings.BlankPage);
        }

        var webUri = ToWebUri(value);
        if (webUri is not null)
        {
            return webUri;
        }

        return new Uri($"{BrowserSettings.GetSearchUrl(searchEngine)}{WebUtility.UrlEncode(value)}");
    }

    public static Uri? ToWebUri(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri) &&
            IsWebScheme(absoluteUri.Scheme))
        {
            return absoluteUri;
        }

        return LooksLikeHost(value) &&
               Uri.TryCreate($"https://{value}", UriKind.Absolute, out var hostUri)
            ? hostUri
            : null;
    }

    private static bool IsWebScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeHost(string value)
    {
        if (value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var hostPart = value.Split('/', '?', '#')[0];
        if (hostPart.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            hostPart.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hostWithoutPort = hostPart.Split(':')[0];
        return hostWithoutPort.Contains('.') ||
               System.Net.IPAddress.TryParse(hostWithoutPort, out _);
    }
}
