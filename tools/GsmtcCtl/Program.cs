using Windows.Media.Control;

// Verification helper: drives the Apple Music GSMTC session from the command line.
// Usage: GsmtcCtl <status|play|pause|toggle|next|prev|seek <sec>|watchpos <count>>

var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
var s = mgr.GetSessions().FirstOrDefault(x =>
    x.SourceAppUserModelId.StartsWith("AppleInc.AppleMusic", StringComparison.Ordinal));
if (s is null) { Console.WriteLine("no Apple Music session"); return 2; }

string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");

async Task Dump()
{
    var p = await s.TryGetMediaPropertiesAsync();
    var i = s.GetPlaybackInfo();
    var t = s.GetTimelineProperties();
    Console.WriteLine($"{Stamp()} title='{p.Title}' artist='{p.Artist}' status={i.PlaybackStatus} pos={t.Position:hh\\:mm\\:ss} end={t.EndTime:hh\\:mm\\:ss} seekRange=[{t.MinSeekTime}..{t.MaxSeekTime}] posEnabled={i.Controls.IsPlaybackPositionEnabled}");
}

var cmd = args.Length > 0 ? args[0] : "status";
switch (cmd)
{
    case "status": await Dump(); break;
    case "play": Console.WriteLine(await s.TryPlayAsync()); await Dump(); break;
    case "pause": Console.WriteLine(await s.TryPauseAsync()); await Dump(); break;
    case "toggle":
        var st = s.GetPlaybackInfo().PlaybackStatus;
        Console.WriteLine(st == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await s.TryPauseAsync() : await s.TryPlayAsync());
        await Dump(); break;
    case "next": Console.WriteLine(await s.TrySkipNextAsync()); await Dump(); break;
    case "prev": Console.WriteLine(await s.TrySkipPreviousAsync()); await Dump(); break;
    case "seek":
        var v = double.Parse(args[1]);
        var unit = args.Length > 2 ? args[2] : "ticks";
        var raw = unit == "ms" ? (long)(v * 10000.0 / 10000.0) * 1L : 0L;
        // try both interpretations
        if (unit == "rawms")
            Console.WriteLine(await s.TryChangePlaybackPositionAsync((long)(v))); // raw ms value
        else
            Console.WriteLine(await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(v).Ticks));
        await Dump(); break;
    case "watchpos":
        var n = int.Parse(args[1]);
        for (var i2 = 0; i2 < n; i2++)
        {
            var t = s.GetTimelineProperties();
            Console.WriteLine($"{Stamp()} pos={t.Position:mm\\:ss\\.f} end={t.EndTime:mm\\:ss} status={s.GetPlaybackInfo().PlaybackStatus}");
            await Task.Delay(500);
        }
        break;
}
return 0;
