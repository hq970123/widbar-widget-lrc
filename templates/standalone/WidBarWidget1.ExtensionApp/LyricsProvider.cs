using System.Net.Http;
using System.Text.Json;

namespace WidBarWidget1.ExtensionApp;

internal sealed class LyricsProvider
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetSyncedLyricsAsync(string title, string artist, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        title = Clean(title);
        artist = Clean(artist);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var key = $"{title}|{artist}|{Math.Round(duration.TotalSeconds)}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var query = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (duration.TotalSeconds > 1) query += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://lrclib.net/api/get?{query}");
            request.Headers.UserAgent.ParseAdd("WidBar-Lyrics/0.2 (https://github.com/hq970123/widbar-widget-lrc)");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) { _cache[key] = null; return null; }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            var synced = root.TryGetProperty("syncedLyrics", out var value) ? value.GetString() : null;
            _cache[key] = string.IsNullOrWhiteSpace(synced) ? null : synced;
            return _cache[key];
        }
        catch
        {
            _cache[key] = null;
            return null;
        }
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim();
        foreach (var marker in new[] { " - YouTube Music", " - Spotify", " | Spotify" })
            if (text.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) text = text[..^marker.Length].Trim();
        return text;
    }
}