using System.Text.Json;
using System.Text.RegularExpressions;

namespace JLModManager.Core;

public class IdentityRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Version { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string,string> Fingerprints { get; set; } = [];
}
public record IdentityResult(string Status, string Detail, string? RuleId = null)
{
    public override string ToString() => Status + "：" + Detail;
}
public static class Identity
{
    public static List<IdentityRule> Load(string path)
    {
        if (!File.Exists(path)) return [];
        var rules = JsonSerializer.Deserialize<List<IdentityRule>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? throw new InvalidDataException("指纹库为空。");
        if(rules.Any(r=>string.IsNullOrWhiteSpace(r.Id)||string.IsNullOrWhiteSpace(r.Name)||string.IsNullOrWhiteSpace(r.Branch)||string.IsNullOrWhiteSpace(r.Version)||string.IsNullOrWhiteSpace(r.Source)||r.Fingerprints.Count==0) || rules.Select(r=>r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=rules.Count)
            throw new InvalidDataException("指纹规则缺少身份、来源或关键文件，或 ID 重复。");
        foreach(var r in rules)
        {
            if(r.Fingerprints.Keys.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Fingerprints.Count) throw new InvalidDataException("指纹路径重复。");
            foreach(var f in r.Fingerprints)
            {
                foreach(var segment in Normalize(f.Key).Split('/')) Files.SafeSegment(segment);
                if(!Regex.IsMatch(f.Value,"^[a-fA-F0-9]{64}$")) throw new InvalidDataException("无效的 SHA-256 指纹。");
            }
        }
        return rules;
    }
    static string Normalize(string path)=>path.Replace('\\','/');
    public static IdentityResult Identify(Package p, IEnumerable<IdentityRule> rules)
    {
        var family=rules.Where(r=>r.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase)).ToList();
        bool Match(IdentityRule r)=>r.Fingerprints.Count>0 && r.Fingerprints.All(f=>p.Files.Any(x=>Normalize(x.Key).Equals(Normalize(f.Key),StringComparison.OrdinalIgnoreCase)&&x.Value.Equals(f.Value,StringComparison.OrdinalIgnoreCase)))
            // An extra DLL may change the implementation even when known DLLs match.
            && p.Files.Keys.Where(k=>k.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).All(k=>r.Fingerprints.Keys.Any(f=>Normalize(f).Equals(Normalize(k),StringComparison.OrdinalIgnoreCase)));
        var exact=family.Where(Match).ToList();
        if(exact.Count>1) return new("无法确认","指纹命中多个发布规则，需核实资料库。");
        if(exact.Count==1)
        {
            var r=exact[0];
            if(!r.Version.Equals(p.Version,StringComparison.OrdinalIgnoreCase)) return new("无法确认",$"关键文件对应 {r.Branch} {r.Version}，但声明版本为 {p.Version}。");
            return new("已确认",$"{r.Branch} · {r.Version}（关键文件指纹一致；不代表游戏兼容性已验证）",r.Id);
        }
        if(family.Count>0) return new("疑似匹配","名称已收录，但关键文件或版本尚未确认；不能据此认定满足前置。");
        return new("无法确认","尚未收录该 Mod 的发布指纹。");
    }
}
public partial class Manager
{
    public IdentityResult Identify(Package p,bool verify=true)
    {
        try
        {
            var rules=Identity.Load(Path.Combine(AppContext.BaseDirectory,"Catalog","identities.json"));
            // Local additions cannot silently override the bundled rules.
            rules.AddRange(Identity.Load(Path.Combine(Root,"identity-rules.json")));
            if(rules.Select(r=>r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=rules.Count) return new("无法确认","内置与本地规则 ID 重复。");
            var actual=verify?Files.Describe(Payload(p.Key)):p;
            if(!Files.Equal(actual.Files,p.Files)) return new("无法确认","仓库文件已改变，请重新导入。");
            return Identity.Identify(actual,rules);
        }
        catch(Exception e) when(e is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        { return new("无法确认","指纹检查失败："+e.Message); }
    }
}
