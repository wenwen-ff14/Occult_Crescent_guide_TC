using System.Reflection;
using System.Reflection.PortableExecutable;
using CrescentCompass.Core;
using Iced.Intel;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina" ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
Run(args);
static void Run(string[] args)
{
    if (args.FirstOrDefault() == "--macro-sdk")
    {
        foreach (var type in new[] { typeof(RaptureMacroModule), typeof(RaptureMacroModule.Macro), typeof(RaptureHotbarModule), typeof(AgentMacro) })
        {
            Console.WriteLine($"TYPE {type.Name} SIZE {type.StructLayoutAttribute?.Size:X}");
            foreach (var f in type.GetFields().Where(f => !f.IsStatic && (type != typeof(RaptureHotbarModule) || f.Name.Contains("Loaded"))))
                Console.WriteLine($"FIELD {f.Name}:{f.FieldType} @{f.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()?.Value:X}");
            foreach (var m in type.GetMethods().Where(m => m.Name is "GetMacro" or "SetIcon" or "SetSavePendingFlag" or "SaveFile" or "ReloadMacroSlots" or "IsAgentActive" or "Instance" or "GetFileType"))
                Console.WriteLine($"METHOD {m} {string.Join(';', m.CustomAttributes.Select(a => a.ToString()))}");
        }
        return;
    }
    if (args.Length < 2) throw new ArgumentException("PhantomAudit <sqpack> <ffxiv_dx11.exe> [local-preview-icon-directory]");
    using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
    foreach (var row in game.GetExcelSheet<MacroIcon>()!.Take(4))
        Console.WriteLine($"MACRO ICON {row.RowId}: {string.Join(';', typeof(MacroIcon).GetProperties().Select(p => p.Name + '=' + p.GetValue(row)))}");
    foreach (var job in PhantomJobs.All)
    {
        var row = game.GetExcelSheet<MKDSupportJob>()!.GetRow(job.Id);
        var status = game.GetExcelSheet<Status>()!.GetRow(job.StatusId);
        if (row.Unknown0.ToString() != job.Name || row.Unknown4.ToString() != job.EnglishName || status.Name.ToString() != job.Name || status.Icon != job.IconId)
            throw new InvalidDataException($"Job/status mapping changed: {job.Id}");
        var path = $"ui/icon/{job.IconId / 1000 * 1000:000000}/{job.IconId:000000}.tex";
        var texture = game.GetFile<TexFile>(path) ?? throw new InvalidDataException("Missing texture " + path);
        Console.WriteLine($"PASS {job.Id}: {job.Name} / {job.EnglishName}; Status {job.StatusId}; icon {job.IconId} ({texture.Header.Width}x{texture.Header.Height})");
        if (args.Length >= 3)
        {
            Directory.CreateDirectory(args[2]);
            using var output = new BinaryWriter(File.Create(Path.Combine(args[2], $"{job.IconId}.rgba")));
            var rgba = texture.ImageData.ToArray(); // A8R8G8B8 in little endian = BGRA bytes.
            for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            output.Write((int)texture.Header.Width); output.Write((int)texture.Header.Height); output.Write(rgba);
        }
    }
    var bytes = File.ReadAllBytes(args[1]);
    using var client = new PEReader(new MemoryStream(bytes));
    Console.WriteLine("Client SHA256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    var section = client.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    foreach (var (type, methodName) in new[] { (typeof(AgentMKDSupportJobList), "ChangeSupportJob"), (typeof(PublicContentOccultCrescent), "ChangeSupportJob"),
        (typeof(RaptureMacroModule), "GetMacro"), (typeof(RaptureMacroModule.Macro), "SetIcon"),
        (typeof(RaptureMacroModule), "SetSavePendingFlag"), (typeof(RaptureHotbarModule), "ReloadMacroSlots") })
    {
        var method = type.GetMethod(methodName)!;
        var signature = (string)method.CustomAttributes.Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value!;
        var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
        List<int> matches = [];
        for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } b && bytes[i + j] != b) { match = false; break; }
            if (match) matches.Add(i);
        }
        if (matches.Count != 1) throw new InvalidDataException($"{type.Name}: expected one signature match, found {matches.Count}");
        var raw = matches.Single(); var rva = raw - section.PointerToRawData + section.VirtualAddress;
        if (bytes[raw] == 0xE8) { var offset = 5 + BitConverter.ToInt32(bytes, raw + 1); raw += offset; rva += offset; }
        Console.WriteLine($"PASS {type.Name}.{methodName}: unique SDK signature; target RVA {rva:X}");
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes, raw, 600)); decoder.IP = (ulong)rva;
        for (var i = 0; i < 85; i++) { decoder.Decode(out var ins); Console.WriteLine($"{ins.IP:X}: {ins}"); if (ins.Mnemonic == Mnemonic.Ret) break; }
    }

}
