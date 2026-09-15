using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WidBarWidget1.ExtensionApp;

internal sealed class NowPlayingPreview : Grid
{
    private readonly Image _cover;
    private readonly Border _coverShell;
    private readonly FontIcon _coverFallback;
    private readonly TextBlock _title;
    private readonly TextBlock _lyric;
    private readonly Grid _lyricViewport;
    private readonly StackPanel _controls;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private Storyboard? _marqueeStoryboard;
    private string _coverKey = "";
    private string _lastLyric = "";

    public event RoutedEventHandler? PreviousClicked;
    public event RoutedEventHandler? PlayPauseClicked;
    public event RoutedEventHandler? NextClicked;

    public NowPlayingPreview(double lyricFontSize)
    {
        Height = 48;
        Padding = new Thickness(5, 3, 5, 3);
        ColumnSpacing = 8;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _cover = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
        _coverFallback = new FontIcon
        {
            Glyph = "\uE8D6",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 17,
            Opacity = .5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var coverGrid = new Grid();
        coverGrid.Children.Add(_coverFallback);
        coverGrid.Children.Add(_cover);
        _coverShell = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)),
            Child = coverGrid,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new ScaleTransform { CenterX = 20, CenterY = 20 }
        };
        Children.Add(_coverShell);

        var info = new Grid { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        SetColumn(info, 1);

        _title = new TextBlock { FontSize = 11, Opacity = .62, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_title);
        _lyric = new TextBlock
        {
            FontSize = Math.Clamp(lyricFontSize, 11, 15),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = .94,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = new TranslateTransform()
        };
        _lyric.SizeChanged += (_, _) => RestartMarquee();

        _lyricViewport = new Grid { Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        _lyricViewport.Children.Add(_lyric);
        _lyricViewport.SizeChanged += (_, _) => RestartMarquee();
        SetRow(_lyricViewport, 1);
        info.Children.Add(_lyricViewport);
        Children.Add(info);

        _controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Opacity = .9 };
        SetColumn(_controls, 2);
        _previous = CreateMediaButton("\uE892", "Previous track");
        _playPause = CreateMediaButton("\uE768", "Play / pause", true);
        _next = CreateMediaButton("\uE893", "Next track");
        _previous.Click += (s, e) => PreviousClicked?.Invoke(s, e);
        _playPause.Click += (s, e) => PlayPauseClicked?.Invoke(s, e);
        _next.Click += (s, e) => NextClicked?.Invoke(s, e);
        _controls.Children.Add(_previous); _controls.Children.Add(_playPause); _controls.Children.Add(_next);
        Children.Add(_controls);

        SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        _coverShell.PointerEntered += (_, _) => AnimateScale(_coverShell, 1.04);
        _coverShell.PointerExited += (_, _) => AnimateScale(_coverShell, 1);
    }

    public void Update(string title, string artist, string lyric, bool isPlaying, bool mediaAvailable, double lyricFontSize)
    {
        _title.Text = string.IsNullOrWhiteSpace(artist) ? title : $"{title}  ·  {artist}";
        _lyric.FontSize = Math.Clamp(lyricFontSize, 11, 15);
        if (!string.Equals(_lastLyric, lyric, StringComparison.Ordinal)) { _lastLyric = lyric; _lyric.Text = lyric; AnimateLyricChange(); RestartMarquee(); }
        _playPause.Content = isPlaying ? "\uE769" : "\uE768";
        _previous.IsEnabled = mediaAvailable; _playPause.IsEnabled = mediaAvailable; _next.IsEnabled = mediaAvailable;
        Opacity = mediaAvailable ? 1 : .68;
    }

    public async Task SetCoverAsync(IRandomAccessStreamReference? thumbnail, string coverKey)
    {
        if (_coverKey == coverKey) return;
        _coverKey = coverKey;
        if (thumbnail is null) { _cover.Source = null; _coverFallback.Visibility = Visibility.Visible; return; }
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (_coverKey == coverKey) { _cover.Source = bitmap; _coverFallback.Visibility = Visibility.Collapsed; AnimateCoverChange(); }
        }
        catch { if (_coverKey == coverKey) { _cover.Source = null; _coverFallback.Visibility = Visibility.Visible; } }
    }

    private void RestartMarquee()
    {
        _marqueeStoryboard?.Stop(); _marqueeStoryboard = null;
        if (_lyric.RenderTransform is not TranslateTransform transform) return;
        transform.X = 0;
        if (string.IsNullOrWhiteSpace(_lyric.Text) || _lyric.ActualWidth <= _lyricViewport.ActualWidth + 6 || _lyricViewport.ActualWidth <= 0) return;
        var distance = _lyric.ActualWidth - _lyricViewport.ActualWidth + 18;
        var seconds = Math.Clamp(distance / 28.0, 2.8, 10.0);
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), Value = 0 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + .7)), Value = -distance });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + 1.7)), Value = -distance });
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + 1.71)), Value = 0 });
        Storyboard.SetTarget(animation, transform); Storyboard.SetTargetProperty(animation, "X");
        _marqueeStoryboard = new Storyboard(); _marqueeStoryboard.Children.Add(animation); _marqueeStoryboard.Begin();
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0) return;
        if (width < 250) { _previous.Visibility = Visibility.Collapsed; _next.Visibility = Visibility.Collapsed; _title.Visibility = Visibility.Collapsed; _controls.Spacing = 0; }
        else if (width < 315) { _previous.Visibility = Visibility.Collapsed; _next.Visibility = Visibility.Collapsed; _title.Visibility = Visibility.Visible; _controls.Spacing = 0; }
        else { _previous.Visibility = Visibility.Visible; _next.Visibility = Visibility.Visible; _title.Visibility = Visibility.Visible; _controls.Spacing = 2; }
        RestartMarquee();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) { _controls.Opacity = 1; _title.Opacity = .78; }
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) { _controls.Opacity = .9; _title.Opacity = .62; }

    private void AnimateLyricChange()
    {
        if (_lyric.RenderTransform is not TranslateTransform transform) return;
        transform.Y = 3; _lyric.Opacity = .35;
        var storyboard = new Storyboard();
        var opacity = new DoubleAnimation { From = .35, To = .94, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(opacity, _lyric); Storyboard.SetTargetProperty(opacity, "Opacity");
        var translate = new DoubleAnimation { From = 3, To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(translate, transform); Storyboard.SetTargetProperty(translate, "Y");
        storyboard.Children.Add(opacity); storyboard.Children.Add(translate); storyboard.Begin();
    }

    private void AnimateCoverChange()
    {
        _cover.Opacity = .3;
        var animation = new DoubleAnimation { From = .3, To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, _cover); Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard(); storyboard.Children.Add(animation); storyboard.Begin();
    }

    private static void AnimateScale(FrameworkElement element, double to)
    {
        if (element.RenderTransform is not ScaleTransform transform) return;
        var x = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var y = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(x, transform); Storyboard.SetTargetProperty(x, "ScaleX");
        Storyboard.SetTarget(y, transform); Storyboard.SetTargetProperty(y, "ScaleY");
        var storyboard = new Storyboard(); storyboard.Children.Add(x); storyboard.Children.Add(y); storyboard.Begin();
    }

    private static Button CreateMediaButton(string glyph, string tooltip, bool primary = false)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = primary ? 17 : 14,
            Width = primary ? 36 : 30,
            Height = primary ? 36 : 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(primary ? 18 : 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = primary ? new SolidColorBrush(Windows.UI.Color.FromArgb(32, 128, 128, 128)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Opacity = primary ? 1 : .82,
            RenderTransform = new ScaleTransform { CenterX = primary ? 18 : 15, CenterY = primary ? 18 : 16 }
        };
        button.PointerEntered += (_, _) => { button.Opacity = 1; AnimateScale(button, 1.06); };
        button.PointerExited += (_, _) => { button.Opacity = primary ? 1 : .82; AnimateScale(button, 1); };
        button.PointerPressed += (_, _) => AnimateScale(button, .92);
        button.PointerReleased += (_, _) => AnimateScale(button, 1.06);
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}