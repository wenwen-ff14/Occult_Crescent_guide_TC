using System.Numerics;
using CrescentCompass.Core;

internal static class WaymarkPreviewChecks
{
    internal static void Run(Action<bool, string> check)
    {
        WaymarkPreset Preset(params SavedWaymark[] marks) => new(Guid.NewGuid(), "Preview", 1252,
            DateTimeOffset.UnixEpoch, marks.Concat(Enumerable.Repeat(SavedWaymark.Off, 8 - marks.Length)).ToArray());
        var first = Preset(new SavedWaymark(700, -481, 366, true), new(710, -480, 395, true));
        check(WaymarkPreview.Detect(first) == 0, "Tower detection uses X/Z, not height Y");
        check(WaymarkPreview.Detect(Preset(new SavedWaymark(0, 0, 0, true))) == -1, "Unrelated presets do not get a tower background");
        check(WaymarkPreview.Detect(first with { Territory = 1346 }) == -1, "North Horn does not use South Horn arenas");
        check(WaymarkPreview.Detect(Preset(new SavedWaymark(700, 0, 379, true), new(-800, 0, 360, true))) == -1,
            "Presets spanning rooms do not silently choose one arena");
        for (int i = 0; i < WaymarkPreview.Arenas.Count; i++)
        {
            var arena = WaymarkPreview.Arenas[i];
            check(WaymarkPreview.Detect(Preset(new SavedWaymark(arena.Center.X, 0, arena.Center.Y, true))) == i, "Detect tower room " + i);
            check(arena.Contains(arena.Center) && !arena.Contains(arena.Center + arena.HalfSize * 2), "Arena bounds " + i);
        }
        var rect = WaymarkPreview.Arenas[0];
        check(rect.Contains(new(715, 412)) && !rect.Contains(new(715.1f, 412)), "Tablet arena is 30 by 66 metres");
        var circle = WaymarkPreview.Arenas[2];
        check(!circle.Contains(circle.Center + new Vector2(29, 29)), "Circle corners are outside even within bounding square");
        var single = WaymarkPreview.Frame(Preset(new SavedWaymark(1, 2, 3, true)), null);
        check(single.Span > 0 && single.Center == new Vector2(1, 3), "Single marker has a finite nonzero preview scale");
        var before = first.Export();
        var frame = WaymarkPreview.Frame(first, rect);
        check(frame.Span >= 66 && first.Export() == before, "Preview frames arena without mutating stored coordinates");
        var outside = Preset(new SavedWaymark(900, 0, 379, true));
        var wide = WaymarkPreview.Frame(outside, rect);
        check(Math.Abs(900 - wide.Center.X) <= wide.Span / 2 && Math.Abs(685 - wide.Center.X) <= wide.Span / 2,
            "Frame includes out-of-arena markers and arena together");
    }
}
