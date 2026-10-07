using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private void DrawJourney(CompassViewState state, CompassActions actions)
    {
        if (ImGui.GetContentRegionAvail().X >= U(760))
        {
            if (!ImGui.BeginTable("journey", 2, ImGuiTableFlags.NoSavedSettings)) return;
            ImGui.TableSetupColumn("map", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("next", ImGuiTableColumnFlags.WidthFixed, U(237));
            ImGui.TableNextColumn(); DrawMapPanel(state, actions);
            ImGui.TableNextColumn(); DrawNextPanel(state, actions); ImGui.EndTable();
        }
        else { DrawNextPanel(state, actions, true); DrawMapPanel(state, actions); }
    }

    private void DrawMapPanel(CompassViewState state, CompassActions actions)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(12)));
        if (ImGui.BeginChild("map-panel", new Vector2(0, U(340)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextUnformatted("巡查路線"); ImGui.SameLine(); ImGui.TextColored(Muted, state.Route.Count > 0 ? $"/ {state.Route.Count} 站待巡查" : "/ 等待規劃");
            DrawRouteMap(state, actions, new Vector2(ImGui.GetContentRegionAvail().X, U(242)));
            if (ImGui.SmallButton("全圖")) mapContext = null;
            ImGui.SameLine(); ImGui.TextColored(Muted, $"{mapViewport.Zoom:P0} · Ctrl＋滾輪縮放 · 右鍵拖曳");
            ImGui.TextColored(Muted, state.Controls?.ChartMode == true ? "點選插旗 · Ctrl＋點選設定起點" : "點選節點插旗 · 雙擊空白處顯示全圖");
            HoverHint("Ctrl＋滾輪以滑鼠所在位置縮放，右鍵／中鍵拖曳平移；全圖可還原。線條只畫已取得的地形路徑。圖表模式固定 1～68 順序；最短模式另計站點順序。機關與解鎖需自行確認。");
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawNextPanel(CompassViewState state, CompassActions actions, bool compact = false)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(16), U(14)));
        if (ImGui.BeginChild("next-panel", new Vector2(0, U(compact ? 264 : 340)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Mint, state.Controls?.Paused == true ? "已暫停 · 保留目前站" : "下一站");
            if (state.Route.FirstOrDefault() is { } next)
            {
                var color = KindColor(next.Spot.Kind); var origin = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
                draw.AddCircleFilled(origin + new Vector2(U(19), U(20)), U(19), Pack(Alpha(color, 0.13f)));
                DrawPin(draw, origin + new Vector2(U(19), U(20)), U(7), color);
                Label(draw, origin + new Vector2(U(48), U(1)), $"{(next.ChartNumber is { } number ? $"#{number:00} " : "")}{KindName(next.Spot.Kind)}", Text, 24);
                Label(draw, origin + new Vector2(U(48), U(29)), next.Coordinates, Muted, 16);
                ImGui.Dummy(new Vector2(0, U(48)));
                if (next.Spot.Name is { } name)
                {
                    Label(draw, ImGui.GetCursorScreenPos(), name, Text, 16);
                    ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, U(18))); HoverHint(name);
                }
                DrawStatus(next.Status, next.Spot.RequiresTower);
                ImGui.TextColored(Text, next.WalkingDistance is { } distance ? $"{(next.LivePath ? "前往約" : "規劃此段")} {distance:F0} m" : "路段距離待計算");
                if (!compact) { ImGui.Spacing(); ImGui.TextColored(Muted, "旗標會開啟遊戲地圖"); }
                var width = ImGui.GetContentRegionAvail().X;
                if (compact) width = (width - U(10)) / 2;
                if (PrimaryButton("在地圖插旗", new Vector2(width, U(36)))) actions.Flag(next.Spot);
                if (compact) ImGui.SameLine();
                ImGui.BeginDisabled(state.Controls?.Paused == true);
                if (ImGui.Button("已巡查 → 下一站", new Vector2(width, U(36)))) actions.Next();
                if (next.Spot.Kind != SpotKind.Carrot && next.ChartNumber is not null && actions.ConfirmOpened is not null)
                {
                    if (ImGui.SmallButton("手動確認此箱已開")) actions.ConfirmOpened();
                    HoverHint("自動紀錄未捕捉到時使用；這會記錄目前編號為已開箱並切到下一站。空點或只經過請用「已巡查」。");
                }
                ImGui.EndDisabled();
            }
            else
            {
                var finished = state.TotalStops > 0 && Completed(state) + state.SkippedStops >= state.TotalStops;
                ImGui.Spacing(); ImGui.TextUnformatted(finished ? "本輪巡查結束" : state.TotalStops > 0 ? "目前沒有可用站點" : "準備開始探索");
                ImGui.TextColored(Muted, finished ? $"已巡查 {Completed(state)} · 略過 {state.SkippedStops}" : state.TotalStops > 0 ? "靠近目標後可重新規劃。" : "選擇目標，再規劃路線。");
                ImGui.BeginDisabled(state.Planning || state.Controls?.ChartMode == true && !state.Region.Contains("南"));
                ImGui.Spacing(); if (PrimaryButton("規劃巡查路線", new Vector2(-1, U(36)))) actions.Plan();
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawRouteMap(CompassViewState state, CompassActions actions, Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        MapArea = (origin, size);
        ImGui.InvisibleButton("route-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        // Claim the wheel while the map is hovered so Ctrl+wheel cannot scroll the parent window.
        if (hovered) ImGuiP.SetItemUsingMouseWheel();
        var clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var chart = state.Controls is { ChartMode: true };
        var points = chart ? state.Controls!.ChartPoints : state.Route;
        var ground = state.GroundLegs ?? [];
        var playerWorld = new Vector2(state.Position.X, state.Position.Z);
        var bounds = points.Select(p => new Vector2(p.Spot.Position.X, p.Spot.Position.Z))
            .Concat(ground.SelectMany(l => l.Path).Select(p => new Vector2(p.X, p.Z))).Append(playerWorld);
        var context = (state.Controls?.MapRevision ?? state.TotalStops, state.Region, chart);
        if (mapContext != context) { mapViewport.Fit(bounds); mapContext = context; }
        if (hovered && io.KeyCtrl && io.MouseWheel != 0) mapViewport.ZoomAt(io.MouseWheel, ImGui.GetMousePos() - origin, size);
        if (ImGui.IsItemActive() && (ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 0)))
            mapViewport.Pan(io.MouseDelta, size);
        Vector2 Project(Vector3 p) => origin + mapViewport.Project(new(p.X, p.Z), size);
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
        draw.AddRectFilled(origin, origin + size, Pack(Background), U(8));
        var grid = U(32) * MathF.Sqrt(mapViewport.Zoom);
        var gridOrigin = mapViewport.Project(Vector2.Zero, size);
        for (var x = (gridOrigin.X % grid + grid) % grid; x < size.X; x += grid)
            draw.AddLine(origin + new Vector2(x, 0), origin + new Vector2(x, size.Y), Pack(Alpha(Border, 0.24f)));
        for (var y = (gridOrigin.Y % grid + grid) % grid; y < size.Y; y += grid)
            draw.AddLine(origin + new Vector2(0, y), origin + new Vector2(size.X, y), Pack(Alpha(Border, 0.24f)));
        Label(draw, origin + new Vector2(size.X - U(24), U(10)), "N", Muted, 11);
        var north = origin + new Vector2(size.X - U(20), U(34));
        draw.AddTriangleFilled(north + new Vector2(0, -U(9)), north + new Vector2(-U(4), U(2)), north + new Vector2(U(4), U(2)), Pack(Muted));
        if (points.Count == 0)
        {
            DrawCompass(draw, origin + size / 2 - new Vector2(0, U(12)), U(28), Border);
            CenterLabel(draw, origin + size / 2 + new Vector2(0, U(34)), "規劃後顯示巡查順序", Muted, 17);
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) mapContext = null;
            draw.PopClipRect(); return;
        }
        var projected = points.Select(p => Project(p.Spot.Position)).ToArray();
        var hoveredIndex = -1; var hoverDistance = U(12);
        if (hovered)
            for (var i = 0; i < projected.Length; i++)
            {
                var distance = Vector2.Distance(ImGui.GetMousePos(), projected[i]);
                if (distance < hoverDistance) { hoveredIndex = i; hoverDistance = distance; }
            }
        var headId = state.Route.FirstOrDefault()?.Spot.Id;
        foreach (var leg in ground)
        {
            var first = leg.DestinationId == headId;
            for (var j = 1; j < leg.Path.Count; j++)
                draw.AddLine(Project(leg.Path[j - 1]), Project(leg.Path[j]), Pack(Alpha(Mint, first ? 0.85f : 0.30f)), U(first ? 2 : 1.4f));
        }
        var labelBounds = new List<(Vector2 Min, Vector2 Max)>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i]; var p = projected[i]; var head = point.Spot.Id == headId;
            if (p.X < origin.X - U(16) || p.Y < origin.Y - U(16) || p.X > origin.X + size.X + U(16) || p.Y > origin.Y + size.Y + U(16)) continue;
            var complete = point.CarrotWeight is { } currentWeight ? currentWeight == 0 : point.Status is SpotStatus.Visited or SpotStatus.Skipped;
            var color = point.CarrotWeight is { } weight ? weight == 2 ? Mint : weight == 1 ? Gold : Muted : complete ? Muted : KindColor(point.Spot.Kind);
            var selected = chart && point.ChartNumber == state.Controls!.StartNumber;
            if (head || i == hoveredIndex) draw.AddCircleFilled(p, U(12), Pack(Alpha(color, 0.15f)));
            draw.AddCircleFilled(p, U(head ? 5 : 3.5f), Pack(Alpha(color, complete ? 0.3f : point.Status == SpotStatus.Visible ? 1 : 0.75f)));
            if (head || selected) draw.AddCircle(p, U(selected ? 10 : 8), Pack(selected ? Mint : color), 20, U(1));
            if (chart || i < 8 || i == hoveredIndex)
            {
                var text = point.ChartNumber is { } n ? n.ToString("00") : (i + 1).ToString("00");
                var textPosition = p + new Vector2(U(9), -U(11)); var textEnd = textPosition + ImGui.CalcTextSize(text) * 13 / 17;
                if (head || selected || i == hoveredIndex || labelBounds.All(r => textEnd.X < r.Min.X || textPosition.X > r.Max.X || textEnd.Y < r.Min.Y || textPosition.Y > r.Max.Y))
                {
                    draw.AddRectFilled(textPosition - new Vector2(U(2)), textEnd + new Vector2(U(2)), Pack(Alpha(Background, 0.9f)), U(2));
                    Label(draw, textPosition, text, head || selected ? Text : color, 13); labelBounds.Add((textPosition, textEnd));
                }
            }
        }
        var player = Project(state.Position); draw.AddCircleFilled(player, U(11), Pack(Alpha(Mint, 0.15f)));
        draw.AddQuadFilled(player - new Vector2(0, U(6)), player + new Vector2(U(5), 0), player + new Vector2(0, U(6)), player - new Vector2(U(5), 0), Pack(Mint));
        Label(draw, player + new Vector2(U(9), U(7)), "你", Mint, 13); draw.PopClipRect();
        if (hoveredIndex >= 0)
        {
            var point = points[hoveredIndex];
            var title = point.ChartNumber is { } number ? $"圖表 #{number:00}" : $"第 {hoveredIndex + 1} 站";
            ImGui.SetTooltip($"{title} · {point.Spot.Name ?? KindName(point.Spot.Kind)}\n{point.Coordinates}\n{StatusName(point.Status)}" +
                (point.CarrotWeight is { } score ? $"\n搜尋權重：{(score == 0 ? "0" : $"+{score}")}（非精確機率）" : "") + "\n點擊插旗" +
                (chart ? "\nCtrl＋點擊設定起點，再按「從此編號開始」" : ""));
            if (clicked)
            {
                if (chart && io.KeyCtrl && point.ChartNumber is { } startNumber) actions.SetChartStart?.Invoke(startNumber);
                else actions.Flag(point.Spot);
            }
        }
        else if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) mapContext = null;
    }

}
