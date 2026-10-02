using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrescentCompass.Core;

public sealed record SavedWaymark(float X, float Y, float Z, bool Active)
{
    [JsonIgnore] public Vector3 Position => new(X, Y, Z);
    public static SavedWaymark Off => new(0, 0, 0, false);
}

public sealed record WaymarkPreset(Guid Id, string Name, ushort Territory, DateTimeOffset SavedAt, SavedWaymark[] Markers)
{
    public const ushort SouthHorn = 1252;
    public const int ContentId = 1018;
    public static readonly string[] Keys = ["A", "B", "C", "D", "One", "Two", "Three", "Four"];
    public static string Label(int index) => index < 4 ? Keys[index] : (index - 3).ToString();
    public static WaymarkPreset Validate(WaymarkPreset preset)
    {
        if (preset.Id == Guid.Empty || preset.Territory != SouthHorn) throw new InvalidDataException("目前僅支援新月島南部標點。");
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 100 || preset.Name.Any(char.IsControl))
            throw new InvalidDataException("請輸入 1～100 字的標點名稱，不含控制字元。");
        if (preset.Markers is not { Length: 8 } || preset.Markers.Any(m => m is null || !Coordinates.IsFinite(m.Position) ||
                Math.Abs(m.X) > 10000 || Math.Abs(m.Y) > 10000 || Math.Abs(m.Z) > 10000))
            throw new InvalidDataException("標點需包含八個有效座標，且各軸不得超出 ±10000。");
        if (!preset.Markers.Any(m => m.Active)) throw new InvalidDataException("至少需要一個啟用的標點。");
        return preset with { Name = preset.Name.Trim(), Markers = preset.Markers.ToArray() };
    }

    public static WaymarkPreset Import(string text, bool assignUnknownMap = false)
    {
        if (text.Length > 65536) throw new InvalidDataException("匯入內容超過 64 KB，請貼上一組 Waymark Preset JSON。");
        // Accept a copied fenced JSON snippet as well as the raw exported object.
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal))
        {
            int newline = text.IndexOf('\n');
            if (newline < 0) throw new InvalidDataException("JSON 程式碼區塊格式不完整。");
            text = text[(newline + 1)..^3].Trim();
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("請貼上一組 Waymark Preset JSON 物件。");
            static void Unique(JsonElement value)
            {
                if (value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.EnumerateObject().Count())
                    throw new InvalidDataException("JSON 有重複欄位，請確認後重新匯入。");
            }
            Unique(root);
            int map = root.TryGetProperty("MapID", out var mapNode) ? mapNode.GetInt32() : 0;
            if (map != ContentId && !(map == 0 && assignUnknownMap))
                throw new InvalidDataException(map == 0 ? "缺少 MapID。確認這是新月島南部標點後，勾選指定地圖再匯入。"
                    : $"此預設 MapID={map}；新月島南部應為 1018，不能套用其他副本的標點。");
            string name = root.TryGetProperty("Name", out var nameNode) ? nameNode.GetString() ?? "" : "匯入標點";
            var marks = new SavedWaymark[8];
            for (int i = 0; i < 8; i++)
            {
                if (!root.TryGetProperty(Keys[i], out var mark)) { marks[i] = SavedWaymark.Off; continue; }
                if (mark.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"標點 {Label(i)} 格式錯誤。");
                Unique(mark);
                bool active = mark.TryGetProperty("Active", out var activeNode) && activeNode.GetBoolean();
                if (!active) { marks[i] = SavedWaymark.Off; continue; }
                marks[i] = new(mark.GetProperty("X").GetSingle(), mark.GetProperty("Y").GetSingle(), mark.GetProperty("Z").GetSingle(), true);
            }
            return Validate(new(Guid.NewGuid(), name, SouthHorn, DateTimeOffset.UtcNow, marks));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("無法讀取 Waymark Preset JSON；請確認 Name、MapID、XYZ 與 Active 欄位。", ex); }
    }

    public string Export()
    {
        Validate(this);
        var output = new Dictionary<string, object> { ["Name"] = Name, ["MapID"] = ContentId };
        for (int i = 0; i < 8; i++) output[Keys[i]] = new { Markers[i].X, Markers[i].Y, Markers[i].Z, ID = i, Markers[i].Active };
        return JsonSerializer.Serialize(output);
    }
}
