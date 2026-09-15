using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WidBarWidget1.ExtensionApp;

internal sealed class NowPlayingFlyout : Grid
{
    private readonly Image _cover;
    private readonly FontIcon _fallback;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _previous;
    private readonly TextBlock _current;
    private readonly TextBlock _next;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _duration;
    private readonly ProgressBar _progress;
    private readonly Button _previousButton;
    private readonly Button _playPauseButton;
    private readonly Button _nextButton;
    private string _coverKey = "";

    public event RoutedEventHandler? PreviousClicked;
    public event RoutedEventHandler? PlayPauseClicked;
    public event RoutedEventHandler? NextClicked;

    public NowPlayingFlyout()
    {
        Padding = new Thickness(22, 18, 22, 20);
        RowSpacing = 14;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var coverGrid = new Grid();
        _fallback = new FontIcon { Glyph = "\uE8D6", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 26, Opacity = .45, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _cover = new Image { Width = 72, Height = 72, Stretch = Stretch.UniformToFill };
        coverGrid.Children.Add(_fallback); coverGrid.Children.Add(_cover);
        var coverBorder = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)), Child = coverGrid };
        header.Children.Add(coverBorder);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        _title = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        _artist = new TextBlock { FontSize = 13, Opacity = .58, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        meta.Children.Add(_title); meta.Children.Add(_artist); Grid.SetColumn(meta, 1); header.Children.Add(meta);
        Children.Add(header);

        var lyrics = new StackPanel { Spacing = 11, VerticalAlignment = VerticalAlignment.Center };
        _previous = Lyric(15, .36); _current = Lyric(23, 1); _current.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _next = Lyric(15, .36);
        lyrics.Children.Add(_previous); lyrics.Children.Add(_current); lyrics.Children.Add(_next); Grid.SetRow(lyrics, 1); Children.Add(lyrics);

        var progressGrid = new Grid { RowSpacing = 3 };
        progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
        progressGrid.Children.Add(_progress);
        var times = new Grid(); times.ColumnDefinitions.Add(new ColumnDefinition()); times.ColumnDefinitions.Add(new ColumnDefinition());
        _elapsed = new TextBlock { FontSize = 11, Opacity = .48, HorizontalAlignment = HorizontalAlignment.Left };
        _duration = new TextBlock { FontSize = 11, Opacity = .48, HorizontalAlignment = HorizontalAlignment.Right };
        times.Children.Add(_elapsed); Grid.SetColumn(_duration, 1); times.Children.Add(_duration); Grid.SetRow(times, 1); progressGrid.Children.Add(times); Grid.SetRow(progressGrid, 2); Children.Add(progressGrid);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
        _previousButton = Button("\uE892", 42, false); _playPauseButton = Button("\uE768", 50, true); _nextButton = Button("\uE893", 42, false);
        _previousButton.Click += (s, e) => PreviousClicked?.Invoke(s, e); _playPauseButton.Click += (s, e) => PlayPauseClicked?.Invoke(s, e); _nextButton.Click += (s, e) => NextClicked?.Invoke(s, e);
        controls.Children.Add(_previousButton); controls.Children.Add(_playPauseButton); controls.Children.Add(_nextButton); Grid.SetRow(controls, 3); Children.Add(controls);
    }

    public void Update(string title, string artist, string previous, string current, string next, TimeSpan position, TimeSpan duration, bool playing, bool enabled)
    {
        _title.Text = string.IsNullOrWhiteSpace(title) ? "Now Playing" : title;
        _artist.Text = string.IsNullOrWhiteSpace(artist) ? "Windows media" : artist;
        _previous.Text = previous; _current.Text = current; _next.Text = next;
        var total = Math.Max(0, duration.TotalSeconds); var elapsed = Math.Clamp(position.TotalSeconds, 0, total > 0 ? total : double.MaxValue);
        _progress.Value = total > 0 ? elapsed / total : 0;
        _elapsed.Text = Format(position); _duration.Text = total > 0 ? Format(duration) : "--:--";
        _playPauseButton.Content = playing ? "\uE769" : "\uE768";
        _previousButton.IsEnabled = enabled; _playPauseButton.IsEnabled = enabled; _nextButton.IsEnabled = enabled;
    }

    public async Task SetCoverAsync(IRandomAccessStreamReference? thumbnail, string key)
    {
        if (_coverKey == key) return; _coverKey = key;
        if (thumbnail is null) { _cover.Source = null; _fallback.Visibility = Visibility.Visible; return; }
        try { using var stream = await thumbnail.OpenReadAsync(); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); if (_coverKey == key) { _cover.Source = bitmap; _fallback.Visibility = Visibility.Collapsed; } }
        catch { if (_coverKey == key) { _cover.Source = null; _fallback.Visibility = Visibility.Visible; } }
    }

    private static TextBlock Lyric(double size, double opacity) => new() { FontSize = size, Opacity = opacity, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Button Button(string glyph, double size, bool primary) => new() { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = primary ? 21 : 17, Width = size, Height = size, Padding = new Thickness(0), CornerRadius = new CornerRadius(size / 2), Background = primary ? new SolidColorBrush(Windows.UI.Color.FromArgb(36, 128, 128, 128)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
    private static string Format(TimeSpan time) { if (time < TimeSpan.Zero) time = TimeSpan.Zero; return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"); }
}