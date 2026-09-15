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
    private readonly LyricsProvider _lyricsProvider = new();
    private TextBlock? _previewText, _titleText, _previousText, _currentText, _nextText, _statusText;
    private Button? _previousButton, _playPauseButton, _nextButton;
    private DispatcherTimer? _previewTimer, _flyoutTimer;
    private List<LrcLine> _lyrics = [];
    private DateTime _startedAt = DateTime.Now, _mediaPositionReadAt = DateTime.Now;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _mediaSession;
    private string _mediaTitle = "", _mediaArtist = "", _loadedTrackKey = "";
    private TimeSpan _mediaPosition, _mediaDuration;
    private GlobalSystemMediaTransportControlsSessionPlaybackStatus _playbackStatus;
    private bool _mediaReady, _refreshingMedia, _loadingLyrics;

    public override string Id => "com.qiong.widbar.lyrics";
    public override string Name => "Lyrics";
    public override int PreviewLogicalWidth => 320;
    public override int FlyoutWidth => 440;
    public override int FlyoutHeight => 370;
    public override WidgetFlyoutBackdrop FlyoutBackdrop => WidgetFlyoutBackdrop.Acrylic;

    private sealed class Settings
    {
        public string LrcText { get; set; } = "";
        public string SongTitle { get; set; } = "Lyrics Widget";
        public double FontSize { get; set; } = 15;
        public double LyricOffsetSeconds { get; set; }
        public bool UseWindowsMediaSession { get; set; } = true;
        public bool AutoFetchLyrics { get; set; } = true;
        public static Settings FromJson(string? json) { try { return string.IsNullOrWhiteSpace(json) ? new Settings() : JsonSerializer.Deserialize<Settings>(json) ?? new Settings(); } catch { return new Settings(); } }
        public string ToJson() => JsonSerializer.Serialize(this);
    }
    private sealed record LrcLine(TimeSpan Time, string Text);

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = Settings.FromJson(context.SettingsJson); ParseLyrics(_settings.LrcText); _startedAt = DateTime.Now;
        await base.InitializeAsync(context); context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
        if (_settings.UseWindowsMediaSession) await InitializeMediaAsync();
    }

    private async Task InitializeMediaAsync()
    {
        try { _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged; _mediaManager.CurrentSessionChanged += OnCurrentSessionChanged; AttachMediaSession(_mediaManager.GetCurrentSession()); await RefreshMediaAsync(); }
        catch { _mediaReady = false; }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) { AttachMediaSession(sender.GetCurrentSession()); _loadedTrackKey = ""; _ = RefreshMediaAsync(); }
    private void AttachMediaSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_mediaSession is not null) { _mediaSession.MediaPropertiesChanged -= OnMediaPropertiesChanged; _mediaSession.PlaybackInfoChanged -= OnPlaybackInfoChanged; _mediaSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged; }
        _mediaSession = session;
        if (session is not null) { session.MediaPropertiesChanged += OnMediaPropertiesChanged; session.PlaybackInfoChanged += OnPlaybackInfoChanged; session.TimelinePropertiesChanged += OnTimelinePropertiesChanged; }
    }
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshMediaAsync();
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshMediaAsync();
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshMediaAsync();

    private async Task RefreshMediaAsync()
    {
        if (_refreshingMedia) return; _refreshingMedia = true;
        try
        {
            var session = _mediaSession; if (session is null) { _mediaReady = false; return; }
            var media = await session.TryGetMediaPropertiesAsync(); var timeline = session.GetTimelineProperties(); var playback = session.GetPlaybackInfo();
            _mediaTitle = media.Title ?? ""; _mediaArtist = media.Artist ?? ""; _mediaPosition = timeline.Position; _mediaDuration = timeline.EndTime; _mediaPositionReadAt = DateTime.Now; _playbackStatus = playback.PlaybackStatus; _mediaReady = true;
            var trackKey = $"{_mediaTitle}|{_mediaArtist}|{Math.Round(_mediaDuration.TotalSeconds)}";
            if (_settings.AutoFetchLyrics && !string.IsNullOrWhiteSpace(_mediaTitle) && trackKey != _loadedTrackKey) { _loadedTrackKey = trackKey; _ = LoadOnlineLyricsAsync(trackKey); }
            RefreshLyrics();
        }
        catch { _mediaReady = false; }
        finally { _refreshingMedia = false; }
    }

    private async Task LoadOnlineLyricsAsync(string trackKey)
    {
        if (_loadingLyrics) return; _loadingLyrics = true; RefreshLyrics();
        try
        {
            var lrc = await _lyricsProvider.GetSyncedLyricsAsync(_mediaTitle, _mediaArtist, _mediaDuration);
            if (trackKey != _loadedTrackKey) return;
            if (!string.IsNullOrWhiteSpace(lrc)) ParseLyrics(lrc); else ParseLyrics(_settings.LrcText);
        }
        finally { _loadingLyrics = false; RefreshLyrics(); }
    }

    private TimeSpan RawPlaybackPosition => !_settings.UseWindowsMediaSession || !_mediaReady ? DateTime.Now - _startedAt : _playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? _mediaPosition + (DateTime.Now - _mediaPositionReadAt) : _mediaPosition;
    private TimeSpan LyricPlaybackPosition { get { var value = RawPlaybackPosition + TimeSpan.FromSeconds(_settings.LyricOffsetSeconds); return value < TimeSpan.Zero ? TimeSpan.Zero : value; } }

    public override UIElement? CreatePreviewContent()
    {
        _previewText = new TextBlock { Text = CurrentLyric().current, FontSize = _settings.FontSize, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var root = new Grid(); root.Children.Add(_previewText); _previewTimer?.Stop(); _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; _previewTimer.Tick += OnTimerTick; SetPreviewUpdatesEnabled(Context?.IsPreviewVisible ?? true); return root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _titleText = new TextBlock { FontSize = 14, Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center }; _statusText = new TextBlock { FontSize = 12, Opacity = .45, HorizontalAlignment = HorizontalAlignment.Center };
        _previousText = LyricText(16, .45); _currentText = LyricText(24, 1); _currentText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _nextText = LyricText(16, .45);
        _previousButton = MediaButton("⏮", "Previous track"); _playPauseButton = MediaButton("▶", "Play / pause"); _nextButton = MediaButton("⏭", "Next track");
        _previousButton.Click += async (_, _) => await RunMediaCommandAsync(s => s.TrySkipPreviousAsync());
        _playPauseButton.Click += async (_, _) => await RunMediaCommandAsync(s => s.TryTogglePlayPauseAsync());
        _nextButton.Click += async (_, _) => await RunMediaCommandAsync(s => s.TrySkipNextAsync());
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center }; controls.Children.Add(_previousButton); controls.Children.Add(_playPauseButton); controls.Children.Add(_nextButton);
        var panel = new StackPanel { Spacing = 13, Padding = new Thickness(24), VerticalAlignment = VerticalAlignment.Center }; panel.Children.Add(_titleText); panel.Children.Add(_statusText); panel.Children.Add(_previousText); panel.Children.Add(_currentText); panel.Children.Add(_nextText); panel.Children.Add(controls); RefreshLyrics();
        _flyoutTimer?.Stop(); _flyoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; _flyoutTimer.Tick += OnTimerTick; return panel;
    }

    private static Button MediaButton(string content, string tooltip) { var button = new Button { Content = content, MinWidth = 52, MinHeight = 38, FontSize = 18 }; ToolTipService.SetToolTip(button, tooltip); return button; }
    private async Task RunMediaCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        var session = _mediaSession; if (session is null) return;
        try { await command(session); await Task.Delay(100); await RefreshMediaAsync(); } catch { }
    }

    private void OnTimerTick(object? sender, object e) { RefreshLyrics(); if (_settings.UseWindowsMediaSession && _mediaReady && (DateTime.Now - _mediaPositionReadAt).TotalSeconds > 2) _ = RefreshMediaAsync(); }
    private static TextBlock LyricText(double size, double opacity) => new() { FontSize = size, Opacity = opacity, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };

    public UIElement? CreateSettingsContent(IWidgetSettingsContext context)
    {
        var draft = Settings.FromJson(context.SettingsJson); var media = new ToggleSwitch { Header = "Sync with Windows current media", IsOn = draft.UseWindowsMediaSession }; var auto = new ToggleSwitch { Header = "Automatically fetch synced lyrics", IsOn = draft.AutoFetchLyrics };
        var title = new TextBox { Header = "Fallback song title", Text = draft.SongTitle }; var lrc = new TextBox { Header = "Fallback / manual LRC lyrics", Text = draft.LrcText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150 };
        var offset = new NumberBox { Header = "Lyrics timing offset (seconds)", Value = draft.LyricOffsetSeconds, Minimum = -10, Maximum = 10, SmallChange = .1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var offsetHint = new TextBlock { Text = "Positive values show lyrics earlier; negative values show them later.", Opacity = .6, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var font = new Slider { Header = "Taskbar lyric font size", Minimum = 11, Maximum = 24, Value = draft.FontSize, StepFrequency = 1 };
        void Save() { draft.UseWindowsMediaSession = media.IsOn; draft.AutoFetchLyrics = auto.IsOn; draft.SongTitle = title.Text; draft.LrcText = lrc.Text; draft.LyricOffsetSeconds = double.IsNaN(offset.Value) ? 0 : offset.Value; draft.FontSize = font.Value; context.SaveSettings(draft.ToJson()); context.RequestPreviewRefresh(); }
        media.Toggled += (_, _) => Save(); auto.Toggled += (_, _) => Save(); title.TextChanged += (_, _) => Save(); lrc.TextChanged += (_, _) => Save(); offset.ValueChanged += (_, _) => Save(); font.ValueChanged += (_, _) => Save();
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(media); panel.Children.Add(auto); panel.Children.Add(offset); panel.Children.Add(offsetHint); panel.Children.Add(title); panel.Children.Add(lrc); panel.Children.Add(font); return panel;
    }

    public override void OnSettingsDraftChanged(string settingsJson)
    {
        var wasMedia = _settings.UseWindowsMediaSession; _settings = Settings.FromJson(settingsJson); ParseLyrics(_settings.LrcText); _startedAt = DateTime.Now;
        if (_settings.UseWindowsMediaSession && !wasMedia) _ = InitializeMediaAsync(); if (!_settings.UseWindowsMediaSession) _mediaReady = false; if (_settings.AutoFetchLyrics) _loadedTrackKey = "";
        if (_previewText is not null) _previewText.FontSize = _settings.FontSize; RefreshLyrics();
    }

    private void ParseLyrics(string lrcText)
    {
        var result = new List<LrcLine>(); var regex = new Regex(@"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
        foreach (var raw in (lrcText ?? "").Replace("\r", "").Split('\n')) { var matches = regex.Matches(raw); if (matches.Count == 0) continue; var text = regex.Replace(raw, "").Trim(); foreach (Match m in matches) { var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); var sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); var f = m.Groups[3].Success ? m.Groups[3].Value : "0"; var ms = f.Length switch { 1 => int.Parse(f) * 100, 2 => int.Parse(f) * 10, _ => int.Parse(f[..Math.Min(3, f.Length)]) }; result.Add(new LrcLine(TimeSpan.FromMilliseconds((min * 60 + sec) * 1000 + ms), text)); } }
        _lyrics = result.OrderBy(x => x.Time).ToList();
    }

    private (string previous, string current, string next) CurrentLyric()
    {
        if (_loadingLyrics) return ("", "♪ Searching lyrics…", ""); if (_lyrics.Count == 0) return ("", _mediaReady && !string.IsNullOrWhiteSpace(_mediaTitle) ? $"♪ {_mediaTitle}" : "♪ No lyrics", "");
        var index = _lyrics.FindLastIndex(x => x.Time <= LyricPlaybackPosition); if (index < 0) return ("", _lyrics[0].Text, _lyrics.Count > 1 ? _lyrics[1].Text : ""); return (index > 0 ? _lyrics[index - 1].Text : "", _lyrics[index].Text, index + 1 < _lyrics.Count ? _lyrics[index + 1].Text : "");
    }

    private void RefreshLyrics()
    {
        var line = CurrentLyric(); if (_previewText is not null) _previewText.Text = line.current; if (_previousText is not null) _previousText.Text = line.previous; if (_currentText is not null) _currentText.Text = line.current; if (_nextText is not null) _nextText.Text = line.next;
        if (_titleText is not null) _titleText.Text = _mediaReady && !string.IsNullOrWhiteSpace(_mediaTitle) ? string.IsNullOrWhiteSpace(_mediaArtist) ? _mediaTitle : $"{_mediaTitle} · {_mediaArtist}" : _settings.SongTitle;
        var offsetText = Math.Abs(_settings.LyricOffsetSeconds) >= .05 ? $"  Offset {_settings.LyricOffsetSeconds:+0.0;-0.0}s" : "";
        if (_statusText is not null) _statusText.Text = _loadingLyrics ? "Searching synced lyrics…" : _settings.UseWindowsMediaSession ? (_mediaReady ? $"{(_playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Playing" : "Paused")}  {RawPlaybackPosition:mm\\:ss}{offsetText}" : "Waiting for Windows media…") : $"Manual timer  {RawPlaybackPosition:mm\\:ss}{offsetText}";
        if (_playPauseButton is not null) _playPauseButton.Content = _playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "⏸" : "▶";
        var enabled = _settings.UseWindowsMediaSession && _mediaReady; if (_previousButton is not null) _previousButton.IsEnabled = enabled; if (_playPauseButton is not null) _playPauseButton.IsEnabled = enabled; if (_nextButton is not null) _nextButton.IsEnabled = enabled;
    }

    public void OnFlyoutShown() { RefreshLyrics(); _flyoutTimer?.Start(); if (_settings.UseWindowsMediaSession) _ = RefreshMediaAsync(); }
    public void OnFlyoutHidden() => _flyoutTimer?.Stop(); private void OnPreviewVisibilityChanged(object? sender, bool visible) => SetPreviewUpdatesEnabled(visible); private void SetPreviewUpdatesEnabled(bool visible) { if (visible) { RefreshLyrics(); _previewTimer?.Start(); } else _previewTimer?.Stop(); }
    public override ValueTask DisposeAsync() { if (Context is not null) Context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged; if (_mediaManager is not null) _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged; AttachMediaSession(null); _previewTimer?.Stop(); _flyoutTimer?.Stop(); return ValueTask.CompletedTask; }
}