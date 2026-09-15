using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WidBarWidget1.ExtensionApp;

internal sealed class LyricsProvider
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ExtraTitleInfo = new(@"\s*[\(\[（【].*?(official|audio|video|lyrics?|mv|live|remaster(?:ed)?|version|visualizer|伴奏|歌词|官方).*?[\)\]）】]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FeatureSuffix = new(@"\s+(?:feat\.?|ft\.?)\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string?> GetSyncedLyricsAsync(string title, string artist, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        title = CleanTitle(title);
        artist = CleanArtist(artist);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var key = $"{title}|{artist}|{Math.Round(duration.TotalSeconds)}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Prefer LRCLIB's exact endpoint, then fall back to search and score candidates.
        var exact = await TryExactAsync(title, artist, duration, cancellationToken);
        if (!string.IsNullOrWhiteSpace(exact)) return _cache[key] = exact;

        var searched = await TrySearchAsync(title, artist, duration, cancellationToken);
        _cache[key] = searched;
        return searched;
    }

    private async Task<string?> TryExactAsync(string title, string artist, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            var query = $"track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrWhiteSpace(artist)) query += $"&artist_name={Uri.EscapeDataString(artist)}";
            if (duration.TotalSeconds > 1) query += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
            using var request = CreateRequest($"https://lrclib.net/api/get?{query}");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadSyncedLyrics(json.RootElement);
        }
        catch { return null; }
    }

    private async Task<string?> TrySearchAsync(string title, string artist, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            using var request = CreateRequest($"https://lrclib.net/api/search?q={Uri.EscapeDataString(q)}");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;

            string? bestLyrics = null;
            var bestScore = double.MinValue;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var lyrics = ReadSyncedLyrics(item);
                if (string.IsNullOrWhiteSpace(lyrics)) continue;
                var candidateTitle = GetString(item, "trackName");
                var candidateArtist = GetString(item, "artistName");
                var candidateDuration = GetDouble(item, "duration");
                var score = Similarity(title, candidateTitle) * 70 + Similarity(artist, candidateArtist) * 20;
                if (duration.TotalSeconds > 1 && candidateDuration > 1)
                {
                    var difference = Math.Abs(duration.TotalSeconds - candidateDuration);
                    score += Math.Max(0, 10 - difference);
                    if (difference > 15) score -= 25;
                }
                if (score > bestScore) { bestScore = score; bestLyrics = lyrics; }
            }
            return bestScore >= 45 ? bestLyrics : null;
        }
        catch { return null; }
    }

    private static string? ReadSyncedLyrics(JsonElement root) => root.TryGetProperty("syncedLyrics", out var value) ? value.GetString() : null;
    private static string GetString(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    private static double GetDouble(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("WidBar-Lyrics/0.3 (https://github.com/hq970123/widbar-widget-lrc)");
        return request;
    }

    private static string CleanTitle(string value)
    {
        var text = CleanCommon(value);
        text = ExtraTitleInfo.Replace(text, " ");
        text = FeatureSuffix.Replace(text, "");
        return Collapse(text);
    }

    private static string CleanArtist(string value)
    {
        var text = CleanCommon(value);
        var separators = new[] { " · Topic", " - Topic" };
        foreach (var suffix in separators)
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) text = text[..^suffix.Length];
        return Collapse(text);
    }

    private static string CleanCommon(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim();
        foreach (var marker in new[] { " - YouTube Music", " - Spotify", " | Spotify", " | YouTube Music" })
            if (text.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) text = text[..^marker.Length].Trim();
        return text;
    }

    private static string Collapse(string value) => Regex.Replace(value, @"\s+", " ").Trim(' ', '-', '|');

    private static double Similarity(string expected, string actual)
    {
        expected = Normalize(expected); actual = Normalize(actual);
        if (string.IsNullOrWhiteSpace(expected)) return string.IsNullOrWhiteSpace(actual) ? 1 : .5;
        if (expected == actual) return 1;
        if (actual.Contains(expected, StringComparison.Ordinal) || expected.Contains(actual, StringComparison.Ordinal)) return .85;
        var expectedWords = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var actualWords = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (expectedWords.Count == 0 || actualWords.Count == 0) return 0;
        var common = expectedWords.Intersect(actualWords).Count();
        return (double)(2 * common) / (expectedWords.Count + actualWords.Count);
    }

    private static string Normalize(string value)
    {
        value = value.ToLowerInvariant();
        value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}