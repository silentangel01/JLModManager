using System.Text.Json;

namespace JLModManager.Core;

public class Package
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool HasCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Title => DisplayName;
    [System.Text.Json.Serialization.JsonIgnore]
    public string VersionLabel => $"{Version} · {Key[..Math.Min(8, Key.Length)]}";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public override string ToString() => $"{Title}  ·  {VersionLabel}";
}
public class Choice
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
    // Read only for upgrading v0.1.0 data; new profiles do not contain inline file edits.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Configs { get; set; }
}
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认方案";
    public string TargetGameVersion { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<Choice> Mods { get; set; } = [];
    public override string ToString() => Name;
}
public class Settings
{
    public Dictionary<string,string> SourceBindings { get; set; } = [];
    public string GamePath { get; set; } = "";
    public string UserModsPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie", "Mods");
    public string ActiveProfile { get; set; } = "";
    public string DownloadPath { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [new()];
}
public class ManagedMod
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public string Target { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = [];
}
public class Deployment
{
    public string ProfileName { get; set; } = "尚未应用";
    public List<ManagedMod> Mods { get; set; } = [];
}
public class Installed
{
    public string Path { get; set; } = "";
    public Package? Info { get; set; }
    public bool Native { get; set; }
    public bool Managed { get; set; }
    public string Error { get; set; } = "";
    public string DisplayName => Info?.Title ?? System.IO.Path.GetFileName(Path);
    public string Version => Info?.Version ?? "—";
    public string Status => Error.Length > 0 ? Error : Native ? "游戏自带 · 保护" : Managed ? "已接管" : "外部安装";
}
public class Change
{
    public string Target { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Backup { get; set; } = "";
    public bool Existed { get; set; }
    public Dictionary<string,string> Before { get; set; } = [];
    public Dictionary<string,string> After { get; set; } = [];
}
public class Transaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "Prepared";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public Deployment Before { get; set; } = new();
    public Deployment After { get; set; } = new();
    public List<Change> Changes { get; set; } = [];
}
public class PackManifest
{
    public string Format { get; set; } = "JLModManager/1";
    public string Name { get; set; } = "";
    public string GameBuild { get; set; } = "未知";
    public string TargetGameVersion { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<PackItem> Mods { get; set; } = [];
}
public class PackItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Folder { get; set; } = "";
    public Dictionary<string,string> Files { get; set; } = [];
}
public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("无效 JSON：" + path);
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(f, value, Options); f.Flush(true); }
        File.Move(temp, path, true);
    }
}
