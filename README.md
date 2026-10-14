<p align="center"><img src="assets/icon.png" width="96" alt="Quiet PC icon"></p>

# Quiet PC

A small Windows tray app that keeps your laptop's fans quiet and cuts Microsoft Edge's memory use.

It was made for **Windows 10 running in Boot Camp on an Intel MacBook Pro**, where the fans tend to roar, but it works on any Windows 10 or 11 PC.

## Download

Get **`QuietPC.exe`** from the [latest release](https://github.com/aduseii/QuietPC/releases/latest), double-click it and choose **Yes**.

Because the app is new and not code-signed, Windows SmartScreen may warn you. Click **More info**, then **Run anyway**.

Quiet PC installs itself to Program Files, starts every time you sign in, and lives in the system tray as a round teal fan icon. If you don't see it, click the **^** arrow at the bottom-right of the taskbar.

## What it does

### Quiet mode (fans)
- **Caps Turbo Boost.** It sets the power plan's maximum processor state to 99%, which switches off Intel Turbo Boost. Turbo is the main reason a MacBook's fans spin up under Windows. The trade-off is that very heavy tasks run somewhat slower, and you can switch this off on its own.
- **Throttles background apps.** Every few seconds, any app you're *not* using that takes more than 15% of a CPU core gets Idle priority and Windows' power-saving mode (EcoQoS). As soon as you click back into that app, it's restored to full speed.
- **Throttles known background hogs.** Search indexing, Windows Update workers, telemetry, and OneDrive, iCloud and Dropbox sync are throttled whenever quiet mode is on.
- **Leaves important things alone.** Windows itself, audio, Defender and the Boot Camp services are never touched. Calls and music (Spotify, Zoom, Teams, Discord, Skype, OBS, VLC) are excluded by default, because throttling them makes audio stutter.

### Edge memory saver
- Turns on sleeping tabs after 5 minutes and keeps Edge's efficiency mode always on.
- Stops Edge from preloading at sign-in (Startup Boost) and from running in the background after you close it.
- When Edge has been in the background for 30 seconds, moves its idle memory out of RAM, then repeats every 5 minutes. **Free Edge memory now** in the tray menu does this on demand.

The Edge settings are applied as Windows policies. They take effect the next time Edge starts, and Edge will show *"Your browser is managed by your organization"* while they're on. Turning the saver off removes them.

## Using it

Right-click the tray icon for all options. Double-click it to switch quiet mode on or off.

| Menu item | What it does |
|---|---|
| Quiet mode | Master switch for fan quieting |
| Cap Turbo Boost (quietest) | Turbo cap on or off |
| Edge memory saver | Edge settings and memory trimming on or off |
| Free Edge memory now | Trims Edge immediately |
| Show throttled apps | Lists what's being slowed right now |
| Edit exclusions... | Apps that should never be throttled, one per line |
| Quit (restores everything) | Stops Quiet PC and puts everything back to normal |
| Uninstall Quiet PC... | Removes it completely |

You can also uninstall it from **Settings > Apps** like any other app. Quitting or uninstalling restores Turbo Boost, every app's priority, and Edge's original settings.

Settings, exclusions and the log are stored in `C:\ProgramData\QuietPC\`. In `settings.txt` you can change how often it checks (`IntervalSec`) and how much CPU counts as hogging (`CpuThreshold`).

## Why it needs admin rights

Admin rights are needed to lower the priority of system processes, change the power plan, and set Edge policies. A sign-in task starts Quiet PC with those rights, so you aren't asked for permission every time you log in.

## Building from source

The app is C# targeting .NET Framework 4.8, which is built into Windows 10. With the .NET SDK installed:

```
dotnet build -c Release
```

The exe ends up in `bin/Release/net48/QuietPC.exe`. Every push to `main` is built on Windows by GitHub Actions and published as a release named after the `<Version>` in `QuietPC.csproj`, so bump the version to publish a new one.

## License

MIT
