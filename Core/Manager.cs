using System.Diagnostics;
using System.IO.Compression;

namespace JLModManager.Core;

public partial class Manager
{
    public string Root { get; }
    public Settings Settings { get; private set; }
    public string Library => Path.Combine(Root,"Library");
    public string Transactions => Path.Combine(Root,"Transactions");
    public Deployment Deployment => File.Exists(StatePath) ? Json.Read<Deployment>(StatePath) : new();
    string StatePath => Path.Combine(Root,"deployment.json");
    public Func<bool> GameRunning { get; set; } = () => Process.GetProcesses().Any(p => { try { return p.ProcessName.Equals("7DaysToDie",StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("7DaysToDieServer",StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("7DaysToDie_EAC",StringComparison.OrdinalIgnoreCase); } finally { p.Dispose(); } });
    // Failure injection is used only by the isolated filesystem tests.
    public Action<string>? Checkpoint { get; set; }
    public Manager(string root)
    {
        Root=Files.Full(root); Files.NoLinks(Root); Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Library); Directory.CreateDirectory(Transactions);
        Settings=File.Exists(Path.Combine(Root,"settings.json")) ? Json.Read<Settings>(Path.Combine(Root,"settings.json")) : new();
        if(Settings.Profiles.Count==0) Settings.Profiles.Add(new());
        if(!Settings.Profiles.Any(p=>p.Id==Settings.ActiveProfile)) Settings.ActiveProfile=Settings.Profiles[0].Id;
        MigrateLegacyConfigs();
    }
    // Upgrade-only compatibility: turn v0.1.0 inline edits into ordinary immutable packages.
    // Runtime deployment and export subsequently copy packages byte for byte.
    void MigrateLegacyConfigs()
    {
        if(!Settings.Profiles.SelectMany(p=>p.Mods).Any(c=>c.Configs?.Count>0)) return;
        using var guard=Lock();
        var updated=Json.Clone(Settings);
        string backup=Path.Combine(Root,"Backups","settings-before-v0.1.1-"+Guid.NewGuid().ToString("N")+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(Path.Combine(Root,"settings.json"),backup,false);
        foreach(var choice in updated.Profiles.SelectMany(p=>p.Mods))
        {
            if(choice.Configs?.Count>0)
            {
                var package=Package(choice.Key);
                if(!Files.Equal(package.Files,Files.Snapshot(Payload(choice.Key)))) throw new IOException("旧版方案迁移失败：原始包已被修改。");
                string temp=Path.Combine(Root,"Import",Guid.NewGuid().ToString("N"));
                string content=Files.Under(temp,package.Folder);
                try
                {
                    Files.Copy(Payload(choice.Key),content);
                    foreach(var kv in choice.Configs)
                    {
                        var target=Files.Under(content,kv.Key);
                        if(!File.Exists(target)) throw new IOException("旧版方案引用了不存在的配置文件："+kv.Key);
                        File.WriteAllText(target,kv.Value);
                    }
                    choice.Key=ImportOne(content).Key;
                }
                finally { Files.DeleteOwn(temp,Root); }
            }
            choice.Configs=null;
        }
        Json.Write(Path.Combine(Root,"settings.json"),updated);
        Settings=updated;
        Log("Migrated legacy inline settings into immutable package snapshots; original settings backed up.");
    }
    public Profile Current => Settings.Profiles.Single(p=>p.Id==Settings.ActiveProfile);
    public void Save() => Json.Write(Path.Combine(Root,"settings.json"),Settings);
    public void Log(string message)
    {
        Directory.CreateDirectory(Path.Combine(Root,"Logs"));
        File.AppendAllText(Path.Combine(Root,"Logs",DateTime.Now.ToString("yyyy-MM-dd")+".log"),DateTime.Now.ToString("O")+" "+message+Environment.NewLine);
    }
    IDisposable Lock() => new OperationLock(Root,Settings.GamePath);
    public void EnsureStopped() { if(GameRunning()) throw new InvalidOperationException("游戏或服务器仍在运行。退出后才能接管、部署、恢复或备份存档。"); }
    public string GameMods => Path.Combine(Settings.GamePath,"Mods");
    public void ValidateGame()
    {
        if(string.IsNullOrWhiteSpace(Settings.GamePath) || !File.Exists(Path.Combine(Settings.GamePath,"7DaysToDie.exe"))) throw new InvalidOperationException("请选择含 7DaysToDie.exe 的游戏安装目录。");
        Files.NoLinks(Settings.GamePath); Files.NoLinks(Settings.UserModsPath);
        if(!Path.GetFileName(Files.Full(Settings.UserModsPath)).Equals("Mods",StringComparison.OrdinalIgnoreCase)) throw new IOException("用户 Mod 目录应是 UserDataFolder 下的 Mods 文件夹。");
        if(Files.Within(Root,GameMods) || Files.Same(Root,GameMods) || Files.Within(Root,Settings.UserModsPath) || Files.Same(Root,Settings.UserModsPath)) throw new IOException("管理器数据仓库不能放在 Mods 内。");
    }
    public string GameBuild()
    {
        if(!string.IsNullOrWhiteSpace(Settings.GamePath))
        {
            var steamApps=Directory.GetParent(Settings.GamePath)?.Parent?.FullName;
            var manifest=steamApps==null ? "" : Path.Combine(steamApps,"appmanifest_251570.acf");
            if(File.Exists(manifest))
            {
                var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest),"\"buildid\"\\s+\"([0-9]+)\"");
                if(match.Success) return "Steam Build "+match.Groups[1].Value;
            }
        }
        var f=Path.Combine(Settings.GamePath,"7DaysToDie_Data","buildid.txt");
        if(File.Exists(f)) return File.ReadAllText(f).Trim();
        var exe=Path.Combine(Settings.GamePath,"7DaysToDie.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "未知" : "未知";
    }
    public List<Installed> Scan()
    {
        var installed=new List<Installed>(); var managed=Deployment.Mods;
        var roots=new List<string>();
        if(!string.IsNullOrWhiteSpace(Settings.GamePath)) roots.Add(GameMods);
        if(!string.IsNullOrWhiteSpace(Settings.UserModsPath) && !roots.Any(r=>Files.Same(r,Settings.UserModsPath))) roots.Add(Settings.UserModsPath);
        foreach(var root in roots)
        {
            if(!Directory.Exists(root)) continue;
            Files.NoLinks(root);
            foreach(var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                var row=new Installed {Path=dir,Managed=managed.Any(m=>Files.Same(m.Target,dir))};
                try { row.Info=Files.Describe(dir,false); row.Native=Files.Native(row.Info); }
                catch(Exception e) { row.Error=e.Message; }
                installed.Add(row);
            }
        }
        foreach(var group in installed.Where(i=>i.Info!=null).GroupBy(i=>i.Info!.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            foreach(var item in group) item.Error="重复内部名称："+group.Key;
        return installed;
    }
    public List<Package> Packages() => Directory.GetDirectories(Library).Where(d=>File.Exists(Path.Combine(d,"package.json"))).Select(d=>Json.Read<Package>(Path.Combine(d,"package.json"))).OrderBy(p=>p.DisplayName).ThenBy(p=>p.Version).ToList();
    public string Payload(string key)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(key,@"^[0-9a-f]{64}$")) throw new InvalidDataException("无效包标识。");
        return Files.Under(Library,key+"/content");
    }
    public Package Package(string key) => Json.Read<Package>(Path.Combine(Path.GetDirectoryName(Payload(key))!,"package.json"));
    Package ImportOne(string dir)
    {
        var p=Files.Describe(dir);
        if(Files.Native(p)) throw new InvalidDataException("游戏自带 Harmony 不作为普通 Mod 导入。");
        var dest=Payload(p.Key);
        if(Directory.Exists(dest))
        {
            if(!Files.Equal(p.Files,Files.Snapshot(dest))) throw new IOException("仓库中的原始包已被修改："+p.DisplayName);
            return Package(p.Key);
        }
        string temp=Path.Combine(Root,"Import",Guid.NewGuid().ToString("N"));
        try
        {
            Files.Copy(dir,Path.Combine(temp,"content"));
            if(!Files.Equal(p.Files,Files.Snapshot(Path.Combine(temp,"content")))) throw new IOException("导入时源文件发生变化。");
            Json.Write(Path.Combine(temp,"package.json"),p);
            Directory.Move(temp,Path.GetDirectoryName(dest)!);
        }
        finally { Files.DeleteOwn(temp,Root); }
        Log("Imported "+p.Name+" "+p.Version+" "+p.Key); return p;
    }
    public PackManifest? LastImportedManifest { get; private set; }
    public List<Package> Import(string path)
    {
        using var guard=Lock();
        LastImportedManifest=null;
        string? temp=null;
        try
        {
            if(File.Exists(path))
            {
                if(!Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("首版支持 ZIP 或已解压文件夹；RAR / 7z 请先解压。");
                temp=Path.Combine(Root,"Import",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); Files.Extract(path,temp); path=temp;
            }
            var manifest=Path.Combine(path,"jlmm-pack.json");
            if(File.Exists(manifest)) { LastImportedManifest=Json.Read<PackManifest>(manifest); VerifyPack(path,LastImportedManifest); }
            var dirs=Files.FindMods(path);
            if(dirs.Count==0) throw new InvalidDataException("没有找到 ModInfo.xml。");
            var candidates=dirs.Select(d=>Files.Describe(d,false)).Where(p=>!Files.Native(p)).ToList();
            if(candidates.GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)) throw new InvalidDataException("压缩包含同一 Mod 的多个版本，请分别导入。");
            return dirs.Where(d=>!Files.Native(Files.Describe(d,false))).Select(ImportOne).ToList();
        }
        finally { if(temp!=null) Files.DeleteOwn(temp,Root); }
    }
    public void Select(Package p, bool enabled=true)
    {
        var old=Current.Mods.FirstOrDefault(c=>Package(c.Key).Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
        if(old!=null)
        {
            old.Key=p.Key; old.Enabled=enabled;
        }
        else Current.Mods.Add(new Choice { Key=p.Key,Enabled=enabled });
        Save();
    }
    public void Adopt(IEnumerable<Installed> rows)
    {
        using var guard=Lock(); EnsureStopped(); ValidateGame(); EnsureNoPending();
        var state=Deployment;
        foreach(var row in rows)
        {
            EnsureStopped();
            ValidateTarget(row.Path);
            if(row.Native || row.Info==null || row.Error.Length>0) throw new IOException("无法接管："+row.DisplayName+" "+row.Status);
            var package=ImportOne(row.Path);
            EnsureStopped();
            if(!Files.Equal(package.Files,Files.Snapshot(row.Path))) throw new IOException("接管时源目录发生变化，请重试。");
            if(state.Mods.Any(m=>m.Name.Equals(package.Name,StringComparison.OrdinalIgnoreCase) && !Files.Same(m.Target,row.Path))) throw new IOException("同名 Mod 已在另一目录接管。");
            state.Mods.RemoveAll(m=>Files.Same(m.Target,row.Path));
            state.Mods.Add(new ManagedMod {Name=package.Name,Key=package.Key,Target=Files.Full(row.Path),Files=package.Files});
            Select(package);
            // Each adopted folder is independently durable; no game files are changed.
            Json.Write(StatePath,state);
        }
        Log("Adopted existing installation");
    }
    void ValidateTarget(string target)
    {
        var parent=Path.GetDirectoryName(Files.Full(target))!;
        if(!Files.Same(parent,GameMods) && !Files.Same(parent,Settings.UserModsPath)) throw new IOException("部署目标不在配置的 Mods 目录内："+target);
        Files.SafeSegment(Path.GetFileName(target)); Files.NoLinks(target);
        if(Path.GetFileName(target).Equals("0_TFP_Harmony",StringComparison.OrdinalIgnoreCase)) throw new IOException("禁止替换游戏自带 Harmony。");
    }
    public List<Transaction> History() => Directory.GetFiles(Transactions,"*.json").Select(Json.Read<Transaction>).OrderByDescending(t=>t.Created).ToList();
    public bool HasPending => History().Any(t=>t.Status=="Prepared");
    void EnsureNoPending() { if(HasPending) throw new IOException("存在未完成部署，请先点击恢复。"); }
    string Journal(Transaction t) => Path.Combine(Transactions,t.Id+".json");
    public List<string> Review()
    {
        ValidateGame(); EnsureNoPending();
        var old=Deployment; var messages=new List<string>();
        foreach(var m in old.Mods)
        {
            ValidateTarget(m.Target);
            if(!Directory.Exists(m.Target) || !Files.Equal(m.Files,Files.Snapshot(m.Target))) throw new IOException("已安装文件被外部修改或移走："+m.Name+"。请扫描并重新接管现有版本，或恢复原文件；本次不会覆盖。");
        }
        var selected=Current.Mods.Where(c=>c.Enabled).Select(c=>(Choice:c,Info:Package(c.Key))).ToList();
        if(selected.GroupBy(c=>c.Info.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1) || selected.GroupBy(c=>c.Info.Folder,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)) throw new IOException("方案含重复 Mod 名称或目标文件夹。");
        var external=Scan().Where(i=>!i.Managed && !i.Native).ToList();
        foreach(var item in selected)
        {
            if(Files.Native(item.Info)) throw new IOException("方案不能包含游戏自带 Harmony。");
            if(!Files.Equal(item.Info.Files,Files.Snapshot(Payload(item.Info.Key)))) throw new IOException("原始仓库校验失败："+item.Info.Name);
            if(external.Any(i=>i.Info?.Name.Equals(item.Info.Name,StringComparison.OrdinalIgnoreCase)==true)) throw new IOException("外部目录已有同名 Mod，请先接管："+item.Info.Name);
            string target=Path.Combine(GameMods,item.Info.Folder); ValidateTarget(target);
            if((Directory.Exists(target)||File.Exists(target)) && !old.Mods.Any(m=>Files.Same(m.Target,target))) throw new IOException("目标文件夹不是管理器管理的内容："+target);
            messages.Add("启用  "+item.Info.DisplayName+"  "+item.Info.Version+"\n  "+Identify(item.Info));
        }
        foreach(var m in old.Mods.Where(m=>!selected.Any(c=>c.Info.Name.Equals(m.Name,StringComparison.OrdinalIgnoreCase)))) messages.Add("停用  "+m.Name);
        if(selected.Any(c=>c.Info.HasCode) && !Scan().Any(i=>i.Native)) throw new IOException("代码 Mod 需要游戏自带的 0_TFP_Harmony，请先验证游戏文件。");
        messages.Add("外部安装仍会加载："+external.Count+" 个。未声明的兼容性与依赖不会自动判定。");
        return messages;
    }
    public void Apply()
    {
        using var guard=Lock(); EnsureStopped(); Review();
        var t=new Transaction {Before=Deployment,After=new Deployment {ProfileName=Current.Name}};
        Directory.CreateDirectory(GameMods);
        var chosen=Current.Mods.Where(c=>c.Enabled).ToList();
        var allTargets=t.Before.Mods.Select(m=>m.Target).Concat(chosen.Select(c=>Path.Combine(GameMods,Package(c.Key).Folder))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int index=0;
        // The journal exists before any staging, so even preparation failures are recoverable.
        Json.Write(Journal(t),t);
        try
        {
            foreach(var target in allTargets)
            {
                ValidateTarget(target);
                string area=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(target))!,".JLMM-Transactions",t.Id);
                Files.NoLinks(area);
                var change=new Change {Target=Files.Full(target),Stage=Path.Combine(area,"new",index.ToString()),Backup=Path.Combine(area,"old",index.ToString()),Existed=Directory.Exists(target)};
                if(change.Existed) change.Before=Files.Snapshot(target);
                t.Changes.Add(change); Json.Write(Journal(t),t);
                var choice=chosen.FirstOrDefault(c=>Files.Same(Path.Combine(GameMods,Package(c.Key).Folder),target));
                if(choice!=null)
                {
                    var p=Package(choice.Key); Files.Copy(Payload(choice.Key),change.Stage);
                    change.After=Files.Snapshot(change.Stage);
                    t.After.Mods.Add(new ManagedMod {Name=p.Name,Key=p.Key,Target=change.Target,Files=change.After});
                }
                Json.Write(Journal(t),t); index++;
            }
            EnsureStopped(); Checkpoint?.Invoke("prepared");
            foreach(var c in t.Changes)
            {
                EnsureStopped();
                if(c.Existed)
                {
                    if(!Files.Equal(c.Before,Files.Snapshot(c.Target))) throw new IOException("部署前文件发生变化。");
                    Directory.CreateDirectory(Path.GetDirectoryName(c.Backup)!); Directory.Move(c.Target,c.Backup);
                }
                Checkpoint?.Invoke("moved-old");
                if(Directory.Exists(c.Stage)) Directory.Move(c.Stage,c.Target);
                Checkpoint?.Invoke("moved-new");
                if(c.After.Count>0 && !Files.Equal(c.After,Files.Snapshot(c.Target))) throw new IOException("部署校验失败。");
            }
            Json.Write(StatePath,t.After); Checkpoint?.Invoke("state-written");
            t.Status="Completed"; Json.Write(Journal(t),t); Log("Applied profile "+Current.Name+" transaction "+t.Id);
        }
        catch
        {
            if(!GameRunning()) { try { Restore(t); } catch(Exception e) { Log("Recovery required: "+e.Message); } }
            throw;
        }
    }
    void ValidateChange(Transaction t, Change c)
    {
        ValidateTarget(c.Target);
        if(!System.Text.RegularExpressions.Regex.IsMatch(t.Id,@"^[0-9a-f]{32}$")) throw new IOException("无效部署记录。");
        string area=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(c.Target))!,".JLMM-Transactions",t.Id);
        if(!Files.Within(c.Backup,Path.Combine(area,"old")) || !Files.Within(c.Stage,Path.Combine(area,"new"))) throw new IOException("部署记录路径越界。");
        Files.NoLinks(c.Backup); Files.NoLinks(c.Stage);
    }
    void Restore(Transaction t)
    {
        EnsureStopped();
        // Preflight every affected folder before changing any of them.
        foreach(var c in t.Changes)
        {
            ValidateChange(t,c);
            if(Directory.Exists(c.Backup) && !Files.Equal(c.Before,Files.Snapshot(c.Backup))) throw new IOException("备份已改变，不能自动恢复。");
            if(Directory.Exists(c.Target))
            {
                var actual=Files.Snapshot(c.Target);
                if(!Files.Equal(actual,c.After) && !Files.Equal(actual,c.Before)) throw new IOException("目标已有新改动，不能自动恢复："+c.Target);
            }
            if(c.Existed && !Directory.Exists(c.Backup) && (!Directory.Exists(c.Target) || !Files.Equal(c.Before,Files.Snapshot(c.Target)))) throw new IOException("旧版本文件缺失，停止恢复。");
        }
        foreach(var c in t.Changes.AsEnumerable().Reverse())
        {
            EnsureStopped();
            if(Directory.Exists(c.Backup))
            {
                if(Directory.Exists(c.Target))
                {
                    var displaced=c.Stage+"-displaced-"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(Path.GetDirectoryName(displaced)!); Directory.Move(c.Target,displaced);
                }
                Directory.Move(c.Backup,c.Target);
            }
            else if(!c.Existed && Directory.Exists(c.Target))
            {
                var displaced=c.Stage+"-displaced-"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(Path.GetDirectoryName(displaced)!); Directory.Move(c.Target,displaced);
            }
        }
        Json.Write(StatePath,t.Before); t.Status="Restored"; Json.Write(Journal(t),t); Log("Restored transaction "+t.Id);
    }
    public void Recover()
    {
        using var guard=Lock(); ValidateGame();
        foreach(var t in History().Where(t=>t.Status=="Prepared")) Restore(t);
    }
    public void Undo()
    {
        using var guard=Lock(); ValidateGame(); EnsureNoPending();
        var t=History().FirstOrDefault(t=>t.Status=="Completed") ?? throw new IOException("没有可回退的部署。");
        if(System.Text.Json.JsonSerializer.Serialize(Deployment,Json.Options)!=System.Text.Json.JsonSerializer.Serialize(t.After,Json.Options)) throw new IOException("部署后接管记录已变化，无法直接回退。请通过方案选择旧版本。");
        Restore(t);
    }
    PackManifest MakeManifest()
    {
        var pack=new PackManifest {Name=Current.Name,GameBuild=GameBuild(),TargetGameVersion=Current.TargetGameVersion,Notes=Current.Notes};
        foreach(var c in Current.Mods.Where(c=>c.Enabled))
        {
            var p=Package(c.Key);
            if(!Files.Equal(p.Files,Files.Snapshot(Payload(c.Key)))) throw new IOException("仓库校验失败。");
            var hashes=new Dictionary<string,string>(p.Files,StringComparer.OrdinalIgnoreCase);
            pack.Mods.Add(new PackItem {Name=p.Name,Version=p.Version,Folder=p.Folder,Files=hashes});
        }
        return pack;
    }
    public void Export(string destination,bool full)
    {
        using var guard=Lock(); var manifest=MakeManifest();
        if(!full) { Json.Write(destination,manifest); return; }
        string temp=Path.Combine(Root,"Export",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        string pending=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            foreach(var c in Current.Mods.Where(c=>c.Enabled))
            {
                var p=Package(c.Key); var to=Files.Under(temp,"Mods/"+p.Folder); Files.Copy(Payload(c.Key),to);
            }
            Json.Write(Path.Combine(temp,"jlmm-pack.json"),manifest);
            File.WriteAllText(Path.Combine(temp,"安装说明.txt"),"使用 JL Mod 管理器导入本 ZIP，检查方案后应用。也可手动复制 Mods 内的各 Mod 文件夹到游戏 Mods。代码 Mod 需要关闭 EAC。联机双方版本及必要配置应一致。此包不包含游戏自带 Harmony，请保留游戏原文件。\n");
            VerifyPack(temp,manifest);
            ZipFile.CreateFromDirectory(temp,pending,CompressionLevel.Optimal,false); File.Move(pending,destination,true);
        }
        finally { Files.DeleteOwn(temp,Root); if(File.Exists(pending)) File.Delete(pending); }
    }
    static void VerifyPack(string dir,PackManifest pack)
    {
        if(pack.Format!="JLModManager/1") throw new InvalidDataException("不支持的整合包清单格式。");
        if(pack.Mods.GroupBy(m=>m.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1) || pack.Mods.GroupBy(m=>m.Folder,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)) throw new InvalidDataException("清单含重复的 Mod 名称或目录。");
        var found=Files.FindMods(dir);
        if(found.Count!=pack.Mods.Count) throw new InvalidDataException("整合包存在未列入清单的 Mod，或缺少 Mod。");
        foreach(var m in pack.Mods)
        {
            var folder=Files.Under(dir,"Mods/"+m.Folder); var info=Files.Describe(folder);
            if(info.Name!=m.Name || info.Version!=m.Version || !Files.Equal(m.Files,info.Files)) throw new InvalidDataException("整合包内容与清单不一致："+m.Name);
        }
    }
    public string Compare(string file)
    {
        var expected=Json.Read<PackManifest>(file); if(expected.Format!="JLModManager/1") throw new InvalidDataException("不是管理器清单。");
        var actual=MakeManifest(); var result=new List<string>();
        if(expected.GameBuild!=actual.GameBuild) result.Add("游戏标识不同：对方 "+expected.GameBuild+" / 本地 "+actual.GameBuild);
        foreach(var e in expected.Mods)
        {
            var a=actual.Mods.FirstOrDefault(m=>m.Name.Equals(e.Name,StringComparison.OrdinalIgnoreCase));
            if(a==null) result.Add("缺少："+e.Name);
            else { if(a.Version!=e.Version) result.Add("版本不同："+e.Name+" "+a.Version+" → "+e.Version); if(!Files.Equal(a.Files,e.Files)) result.Add("文件或配置不同："+e.Name); if(a.Folder!=e.Folder) result.Add("文件夹名称 / 排序不同："+e.Name); }
        }
        foreach(var a in actual.Mods.Where(a=>!expected.Mods.Any(e=>e.Name.Equals(a.Name,StringComparison.OrdinalIgnoreCase)))) result.Add("多出："+a.Name);
        return (result.Count==0 ? "当前方案的 Mod 文件与清单一致。" : string.Join(Environment.NewLine,result))+"\n比较范围是已选方案，不包含未接管的外部 Mod；不能据此保证多人兼容。";
    }
    public void BackupSave(string saves,string destination)
    {
        using var guard=Lock(); EnsureStopped(); Files.NoLinks(saves);
        if(Files.Within(destination,saves)) throw new IOException("备份不能放进被备份目录。");
        _=Files.Walk(saves).ToArray();
        var pending=destination+".partial";
        try { ZipFile.CreateFromDirectory(saves,pending,CompressionLevel.Fastest,false); EnsureStopped(); File.Move(pending,destination,false); }
        finally { if(File.Exists(pending)) File.Delete(pending); }
    }
}

sealed class OperationLock : IDisposable
{
    readonly FileStream file;
    Mutex? mutex;
    public OperationLock(string root,string game)
    {
        file=new FileStream(Path.Combine(root,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            if(!string.IsNullOrWhiteSpace(game))
            {
                var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Files.Full(game).ToLowerInvariant())));
                mutex=new Mutex(false,"Local\\JLModManager-Game-"+key);
                bool acquired;
                try { acquired=mutex.WaitOne(0); } catch(AbandonedMutexException) { acquired=true; }
                if(!acquired) { mutex.Dispose();mutex=null;throw new IOException("另一管理器正在处理此游戏，请稍后重试。"); }
            }
        }
        catch { file.Dispose();throw; }
    }
    public void Dispose(){if(mutex!=null){mutex.ReleaseMutex();mutex.Dispose();}file.Dispose();}
}
