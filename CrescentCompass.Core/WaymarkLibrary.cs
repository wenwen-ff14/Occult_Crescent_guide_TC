using System.Text.Json;

namespace CrescentCompass.Core;

public sealed class WaymarkLibrary
{
    private sealed record LibraryFile(int Version, WaymarkPreset[] Presets);
    private readonly string path;
    private WaymarkPreset[] presets = [];
    public IReadOnlyList<WaymarkPreset> Presets => presets;
    public string LoadError { get; private set; } = "";
    public WaymarkLibrary(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("預設檔案為空。");
            if (file.Version != 1 || file.Presets is null) throw new InvalidDataException("預設檔案版本不支援。");
            presets = file.Presets.Select(WaymarkPreset.Validate).ToArray();
            if (presets.Select(p => p.Id).Distinct().Count() != presets.Length) throw new InvalidDataException("預設 ID 重複。");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        { presets = []; LoadError = "無法讀取標點庫，已保留原檔並停止寫入：" + ex.Message; }
    }
    public Guid Add(WaymarkPreset preset)
    {
        preset = WaymarkPreset.Validate(preset);
        if (presets.FirstOrDefault(p => p.Name == preset.Name && p.Territory == preset.Territory && p.Markers.SequenceEqual(preset.Markers)) is { } same) return same.Id;
        if (presets.Any(p => p.Id == preset.Id)) preset = preset with { Id = Guid.NewGuid() };
        Write([preset, .. presets]); return preset.Id;
    }
    public WaymarkPreset Get(Guid id) => presets.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("標點已刪除，請重新選擇。");
    public void Rename(Guid id, string name)
    {
        var renamed = WaymarkPreset.Validate(Get(id) with { Name = name });
        Write(presets.Select(p => p.Id == id ? renamed : p).ToArray());
    }
    public void Delete(Guid id) => Write(presets.Where(p => p.Id != id).ToArray());
    private void Write(WaymarkPreset[] updated)
    {
        if (LoadError.Length > 0) throw new InvalidOperationException(LoadError);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new LibraryFile(1, updated), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            presets = updated;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
