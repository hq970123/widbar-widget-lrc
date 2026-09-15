using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;

namespace WidBarWidget1.ExtensionApp;

internal sealed class NowPlayingPreview : Grid
{
    private readonly Image _cover;
    private readonly Border _coverShell;
    private readonly TextBlock _title;
    private readonly TextBlock _lyric;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
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

        _cover = new Image
        {
            Width = 40,
            Height = 40,
            Stretch = Stretch.UniformToFill
        };
        _coverShell = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = _cover,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _coverShell.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 40, 40), RadiusX = 7, RadiusY = 7 };
        Children.Add(_coverShell);

        var info = new Grid { VerticalAlignment = VerticalAlignment.Center, MinWidth = 170 };
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        SetColumn(info, 1);

        _title = new TextBlock
        {
            FontSize = 11,
            Opacity = .62,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        info.Children.Add(_title);

        _lyric = new TextBlock
        {
            FontSize = Math.Clamp(lyricFontSize, 11, 15),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = .94,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform()
        };
        SetRow(_lyric, 1);
        info.Children.Add(_lyric);
        Children.Add(info);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        SetColumn(controls, 2);

        _previous = CreateMediaButton("\uE892", "Previous track");
        _playPause = CreateMediaButton("\uE768", "Play / pause", true);
        _next = CreateMediaButton("\uE893", "Next track");
        _previous.Click += (s, e) => PreviousClicked?.Invoke(s, e);
        _playPause.Click += (s, e) => PlayPauseClicked?.Invoke(s, e);
        _next.Click += (s, e) => NextClicked?.Invoke(s, e);
        controls.Children.Add(_previous);
        controls.Children.Add(_playPause);
        controls.Children.Add(_next);
        Children.Add(controls);
    }

    public void Update(string title, string artist, string lyric, bool isPlaying, bool mediaAvailable, double lyricFontSize)
    {
        _title.Text = string.IsNullOrWhiteSpace(artist) ? title : $"{title}  ·  {artist}";
        _lyric.FontSize = Math.Clamp(lyricFontSize, 11, 15);
        if (!string.Equals(_lastLyric, lyric, StringComparison.Ordinal))
        {
            _lastLyric = lyric;
            _lyric.Text = lyric;
            AnimateLyricChange();
        }
        _playPause.Content = isPlaying ? "\uE769" : "\uE768";
        _previous.IsEnabled = mediaAvailable;
        _playPause.IsEnabled = mediaAvailable;
        _next.IsEnabled = mediaAvailable;
        Opacity = mediaAvailable ? 1 : .68;
    }

    public async Task SetCoverAsync(IRandomAccessStreamReference? thumbnail, string coverKey)
    {
        if (_coverKey == coverKey) return;
        _coverKey = coverKey;
        if (thumbnail is null)
        {
            _cover.Source = null;
            return;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (_coverKey == coverKey)
            {
                _cover.Source = bitmap;
                AnimateCoverChange();
            }
        }
        catch
        {
            if (_coverKey == coverKey) _cover.Source = null;
        }
    }

    private void AnimateLyricChange()
    {
        if (_lyric.RenderTransform is not TranslateTransform transform) return;
        transform.Y = 3;
        _lyric.Opacity = .35;

        var storyboard = new Storyboard();
        var opacity = new DoubleAnimation
        {
            From = .35,
            To = .94,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacity, _lyric);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var translate = new DoubleAnimation
        {
            From = 3,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(translate, transform);
        Storyboard.SetTargetProperty(translate, "Y");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translate);
        storyboard.Begin();
    }

    private void AnimateCoverChange()
    {
        _cover.Opacity = .3;
        var animation = new DoubleAnimation
        {
            From = .3,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, _cover);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private static Button CreateMediaButton(string glyph, string tooltip, bool primary = false)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = primary ? 18 : 14,
            Width = primary ? 38 : 31,
            Height = primary ? 38 : 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(primary ? 19 : 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = primary ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}