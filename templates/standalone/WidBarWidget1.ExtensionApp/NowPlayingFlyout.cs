using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WidBarWidget1.ExtensionApp;

internal sealed class NowPlayingFlyout : Grid
{
    private readonly Image _cover;
    private readonly Border _coverBorder;
    private readonly FontIcon _fallback;
    private readonly StackPanel _meta;
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
    private string _lastCurrentLyric = "";
    private string _lastTrack = "";

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
        _coverBorder = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)), Child = coverGrid, RenderTransform = new ScaleTransform { CenterX = 36, CenterY = 36 } };
        _coverBorder.PointerEntered += (_, _) => AnimateScale(_coverBorder, 1.035);
        _coverBorder.PointerExited += (_, _) => AnimateScale(_coverBorder, 1);
        header.Children.Add(_coverBorder);

        _meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4, RenderTransform = new TranslateTransform() };
        _title = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        _artist = new TextBlock { FontSize = 13, Opacity = .58, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        _meta.Children.Add(_title); _meta.Children.Add(_artist); Grid.SetColumn(_meta, 1); header.Children.Add(_meta);
        Children.Add(header);

        var lyrics = new StackPanel { Spacing = 11, VerticalAlignment = VerticalAlignment.Center };
        _previous = Lyric(15, .34); _current = Lyric(23, 1); _current.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _current.RenderTransform = new TranslateTransform(); _next = Lyric(15, .34);
        lyrics.Children.Add(_previous); lyrics.Children.Add(_current); lyrics.Children.Add(_next); Grid.SetRow(lyrics, 1); Children.Add(lyrics);

        var progressGrid = new Grid { RowSpacing = 4 };
        progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(2) };
        progressGrid.Children.Add(_progress);
        var times = new Grid(); times.ColumnDefinitions.Add(new ColumnDefinition()); times.ColumnDefinitions.Add(new ColumnDefinition());
        _elapsed = new TextBlock { FontSize = 11, Opacity = .48, HorizontalAlignment = HorizontalAlignment.Left };
        _duration = new TextBlock { FontSize = 11, Opacity = .48, HorizontalAlignment = HorizontalAlignment.Right };
        times.Children.Add(_elapsed); Grid.SetColumn(_duration, 1); times.Children.Add(_duration); Grid.SetRow(times, 1); progressGrid.Children.Add(times); Grid.SetRow(progressGrid, 2); Children.Add(progressGrid);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
        _previousButton = MediaButton("\uE892", 42, false, "Previous track"); _playPauseButton = MediaButton("\uE768", 50, true, "Play / pause"); _nextButton = MediaButton("\uE893", 42, false, "Next track");
        _previousButton.Click += (s, e) => PreviousClicked?.Invoke(s, e); _playPauseButton.Click += (s, e) => PlayPauseClicked?.Invoke(s, e); _nextButton.Click += (s, e) => NextClicked?.Invoke(s, e);
        controls.Children.Add(_previousButton); controls.Children.Add(_playPauseButton); controls.Children.Add(_nextButton); Grid.SetRow(controls, 3); Children.Add(controls);
    }

    public void Update(string title, string artist, string previous, string current, string next, TimeSpan position, TimeSpan duration, bool playing, bool enabled)
    {
        var track = $"{title}|{artist}";
        if (!string.Equals(_lastTrack, track, StringComparison.Ordinal))
        {
            _lastTrack = track; _title.Text = string.IsNullOrWhiteSpace(title) ? "Now Playing" : title; _artist.Text = string.IsNullOrWhiteSpace(artist) ? "Windows media" : artist; AnimateTrackChange();
        }
        _previous.Text = previous; _next.Text = next;
        if (!string.Equals(_lastCurrentLyric, current, StringComparison.Ordinal)) { _lastCurrentLyric = current; _current.Text = current; AnimateLyricChange(); }
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
        try
        {
            using var stream = await thumbnail.OpenReadAsync(); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (_coverKey == key) { _cover.Source = bitmap; _fallback.Visibility = Visibility.Collapsed; AnimateCoverChange(); }
        }
        catch { if (_coverKey == key) { _cover.Source = null; _fallback.Visibility = Visibility.Visible; } }
    }

    private void AnimateLyricChange()
    {
        if (_current.RenderTransform is not TranslateTransform transform) return;
        transform.Y = 7; _current.Opacity = .2;
        var storyboard = new Storyboard();
        AddAnimation(storyboard, _current, "Opacity", .2, 1, 220);
        AddAnimation(storyboard, transform, "Y", 7, 0, 220);
        storyboard.Begin();
    }

    private void AnimateTrackChange()
    {
        if (_meta.RenderTransform is not TranslateTransform transform) return;
        transform.X = 7; _meta.Opacity = .35;
        var storyboard = new Storyboard();
        AddAnimation(storyboard, _meta, "Opacity", .35, 1, 240);
        AddAnimation(storyboard, transform, "X", 7, 0, 240);
        storyboard.Begin();
    }

    private void AnimateCoverChange()
    {
        _cover.Opacity = .25;
        var storyboard = new Storyboard(); AddAnimation(storyboard, _cover, "Opacity", .25, 1, 260); storyboard.Begin();
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(milliseconds), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); storyboard.Children.Add(animation);
    }

    private static void AnimateScale(FrameworkElement element, double to)
    {
        if (element.RenderTransform is not ScaleTransform transform) return;
        var storyboard = new Storyboard(); AddAnimation(storyboard, transform, "ScaleX", transform.ScaleX, to, 120); AddAnimation(storyboard, transform, "ScaleY", transform.ScaleY, to, 120); storyboard.Begin();
    }

    private static TextBlock Lyric(double size, double opacity) => new() { FontSize = size, Opacity = opacity, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static Button MediaButton(string glyph, double size, bool primary, string tooltip)
    {
        var button = new Button
        {
            Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = primary ? 21 : 17, Width = size, Height = size, Padding = new Thickness(0), CornerRadius = new CornerRadius(size / 2),
            Background = primary ? new SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Opacity = primary ? 1 : .8,
            RenderTransform = new ScaleTransform { CenterX = size / 2, CenterY = size / 2 }
        };
        button.PointerEntered += (_, _) => { button.Opacity = 1; AnimateScale(button, 1.06); };
        button.PointerExited += (_, _) => { button.Opacity = primary ? 1 : .8; AnimateScale(button, 1); };
        button.PointerPressed += (_, _) => AnimateScale(button, .92);
        button.PointerReleased += (_, _) => AnimateScale(button, 1.06);
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static string Format(TimeSpan time) { if (time < TimeSpan.Zero) time = TimeSpan.Zero; return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"); }
}