# WidBar Lyrics

A Windows 11 taskbar lyrics widget for [WidBar](https://apps.microsoft.com/detail/9PKLDNM83TP9), built with WinUI 3, C# and `WidBar.SDK`.

WidBar Lyrics follows the media currently exposed through Windows System Media Transport Controls and displays synchronized LRC lyrics directly on the taskbar. Clicking the widget opens a richer flyout with song information, surrounding lyric lines and playback controls.

## Features

- Live single-line lyrics on the Windows 11 taskbar
- Automatic current song and artist detection through Windows media sessions
- Automatic synchronized lyric lookup with LRCLIB
- Manual LRC fallback when online lyrics are unavailable
- Previous / play-pause / next media controls
- Previous, current and next lyric lines in the flyout
- Adjustable lyric timing offset from -10s to +10s
- Adjustable taskbar lyric font size
- Acrylic flyout UI
- x64 and ARM64 builds

## How it works

1. A supported player exposes its current session to Windows.
2. The widget reads title, artist, playback state, duration and timeline position.
3. When the track changes, the widget attempts to retrieve synchronized LRC lyrics.
4. Lyrics are matched against the current playback position and updated on the taskbar.
5. If synchronized lyrics cannot be found, the configured manual LRC text is used as fallback.

Media-session availability depends on the player. Browsers and music applications that integrate with Windows media controls generally work best.

## Settings

### Sync with Windows current media

Enabled by default. Uses the active Windows media session instead of the widget's manual timer.

### Automatically fetch synced lyrics

Enabled by default. Attempts to find synchronized lyrics for the current title, artist and duration.

### Lyrics timing offset

Adjust from `-10.0` to `+10.0` seconds in 0.1-second increments.

- Positive values show lyrics earlier.
- Negative values show lyrics later.

### Fallback song title

Displayed when no Windows media session is available.

### Fallback / manual LRC lyrics

Paste standard LRC content such as:

```text
[00:10.20]First lyric line
[00:14.80]Second lyric line
[00:18.50]Third lyric line
```

Multiple timestamps on one line are supported.

### Taskbar lyric font size

Adjust the compact taskbar lyric size between 11 and 24.

## Build

### Requirements

- Windows 11
- WidBar
- .NET 10 SDK
- Visual Studio / MSBuild with Windows App SDK tooling
- Developer Mode enabled for local package deployment

Clone the repository and build the standalone solution:

```powershell
git clone https://github.com/hq970123/widbar-widget-lrc.git
cd widbar-widget-lrc
msbuild "templates/standalone/WidBarWidget1.sln" /t:Restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64
msbuild "templates/standalone/WidBarWidget1.sln" /m /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false
```

For ARM64, use:

```text
/p:Platform=ARM64 /p:RuntimeIdentifier=win-arm64
```

## CI builds

Every push to `main` is built on GitHub Actions for both x64 and ARM64. Successful runs upload build artifacts named:

```text
widbar-widget-lrc-x64
widbar-widget-lrc-ARM64
```

Open the repository's **Actions → Windows CI** page to download artifacts from a successful run.

## Current limitations

- Automatic lyrics depend on an external lyrics provider and may not be available for every song.
- Some applications do not expose complete title, artist, duration or playback controls to Windows.
- Track-title formatting from browsers or web players can affect automatic lyric matching.
- The current online lyric cache is in memory and resets when the widget process restarts.

## Project

The standalone widget implementation lives in:

```text
templates/standalone/WidBarWidget1.ExtensionApp/
```

Important files:

- `MainPlugin.cs` — widget UI, media synchronization, LRC parsing and playback controls
- `LyricsProvider.cs` — synchronized lyric lookup
- `WidBarWidget1.ExtensionApp.csproj` — widget metadata and build configuration

## Credits

Built from the [WidBar Widget Template](https://github.com/andelby/widbar-widget-template).

Lyrics lookup currently uses the public LRCLIB service.

## License

MIT. See [LICENSE](LICENSE).
