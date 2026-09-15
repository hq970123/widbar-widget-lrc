using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WidBarWidget1.ExtensionApp;

internal sealed class NowPlayingPreview : Grid
{
    private readonly Image _cover;
    private readonly TextBlock _title;
    private readonly TextBlock _lyric;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private string _coverKey = "";

    public event RoutedEventHandler? PreviousClicked;
    public event RoutedEventHandler? PlayPauseClicked;
    public event RoutedEventHandler? NextClicked;

    public NowPlayingPreview(double lyricFontSize)
    {
        Height = 46;
        Padding = new Thickness(4, 3, 4, 3);
        ColumnSpacing = 7;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _cover = new Image
        {
            Width = 38,
            Height = 38,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _cover.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 38, 38), RadiusX = 6, RadiusY = 6 };
        Children.Add(_cover);

        var info = new Grid { VerticalAlignment = VerticalAlignment.Center };
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetColumn(info, 1);

        _title = new TextBlock
        {
            FontSize = 11,
            Opacity = .72,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        info.Children.Add(_title);

        _lyric = new TextBlock
        {
            FontSize = Math.Clamp(lyricFontSize, 11, 16),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        SetRow(_lyric, 1);
        info.Children.Add(_lyric);
        Children.Add(info);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
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
        _title.Text = string.IsNullOrWhiteSpace(artist) ? title : $"{title} · {artist}";
        _lyric.Text = lyric;
        _lyric.FontSize = Math.Clamp(lyricFontSize, 11, 16);
        _playPause.Content = isPlaying ? "\uE769" : "\uE768";
        _previous.IsEnabled = mediaAvailable;
        _playPause.IsEnabled = mediaAvailable;
        _next.IsEnabled = mediaAvailable;
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
            if (_coverKey == coverKey) _cover.Source = bitmap;
        }
        catch
        {
            if (_coverKey == coverKey) _cover.Source = null;
        }
    }

    private static Button CreateMediaButton(string glyph, string tooltip, bool primary = false)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = primary ? 17 : 14,
            Width = primary ? 34 : 29,
            Height = 34,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}