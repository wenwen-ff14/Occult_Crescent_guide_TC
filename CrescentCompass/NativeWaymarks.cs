using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CrescentCompass.Core;

namespace CrescentCompass;

// TC API 13's structs do not expose PlaceFieldMarker. Bind only the audited
// executable; never patch code or assume global-client offsets on TC.
internal sealed unsafe class NativeWaymarks
{
    private sealed record Target(int Rva, string Bytes);
    private sealed record Profile(string GameVersion, string Sha256, int MarkingRva, int FrameworkPointerRva,
        int CollisionOffset, int MarkerOffset, int MarkerStride, Dictionary<string, Target> Targets);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PlaceDelegate(nint self, uint index, Vector3* point);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ClearDelegate(nint self, uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte RaycastDelegate(nint self, byte* hit, Vector3* origin, Vector3* direction, float distance, int layer, int* flags);
    private readonly Profile profile;
    private readonly nint module;
    private readonly PlaceDelegate place;
    private readonly ClearDelegate clear;
    private readonly RaycastDelegate raycast;

    internal NativeWaymarks()
    {
        using var stream = typeof(NativeWaymarks).Assembly.GetManifestResourceStream("CrescentCompass.Data.waymark_client_tc.json")!;
        profile = JsonSerializer.Deserialize<Profile>(stream) ?? throw new InvalidDataException("缺少標點相容性資料。");
        using var process = Process.GetCurrentProcess();
        var main = process.MainModule ?? throw new InvalidOperationException("無法取得遊戲版本。");
        using var executable = File.OpenRead(main.FileName);
        if (Convert.ToHexString(SHA256.HashData(executable)) != profile.Sha256)
            throw new InvalidOperationException($"此版本尚未驗證標點讀取／放置；目前僅適配繁中 {profile.GameVersion}。仍可匯入及管理預設。");
        module = main.BaseAddress;
        VerifyEntries();
        place = Marshal.GetDelegateForFunctionPointer<PlaceDelegate>(module + profile.Targets["Place"].Rva);
        clear = Marshal.GetDelegateForFunctionPointer<ClearDelegate>(module + profile.Targets["Clear"].Rva);
        raycast = Marshal.GetDelegateForFunctionPointer<RaycastDelegate>(module + profile.Targets["Raycast"].Rva);
    }

    private void VerifyEntries()
    {
        foreach (var (name, target) in profile.Targets)
        {
            var expected = Convert.FromHexString(target.Bytes);
            if (!new ReadOnlySpan<byte>((void*)(module + target.Rva), expected.Length).SequenceEqual(expected))
                throw new InvalidOperationException($"標點函式 {name} 已變更，停止讀取／放置。請停用衝突的標點插件後重試。");
        }
    }

    internal SavedWaymark[] Read()
    {
        RequireFramework(); VerifyEntries();
        var markers = new SavedWaymark[8];
        for (int i = 0; i < 8; i++)
        {
            var p = (byte*)(module + profile.MarkingRva + profile.MarkerOffset + profile.MarkerStride * i);
            markers[i] = p[0x1C] == 0 ? SavedWaymark.Off : new(*(int*)(p + 0x10) / 1000f,
                *(int*)(p + 0x14) / 1000f, *(int*)(p + 0x18) / 1000f, true);
        }
        return markers;
    }

    internal SavedWaymark GroundPoint(SavedWaymark marker, Vector3 player)
    {
        RequireFramework();
        if (!marker.Active) return SavedWaymark.Off;
        if (!WaymarkPlacement.WithinRange(marker, player)) throw new InvalidOperationException("標點距離超過 200 公尺，請靠近預設位置再套用。");
        VerifyEntries();
        var framework = *(nint*)(module + profile.FrameworkPointerRva);
        if (framework == 0) throw new InvalidOperationException("遊戲地形尚未就緒。");
        var collision = *(nint*)(framework + profile.CollisionOffset);
        if (collision == 0 || *(byte*)(collision + 1) != 0) throw new InvalidOperationException("遊戲地形正在切換，請稍後重試。");
        byte* hit = stackalloc byte[0x80]; new Span<byte>(hit, 0x80).Clear();
        int* flags = stackalloc int[4] { 0x8004000, 0, 0, 0 };
        var origin = marker.Position + Vector3.UnitY;
        var direction = -Vector3.UnitY;
        if (raycast(collision, hit, &origin, &direction, 2f, 1, flags) == 0)
            throw new InvalidOperationException("標點附近沒有已載入的地面。請走近現場，並核對預設的高度／樓層。");
        return WaymarkGround.Snap(marker, new(*(Vector3*)hit, *(Vector3*)(hit + 0x0C),
            *(Vector3*)(hit + 0x18), *(Vector3*)(hit + 0x24), *(Vector3*)(hit + 0x30)));
    }

    internal byte Apply(WaymarkOperation operation, Vector3 player)
    {
        RequireFramework(); VerifyEntries();
        if (operation.Index is < 0 or > 7) throw new InvalidOperationException("標點編號無效。");
        var self = module + profile.MarkingRva;
        if (!operation.Marker.Active) return clear(self, (uint)operation.Index);
        var checkedPoint = operation.ResolvePoint(marker => GroundPoint(marker, player));
        var point = checkedPoint.Position;
        return place(self, (uint)operation.Index, &point);
    }

    private static void RequireFramework()
    {
        if (!Plugin.Framework.IsInFrameworkUpdateThread) throw new InvalidOperationException("標點操作必須在遊戲更新執行緒執行。");
    }
}
