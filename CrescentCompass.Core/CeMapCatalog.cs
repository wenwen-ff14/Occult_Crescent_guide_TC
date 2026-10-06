using System.Numerics;

namespace CrescentCompass.Core;

public sealed record CeMapLocation(ushort Id, string BossName, Vector2 WorldPosition, uint IconId, Vector2? TriggerMapPosition = null);

/// <summary>TC map 967 and DynamicEvent.Location -> planmap.lgb, audited 2026-10-05.</summary>
public static class CeMapCatalog
{
    public const uint MapId = 967;
    public const string TexturePath = "ui/map/o6b1/01/o6b101_m.tex";
    public static readonly Vector2 TextureMin = new(-1024);
    public static readonly Vector2 TextureMax = new(1024);
    public static IReadOnlyList<CeMapLocation> All { get; } = Array.AsReadOnly<CeMapLocation>([
        new(33, "奪心魔", new(300, 730), 63909, new(26, 33)),
        new(34, "黑色連隊", new(450, 357), 63911),
        new(35, "新月狂戰士", new(620, 800), 63909),
        new(36, "死亡厲爪", new(681, 534), 63909),
        new(37, "回廊惡魔", new(-340, 800), 63909, new(14, 35)),
        new(38, "水晶龍", new(-414, 75), 63909),
        new(39, "神秘土偶", new(-800, 245), 63909, new(5, 25)),
        new(40, "石製騎士團", new(680.0022f, -265), 63911),
        new(41, "尼姆瓣齒鯊", new(-117, -850), 63909, new(19, 6)),
        new(42, "躍立獅", new(636, -54), 63909, new(35, 21)),
        new(43, "指令罐", new(-352, -608), 63909),
        new(44, "進化加魯拉", new(461, -363), 63909, new(31, 8)),
        new(45, "金錢龜", new(72, -545), 63909),
        new(46, "復原獅像", new(870.1f, 180), 63909),
        new(47, "鬼火苗", new(-570, -160), 63909),
    ]);

    public static CeMapLocation? Find(ushort id) => All.FirstOrDefault(p => p.Id == id);
    // South Horn map 967: SizeFactor 100, offsets 0. Map-link integer overload uses world * 1000.
    public static Vector2 ToMap(Vector2 world) => world / 50 + new Vector2(21.48f);
    public static Vector2 ToWorld(Vector2 map) => (map - new Vector2(21.48f)) * 50;
    public static Vector2? FlagPosition(ushort id, bool trigger) => Find(id) is not { } point ? null :
        trigger ? point.TriggerMapPosition is { } map ? ToWorld(map) : null : point.WorldPosition;
}
