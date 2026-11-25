using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JLModManager.Core;

public record CheckItem(string Level,string Mod,string Message);
public class SiteRelease
{
    public string ModVersion {get;set;}="";
    public string[] Games {get;set;}=[];
}
public class SiteMod
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Url {get;set;}="";
    public string CheckedOn {get;set;}="";
    public string[] Required {get;set;}=[];
    public List<SiteRelease> Releases {get;set;}=[];
    public string Note {get;set;}="";
    public override string ToString()=>Name;
}
public static class GameVersions
{
    public static string? Parse(string raw)
    {
        var m=Regex.Match(raw.Trim(),@"^(?:V|v)?([1-9])\.(\d+)(?:\.(\d+))?(?:\s*\(?b(\d+)\)?)?$",RegexOptions.IgnoreCase);
        return m.Success?$"{m.Groups[1].Value}.{m.Groups[2].Value}.{(m.Groups[3].Success?m.Groups[3].Value:"0")}"+(m.Groups[4].Success?" b"+m.Groups[4].Value:""):null;
    }
    public static bool Matches(string version,string pattern)
    {
        var normalized=Parse(version);if(normalized==null)return false;
        var pair=pattern.Split(" b",StringSplitOptions.None);var actual=normalized.Split(" b",StringSplitOptions.None);
        if(pair.Length>1&&(actual.Length<2||pair[1]!=actual[1]))return false;
        var expected=pair[0].Split('.');var parts=actual[0].Split('.');
        if(expected.Length>3)return false;
        for(int i=0;i<expected.Length;i++)if(expected[i]!="*"&&expected[i]!=parts[i])return false;
        return true;
    }
}
public partial class Manager
{
    public List<SiteMod> SiteCatalog()=>File.Exists(Path.Combine(AppContext.BaseDirectory,"Catalog","7risha.json"))?Json.Read<List<SiteMod>>(Path.Combine(AppContext.BaseDirectory,"Catalog","7risha.json")):[];
    public string? ActualGameVersion()
    {
        var path=Path.Combine(Settings.GamePath,"7DaysToDie_Data","buildid.txt");
        if(File.Exists(path)&&GameVersions.Parse(File.ReadAllText(path)) is {} text)return text;
        var exe=Path.Combine(Settings.GamePath,"7DaysToDie.exe");
        return File.Exists(exe)?GameVersions.Parse(FileVersionInfo.GetVersionInfo(exe).ProductVersion??""):null;
    }
    public List<CheckItem> Preflight(List<SiteMod>? catalog=null)
    {
        var result=new List<CheckItem>();
        try{EnsureStopped();_=Review(false);}catch(Exception ex) when(ex is IOException or InvalidOperationException or System.Text.Json.JsonException){result.Add(new("阻止","部署",ex.Message));}
        catalog??=SiteCatalog();var actual=ActualGameVersion();var target=GameVersions.Parse(Current.TargetGameVersion);
        if(actual==null)result.Add(new("提醒","游戏版本",target==null?"无法读取实际版本；请填写目标版本。":"实际版本未知；本次按目标版本 "+target+" 检查。"));
        else if(target!=null&&actual!=target)result.Add(new("提醒","游戏版本",$"实际 {actual}，目标 {target}。"));
        var version=actual??target;
        var packages=Current.Mods.Where(c=>c.Enabled).Select(c=>Package(c.Key)).ToList();
        foreach(var p in packages)
        {
            var id=Settings.SourceBindings.GetValueOrDefault(p.Key);
            var rule=catalog.FirstOrDefault(r=>r.Id==id);
            if(rule==null){result.Add(new("待核实",p.Title,"未关联来源；前置和适配范围未知。"));continue;}
            result.Add(new("待核实",p.Title,"来源为手动关联，未通过网站发布指纹验证。"));
            var releases=rule.Releases.Where(r=>r.ModVersion.Equals(p.Version,StringComparison.OrdinalIgnoreCase)).ToList();
            if(releases.Count==0)result.Add(new("待核实",p.Title,"该发布版本尚未收录。"));
            else if(version!=null&&!releases.Any(r=>r.Games.Any(g=>GameVersions.Matches(version,g))))result.Add(new("提醒",p.Title,"游戏版本超出已收录的作者声明范围。"));
            foreach(var dependency in rule.Required)
            {
                if(dependency=="builtin:Harmony")
                {
                    if(!Scan().Any(i=>i.Native))result.Add(new("阻止",p.Title,"缺少游戏自带 Harmony。"));
                    continue;
                }
                string title=catalog.FirstOrDefault(r=>r.Id==dependency)?.Name??dependency;
                var selected=packages.Any(x=>Settings.SourceBindings.GetValueOrDefault(x.Key)==dependency);
                result.Add(new(selected?"待核实":"提醒",p.Title,selected?"前置 "+title+" 已选择，分支和版本仍需核实。":"缺少已启用的前置关联："+title));
            }
            if(rule.Note.Length>0)result.Add(new("提醒",p.Title,rule.Note));
        }
        return result;
    }
}
