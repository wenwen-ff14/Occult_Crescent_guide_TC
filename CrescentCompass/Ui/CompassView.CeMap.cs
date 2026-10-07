using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private readonly MapViewport ceMapViewport = new();
    private bool ceMapReady;
    private ushort selectedCe = 33;
    private ushort? cePressed;
    private bool ceMapDragged;
    internal Dictionary<string, Vector2> CeTargets { get; } = [];
    internal (Vector2 Origin, Vector2 Size) CeMapArea { get; private set; }
    internal float CeMapZoom => ceMapViewport.Zoom;
    internal Vector2 CeMapCenter => ceMapViewport.Center;
    internal ushort SelectedCe => selectedCe;

    private void DrawCeSelection(CompassViewState state, CompassActions actions, CeCooldownEntry[] rows, DateTimeOffset now)
    {
        var row = rows.FirstOrDefault(r => r.Definition.Id == selectedCe);
        if (row is null || CeMapCatalog.Find(selectedCe) is not { } location) return;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Raised);
        if (ImGui.BeginChild("ce-selection", new Vector2(0, U(105)), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextColored(Sky, location.BossName); ImGui.SameLine();
            ImGui.TextColored(CeColor(row.Status), CeLabel(row, now));
            var canFlag = state.Active && !state.Transit && state.Region.Contains("南") && actions.FlagCeLocation is not null;
            ImGui.TextUnformatted("BOSS 位置"); ImGui.SameLine();
            CoordinateButton("boss-flag", CeMapCatalog.ToMap(location.WorldPosition), canFlag, () => actions.FlagCeLocation?.Invoke(selectedCe, false));
            if (location.TriggerMapPosition is { } trigger)
            {
                ImGui.TextUnformatted($"觸發怪：{row.Definition.TriggerMob}"); ImGui.SameLine();
                CoordinateButton("trigger-flag", trigger, canFlag, () => actions.FlagCeLocation?.Invoke(selectedCe, true));
                HoverHint("觸發怪的大致活動區域；點擊在遊戲地圖插旗。擊殺數未確認，冷卻到期仍需符合觸發條件。");
            }
            else ImGui.TextColored(Muted, "隨時間自動出現 · 無指定觸發怪");
        }
        ImGui.EndChild(); ImGui.PopStyleColor();
    }

    private void CoordinateButton(string key, Vector2 map, bool enabled, Action clicked)
    {
        ImGui.BeginDisabled(!enabled);
        if (ImGui.SmallButton($"X {map.X:F1} / Y {map.Y:F1} ↗##{key}")) clicked();
        CeTargets[key] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
    }

    private void DrawCeMap(CompassViewState state, CompassActions actions, CeCooldownEntry[] rows, DateTimeOffset now)
    {
        var available = ImGui.GetContentRegionAvail();
        var size = new Vector2(available.X, Math.Clamp(Math.Min(available.X, available.Y - U(5)), U(360), U(780)));
        var origin = ImGui.GetCursorScreenPos(); CeMapArea = (origin, size);
        ImGui.InvisibleButton("ce-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        var hovered = ImGui.IsItemHovered(); var io = ImGui.GetIO();
        if (!ceMapReady) { ceMapViewport.Fit([CeMapCatalog.TextureMin, CeMapCatalog.TextureMax]); ceMapReady = true; }
        if (hovered)
        {
            ImGuiP.SetItemUsingMouseWheel();
            if (io.KeyCtrl && io.MouseWheel != 0) ceMapViewport.ZoomAt(io.MouseWheel, ImGui.GetMousePos() - origin, size);
        }
        var active = ImGui.IsItemActive();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) ceMapDragged = false;
        if (active && (ImGui.IsMouseDragging(ImGuiMouseButton.Left) || ImGui.IsMouseDragging(ImGuiMouseButton.Right) ||
            ImGui.IsMouseDragging(ImGuiMouseButton.Middle)))
        {
            ceMapViewport.Pan(io.MouseDelta, size);
            ceMapDragged = true;
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
        Vector2 Project(Vector2 world) => origin + ceMapViewport.Project(world, size);
        var zoom = ceMapViewport.Zoom;
        float M(float value) => U(value) * zoom;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(origin, origin + size, true);
        draw.AddRectFilled(origin, origin + size, Pack(Background), U(8));
        var texture = actions.GetGameTexture?.Invoke(CeMapCatalog.TexturePath) ?? 0;
        if (texture != 0) draw.AddImage(new ImTextureID(texture), Project(CeMapCatalog.TextureMin), Project(CeMapCatalog.TextureMax), Vector2.Zero, Vector2.One);
        else CenterLabel(draw, origin + size / 2, "正在載入新月島地圖…", Muted, 16);
        var locations = rows.Select(r => (Row: r, Location: CeMapCatalog.Find(r.Definition.Id)!)).ToArray();
        var occupied = locations.Select(p => (Min: Project(p.Location.WorldPosition) - new Vector2(M(18)), Max: Project(p.Location.WorldPosition) + new Vector2(M(18)))).ToList();
        CeCooldownEntry? hoverRow = null;
        foreach (var (row, location) in locations)
        {
            CeRowsDrawn++;
            var p = Project(location.WorldPosition);
            if (p.X < origin.X || p.Y < origin.Y || p.X > origin.X + size.X || p.Y > origin.Y + size.Y) continue;
            CeTargets[$"boss-{location.Id}"] = p;
            var color = CeColor(row.Status);
            draw.AddCircleFilled(p, M(18), Pack(Alpha(Background, 0.85f)));
            draw.AddCircle(p, M(18), Pack(selectedCe == location.Id ? Gold : color), 0, M(selectedCe == location.Id ? 2.5f : 1));
            var iconPath = $"ui/icon/{location.IconId / 1000 * 1000:000000}/{location.IconId:000000}.tex";
            var icon = actions.GetGameTexture?.Invoke(iconPath) ?? 0;
            if (icon != 0) draw.AddImage(new ImTextureID(icon), p - new Vector2(M(15)), p + new Vector2(M(15)));
            else CenterLabel(draw, p - new Vector2(0, M(7)), "CE", color, 12 * zoom);
            var status = CeMapLabel(row, now);
            var labelSize = new Vector2(Math.Max(ImGui.CalcTextSize(location.BossName).X * 13 / 17, ImGui.CalcTextSize(status).X * 12 / 17) * zoom + M(12), M(36));
            var labelMin = CeLabelPosition(p, labelSize, origin, size, occupied, zoom);
            var labelMax = labelMin + labelSize; occupied.Add((labelMin - new Vector2(M(2)), labelMax + new Vector2(M(2))));
            draw.AddLine(p, Vector2.Clamp(p, labelMin, labelMax), Pack(Alpha(color, 0.65f)), M(1));
            draw.AddRectFilled(labelMin, labelMax, Pack(Alpha(Background, 0.94f)), M(4));
            Label(draw, labelMin + new Vector2(M(6), M(2)), location.BossName, Text, 13 * zoom);
            Label(draw, labelMin + new Vector2(M(6), M(19)), status, color, 12 * zoom);
            var mouse = ImGui.GetMousePos();
            if (hovered && (Vector2.Distance(mouse, p) <= M(19) || mouse.X >= labelMin.X && mouse.X <= labelMax.X && mouse.Y >= labelMin.Y && mouse.Y <= labelMax.Y)) hoverRow = row;
        }
        if (state.Active && state.Region.Contains("南"))
        {
            var player = Project(new(state.Position.X, state.Position.Z));
            draw.AddCircleFilled(player, M(5), Pack(Mint)); draw.AddCircle(player, M(7), Pack(Background), 0, M(2));
        }
        draw.PopClipRect();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) cePressed = hoverRow?.Definition.Id;
        if (hoverRow is { } item && !ceMapDragged)
        {
            ImGui.SetTooltip($"{item.Definition.Name}\n{CeLabel(item, now)}\n{item.Definition.TriggerCondition}\n上次觀測結束：{item.EndedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "未知"}\n點選查看座標與插旗");
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && cePressed == item.Definition.Id) selectedCe = item.Definition.Id;
        }
        else if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ceMapReady = false;
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) cePressed = null;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right) && !ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            ceMapDragged = false;
    }

    private Vector2 CeLabelPosition(Vector2 p, Vector2 size, Vector2 origin, Vector2 area, List<(Vector2 Min, Vector2 Max)> occupied, float zoom)
    {
        float M(float value) => U(value) * zoom;
        Vector2[] candidates = [new(-size.X / 2, M(22)), new(-size.X / 2, -size.Y - M(22)), new(M(23), -size.Y / 2),
            new(-size.X - M(23), -size.Y / 2), new(-size.X - M(20), M(20)), new(M(20), M(20)), new(-size.X / 2, M(62)), new(-size.X / 2, -size.Y - M(62))];
        var minimum = origin + new Vector2(M(3));
        var maximum = Vector2.Max(minimum, origin + area - size - new Vector2(M(3)));
        var best = Vector2.Zero; var penalty = float.MaxValue;
        foreach (var offset in candidates)
        {
            var min = Vector2.Clamp(p + offset, minimum, maximum);
            var max = min + size;
            var overlap = occupied.Sum(r => Math.Max(0, Math.Min(max.X, r.Max.X) - Math.Max(min.X, r.Min.X)) * Math.Max(0, Math.Min(max.Y, r.Max.Y) - Math.Max(min.Y, r.Min.Y)));
            var score = overlap * 100 + Vector2.DistanceSquared(p, min + size / 2);
            if (score < penalty) { penalty = score; best = min; }
        }
        return best;
    }

    private static Vector4 CeColor(CeStatus status) => status switch
    {
        CeStatus.Register or CeStatus.Warmup or CeStatus.Battle => Mint,
        CeStatus.Cooldown or CeStatus.ConfirmingEnd => Carrot,
        CeStatus.Eligible => Gold,
        _ => Muted,
    };

    private static string CeMapLabel(CeCooldownEntry row, DateTimeOffset now) => row.Status switch
    {
        CeStatus.Cooldown when row.EligibleAt is { } at => "約 " + Countdown(at - now),
        CeStatus.Eligible => "到期 · 待觸發",
        CeStatus.Unknown => "冷卻未知",
        _ => CeLabel(row, now),
    };
}
