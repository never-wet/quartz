using System.Net;
using System.Text;
using Quartz.Models;

namespace Quartz.Services;

internal static class NewTabPageBuilder
{
    private static readonly (string Title, string Url)[] DefaultLinks =
    [
        ("Google", "https://www.google.com/"),
        ("YouTube", "https://www.youtube.com/"),
        ("GitHub", "https://github.com/")
    ];

    public static string Build(IEnumerable<Bookmark> bookmarks)
    {
        var palette = ThemeManager.CurrentPalette;
        var quickLinks = bookmarks
            .Where(bookmark => Uri.TryCreate(bookmark.Url, UriKind.Absolute, out var uri) &&
                               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .Select(bookmark => (bookmark.Title, bookmark.Url))
            .Concat(DefaultLinks)
            .DistinctBy(link => link.Url, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        var links = new StringBuilder();
        foreach (var (title, url) in quickLinks)
        {
            var label = string.IsNullOrWhiteSpace(title) ? new Uri(url).Host : title;
            links.Append("<a class=\"quick\" href=\"")
                .Append(WebUtility.HtmlEncode(url))
                .Append("\"><span>")
                .Append(WebUtility.HtmlEncode(label[..Math.Min(label.Length, 28)]))
                .Append("</span><small>")
                .Append(WebUtility.HtmlEncode(new Uri(url).Host))
                .Append("</small></a>");
        }

        return $$$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>New tab</title>
              <style>
                *{box-sizing:border-box} html,body{height:100%;margin:0}
                body{font-family:"Segoe UI",system-ui,sans-serif;background:{{{palette.Canvas}}};color:{{{palette.Text}}};display:grid;place-items:center;padding:32px}
                main{width:min(760px,100%);transform:translateY(-5vh)}
                .brand{display:flex;align-items:center;justify-content:center;gap:12px;margin-bottom:28px}
                .mark{width:36px;height:36px;border:3px solid {{{ThemeManager.CurrentAccentHex}}};border-radius:12px;transform:rotate(45deg);box-shadow:0 0 22px color-mix(in srgb,{{{ThemeManager.CurrentAccentHex}}} 35%,transparent)}
                h1{font-size:34px;letter-spacing:.16em;margin:0;font-weight:650}.theme{color:{{{palette.MutedText}}};font-size:12px;letter-spacing:.12em;text-transform:uppercase;text-align:center;margin-top:8px}
                form{display:flex;gap:10px;background:{{{palette.Surface}}};border:1px solid {{{palette.Border}}};padding:8px;border-radius:16px;box-shadow:0 14px 40px rgba(0,0,0,.12)}
                input{flex:1;min-width:0;background:transparent;color:{{{palette.Text}}};border:0;outline:0;padding:10px 13px;font-size:16px}
                button{border:0;border-radius:10px;background:{{{ThemeManager.CurrentAccentHex}}};color:{{{ThemeManager.CurrentOnAccentHex}}};padding:0 22px;font-size:14px;font-weight:650;cursor:pointer}
                h2{font-size:12px;color:{{{palette.MutedText}}};text-transform:uppercase;letter-spacing:.13em;margin:28px 4px 10px}
                .links{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}
                .quick{display:flex;flex-direction:column;gap:4px;text-decoration:none;color:{{{palette.Text}}};background:{{{palette.Surface}}};border:1px solid {{{palette.Border}}};padding:14px;border-radius:12px;transition:transform .14s,border-color .14s}
                .quick:hover{transform:translateY(-2px);border-color:{{{ThemeManager.CurrentAccentHex}}}}.quick span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:600}.quick small{color:{{{palette.MutedText}}};overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
                @media(max-width:620px){body{padding:20px}.links{grid-template-columns:repeat(2,minmax(0,1fr))}h1{font-size:27px}}
              </style>
            </head>
            <body>
              <main>
                <div class="brand"><div class="mark"></div><div><h1>QUARTZ</h1><div class="theme">{{{WebUtility.HtmlEncode(ThemeManager.CurrentTheme.ToString())}}} workspace</div></div></div>
                <form id="search"><input id="query" autocomplete="off" spellcheck="false" placeholder="Search or enter an address"><button type="submit">Go</button></form>
                <h2>Quick links</h2><div class="links">{{{links}}}</div>
              </main>
              <script>
                document.getElementById('search').addEventListener('submit',event=>{event.preventDefault();const value=document.getElementById('query').value.trim();if(value&&window.CefSharp)CefSharp.PostMessage(value)});
              </script>
            </body>
            </html>
            """;
    }
}
