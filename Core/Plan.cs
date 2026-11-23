namespace JLModManager.Core;

public record PlanChange(string Action, string Name, string Before, string After);
public record FileChange(string Action,string Path);
public partial class Manager
{
    public static List<FileChange> ComparePackages(Package before,Package after)
    {
        var a=new Dictionary<string,string>(before.Files,StringComparer.OrdinalIgnoreCase);
        var b=new Dictionary<string,string>(after.Files,StringComparer.OrdinalIgnoreCase);
        return a.Keys.Union(b.Keys,StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Where(k=>a.GetValueOrDefault(k)!=b.GetValueOrDefault(k))
            .Select(k=>new FileChange(!a.ContainsKey(k)?"新增":!b.ContainsKey(k)?"移除":"修改",k)).ToList();
    }
    public List<PlanChange> Plan()
    {
        var old=Deployment.Mods;
        var next=Current.Mods.Where(c=>c.Enabled).Select(c=>Package(c.Key)).ToList();
        var result=new List<PlanChange>();
        foreach(var p in next)
        {
            var previous=old.FirstOrDefault(m=>m.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
            if(previous==null) result.Add(new("启用",p.Title,"—",p.Version));
            else if(previous.Key!=p.Key || !Files.Same(previous.Target,Path.Combine(GameMods,p.Folder)))
                result.Add(new("替换",p.Title,Package(previous.Key).Version+" · "+previous.Key[..8],p.Version+" · "+p.Key[..8]));
        }
        foreach(var p in old.Where(m=>!next.Any(n=>n.Name.Equals(m.Name,StringComparison.OrdinalIgnoreCase))))
            result.Add(new("停用",p.Name,Package(p.Key).Version,"—"));
        return result;
    }
    public string RowState(Choice choice)
    {
        var p=Package(choice.Key);var old=Deployment.Mods.FirstOrDefault(m=>m.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
        return !choice.Enabled ? old==null?"未启用":"待停用" : old==null?"待启用" : old.Key==p.Key&&Files.Same(old.Target,Path.Combine(GameMods,p.Folder))?"已部署":"待替换";
    }
}
