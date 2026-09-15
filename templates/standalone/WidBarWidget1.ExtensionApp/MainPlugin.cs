using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;
using Windows.Media.Control;

namespace WidBarWidget1.ExtensionApp;

public sealed class MainPlugin : WidgetPluginBase, IConfigurableWidgetPlugin, IWidgetFlyoutLifecycle
{
    private Settings _settings = new();
    private TextBlock? _previewText;
    private TextBlock? _titleText;
    private TextBlock? _previousText;
    private TextBlock? _currentText;
    private TextBlock? _nextText;
    private TextBlock? _statusText;
    private DispatcherTimer? _previewTimer;
    private DispatcherTimer? _flyoutTimer;
    private List<LrcLine> _lyrics = [];
    private DateTime _startedAt = DateTime.Now;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _mediaSession;
    private string _mediaTitle = "";
    private string _mediaArtist = "";
    private TimeSpan _mediaPosition;
    private DateTime _mediaPositionReadAt = DateTime.Now;
    private GlobalSystemMediaTransportControlsSessionPlaybackStatus _playbackStatus;
    private bool _mediaReady;
    private bool _refreshingMedia;

    public override string Id => "com.qiong.widbar.lyrics";
    public override string Name => "Lyrics";
    public override int PreviewLogicalWidth => 320;
    public override int FlyoutWidth => 440;
    public override int FlyoutHeight => 320;
    public override WidgetFlyoutBackdrop FlyoutBackdrop => WidgetFlyoutBackdrop.Acrylic;

    private sealed class Settings
    {
        public string LrcText { get; set; } = "[00:00.00]♪ Lyrics\n[00:03.00]Paste LRC in settings\n[00:07.00]Lyrics will follow playback time";
        public string SongTitle { get; set; } = "Lyrics Widget";
        public double FontSize { get; set; } = 15;
        public bool UseWindowsMediaSession { get; set; } = true;
        public static Settings FromJson(string? json) { try { return string.IsNullOrWhiteSpace(json) ? new Settings() : JsonSerializer.Deserialize<Settings>(json) ?? new Settings(); } catch { return new Settings(); } }
        public string ToJson() => JsonSerializer.Serialize(this);
    }

    private sealed record LrcLine(TimeSpan Time, string Text);

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = Settings.FromJson(context.SettingsJson);
        ParseLyrics();
        _startedAt = DateTime.Now;
        await base.InitializeAsync(context);
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
        if (_settings.UseWindowsMediaSession) await InitializeMediaAsync();
    }

    private async Task InitializeMediaAsync()
    {
        try
        {
            _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _mediaManager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachMediaSession(_mediaManager.GetCurrentSession());
            await RefreshMediaAsync();
        }
        catch { _mediaReady = false; }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        AttachMediaSession(sender.GetCurrentSession());
        _ = RefreshMediaAsync();
    }

    private void AttachMediaSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_mediaSession is not null)
        {
            _mediaSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _mediaSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _mediaSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        _mediaSession = session;
        if (_mediaSession is not null)
        {
            _mediaSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _mediaSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _mediaSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshMediaAsync();
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshMediaAsync();
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshMediaAsync();

    private async Task RefreshMediaAsync()
    {
        if (_refreshingMedia) return;
        _refreshingMedia = true;
        try
        {
            var session = _mediaSession;
            if (session is null) { _mediaReady = false; return; }
            var media = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            _mediaTitle = media.Title ?? "";
            _mediaArtist = media.Artist ?? "";
            _mediaPosition = timeline.Position;
            _mediaPositionReadAt = DateTime.Now;
            _playbackStatus = playback.PlaybackStatus;
            _mediaReady = true;
            RefreshLyrics();
        }
        catch { _mediaReady = false; }
        finally { _refreshingMedia = false; }
    }

    private TimeSpan PlaybackPosition
    {
        get
        {
            if (!_settings.UseWindowsMediaSession || !_mediaReady) return DateTime.Now - _startedAt;
            if (_playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return _mediaPosition + (DateTime.Now - _mediaPositionReadAt);
            return _mediaPosition;
        }
    }

    public override UIElement? CreatePreviewContent()
    {
        _previewText = new TextBlock { Text = CurrentLyric().current, FontSize = _settings.FontSize, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var root = new Grid(); root.Children.Add(_previewText);
        _previewTimer?.Stop(); _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; _previewTimer.Tick += OnTimerTick;
        SetPreviewUpdatesEnabled(Context?.IsPreviewVisible ?? true); return root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _titleText = new TextBlock { FontSize = 14, Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center };
        _statusText = new TextBlock { FontSize = 12, Opacity = .45, HorizontalAlignment = HorizontalAlignment.Center };
        _previousText = LyricText(16, .45); _currentText = LyricText(24, 1); _currentText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _nextText = LyricText(16, .45);
        var panel = new StackPanel { Spacing = 13, Padding = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_titleText); panel.Children.Add(_statusText); panel.Children.Add(_previousText); panel.Children.Add(_currentText); panel.Children.Add(_nextText);
        RefreshLyrics();
        _flyoutTimer?.Stop(); _flyoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; _flyoutTimer.Tick += OnTimerTick;
        return panel;
    }

    private void OnTimerTick(object? sender, object e)
    {
        RefreshLyrics();
        if (_settings.UseWindowsMediaSession && _mediaReady && (DateTime.Now - _mediaPositionReadAt).TotalSeconds > 2) _ = RefreshMediaAsync();
    }

    private static TextBlock LyricText(double size, double opacity) => new() { FontSize = size, Opacity = opacity, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };

    public UIElement? CreateSettingsContent(IWidgetSettingsContext context)
    {
        var draft = Settings.FromJson(context.SettingsJson);
        var mediaToggle = new ToggleSwitch { Header = "Sync with Windows current media", IsOn = draft.UseWindowsMediaSession };
        var title = new TextBox { Header = "Fallback song title", Text = draft.SongTitle };
        var lrc = new TextBox { Header = "LRC lyrics", Text = draft.LrcText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180 };
        var font = new Slider { Header = "Taskbar lyric font size", Minimum = 11, Maximum = 24, Value = draft.FontSize, StepFrequency = 1 };
        void Save() { draft.UseWindowsMediaSession = mediaToggle.IsOn; draft.SongTitle = title.Text; draft.LrcText = lrc.Text; draft.FontSize = font.Value; context.SaveSettings(draft.ToJson()); context.RequestPreviewRefresh(); }
        mediaToggle.Toggled += (_, _) => Save(); title.TextChanged += (_, _) => Save(); lrc.TextChanged += (_, _) => Save(); font.ValueChanged += (_, _) => Save();
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(mediaToggle); panel.Children.Add(title); panel.Children.Add(lrc); panel.Children.Add(font); return panel;
    }

    public override void OnSettingsDraftChanged(string settingsJson)
    {
        var wasMedia = _settings.UseWindowsMediaSession; _settings = Settings.FromJson(settingsJson); ParseLyrics(); _startedAt = DateTime.Now;
        if (_settings.UseWindowsMediaSession && !wasMedia) _ = InitializeMediaAsync();
        if (!_settings.UseWindowsMediaSession) _mediaReady = false;
        if (_previewText is not null) _previewText.FontSize = _settings.FontSize; RefreshLyrics();
    }

    private void ParseLyrics()
    {
        var result = new List<LrcLine>(); var regex = new Regex(@"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
        foreach (var raw in _settings.LrcText.Replace("\r", "").Split('\n'))
        {
            var matches = regex.Matches(raw); if (matches.Count == 0) continue; var text = regex.Replace(raw, "").Trim();
            foreach (Match m in matches) { var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); var sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); var f = m.Groups[3].Success ? m.Groups[3].Value : "0"; var ms = f.Length switch { 1 => int.Parse(f) * 100, 2 => int.Parse(f) * 10, _ => int.Parse(f[..Math.Min(3, f.Length)]) }; result.Add(new LrcLine(TimeSpan.FromMilliseconds((min * 60 + sec) * 1000 + ms), text)); }
        }
        _lyrics = result.OrderBy(x => x.Time).ToList();
    }

    private (string previous, string current, string next) CurrentLyric()
    {
        if (_lyrics.Count == 0) return ("", _mediaReady && !string.IsNullOrWhiteSpace(_mediaTitle) ? $"♪ {_mediaTitle}" : "♪ No lyrics", "");
        var position = PlaybackPosition; var index = _lyrics.FindLastIndex(x => x.Time <= position);
        if (index < 0) return ("", _lyrics[0].Text, _lyrics.Count > 1 ? _lyrics[1].Text : "");
        return (index > 0 ? _lyrics[index - 1].Text : "", _lyrics[index].Text, index + 1 < _lyrics.Count ? _lyrics[index + 1].Text : "");
    }

    private void RefreshLyrics()
    {
        var line = CurrentLyric(); if (_previewText is not null) _previewText.Text = line.current; if (_previousText is not null) _previousText.Text = line.previous; if (_currentText is not null) _currentText.Text = line.current; if (_nextText is not null) _nextText.Text = line.next;
        if (_titleText is not null) _titleText.Text = _mediaReady && !string.IsNullOrWhiteSpace(_mediaTitle) ? string.IsNullOrWhiteSpace(_mediaArtist) ? _mediaTitle : $"{_mediaTitle} · {_mediaArtist}" : _settings.SongTitle;
        if (_statusText is not null) _statusText.Text = _settings.UseWindowsMediaSession ? (_mediaReady ? $"{(_playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Playing" : "Paused")}  {PlaybackPosition:mm\\:ss}" : "Waiting for Windows media…") : $"Manual timer  {PlaybackPosition:mm\\:ss}";
    }

    public void OnFlyoutShown() { RefreshLyrics(); _flyoutTimer?.Start(); if (_settings.UseWindowsMediaSession) _ = RefreshMediaAsync(); }
    public void OnFlyoutHidden() => _flyoutTimer?.Stop();
    private void OnPreviewVisibilityChanged(object? sender, bool visible) => SetPreviewUpdatesEnabled(visible);
    private void SetPreviewUpdatesEnabled(bool visible) { if (visible) { RefreshLyrics(); _previewTimer?.Start(); } else _previewTimer?.Stop(); }

    public override ValueTask DisposeAsync()
    {
        if (Context is not null) Context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;
        if (_mediaManager is not null) _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged;
        AttachMediaSession(null); _previewTimer?.Stop(); _flyoutTimer?.Stop(); return ValueTask.CompletedTask;
    }
}