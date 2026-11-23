using System.IO.Compression;
using JLModManager.Core;

string root=Path.Combine(Path.GetFullPath(args.Length>0?args[0]:"test-results"),"run-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results=new List<object>();int failed=0;
const string config="""
{"Radius":8,"DistanceMetric":"Euclidean","NormalPowerWatts":10,"CombatPowerWatts":30,"RepairIntervalSeconds":2,"RepairHitPointsPerInterval":50,"NormalMaterialMultiplier":1,"ActiveMaterialMultiplier":2,"CombatQuietSeconds":5,"RestoreDamagedStages":true,"DefaultSupplyReserve":20,"FencePriorityMargin":50}
""";
void Check(bool ok,string message="断言失败"){if(!ok)throw new Exception(message);}
void Reject(Action action){bool denied=false;try{action();}catch{denied=true;}Check(denied,"应拒绝该操作");}
void Test(string name,Action run)
{
    try{run();results.Add(new{Name=name,Passed=true});Console.WriteLine("PASS "+name);}
    catch(Exception e){failed++;results.Add(new{Name=name,Passed=false,Error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e);}
}
string Mod(string parent,string folder="TestMod",string name="TestMod",string version="1.0",string content="first")
{
    string dir=Path.Combine(parent,folder);Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir,"ModInfo.xml"),$"<xml><Name value=\"{name}\"/><DisplayName value=\"测试模组 {name}\"/><Version value=\"{version}\"/></xml>");
    File.WriteAllText(Path.Combine(dir,"content.txt"),content);return dir;
}
(Manager M,string Game,string Source) Setup(string id)
{
    var dir=Path.Combine(root,id);var game=Path.Combine(dir,"Game");Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,"7DaysToDie.exe"),"fixture");
    var m=new Manager(Path.Combine(dir,"Store")){GameRunning=()=>false};m.Settings.GamePath=game;m.Settings.UserModsPath=Path.Combine(dir,"UserData","Mods");m.Save();
    Mod(Path.Combine(game,"Mods"),"0_TFP_Harmony","TFP_Harmony");
    return(m,game,Path.Combine(dir,"Source"));
}
Test("嵌套 ZIP 导入，中文文件名完整",()=>{
    var (m,_,src)=Setup("zip");var d=Mod(Path.Combine(src,"wrapper","Mods"));File.WriteAllText(Path.Combine(d,"说明.txt"),"测试");
    string zip=Path.Combine(root,"valid.zip");ZipFile.CreateFromDirectory(src,zip);var p=m.Import(zip).Single();Check(File.ReadAllText(Path.Combine(m.Payload(p.Key),"说明.txt"))=="测试");Check(m.Import(zip).Single().Key==p.Key);Check(m.Packages().Count==1);
});
foreach(var bad in new[]{"../escape.txt","/absolute.txt","C:/escape.txt","bad:stream.txt","CON.txt","folder./x.txt","folder/../x.txt"})
Test("拒绝 ZIP 危险路径 "+bad,()=>{
    var(m,_,_)=Setup("zipbad-"+Guid.NewGuid().ToString("N"));var z=Path.Combine(root,Guid.NewGuid()+".zip");using(var a=ZipFile.Open(z,ZipArchiveMode.Create)){using var w=new StreamWriter(a.CreateEntry(bad).Open());w.Write("bad");}Reject(()=>m.Import(z));
});
Test("拒绝 ZIP 大小写重复路径",()=>{
    var(m,_,_)=Setup("zipdupe");var z=Path.Combine(root,"dupe.zip");using(var a=ZipFile.Open(z,ZipArchiveMode.Create)){a.CreateEntry("A.txt");a.CreateEntry("a.txt");}Reject(()=>m.Import(z));
});
Test("拒绝 ZIP 符号链接",()=>{
    var(m,_,_)=Setup("ziplink");var z=Path.Combine(root,"link.zip");using(var a=ZipFile.Open(z,ZipArchiveMode.Create)){a.CreateEntry("link").ExternalAttributes=unchecked((int)0xA1FF0000);}Reject(()=>m.Import(z));
});
Test("拒绝 XML 外部实体",()=>{
    var(m,_,src)=Setup("xxe");var d=Mod(src);File.WriteAllText(Path.Combine(d,"ModInfo.xml"),"<!DOCTYPE xml [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><xml><Name value='Bad'/><DisplayName value='&x;'/></xml>");Reject(()=>m.Import(d));
});
Test("相同版本不同内容保留独立包",()=>{
    var(m,_,src)=Setup("hash");var d=Mod(src);var a=m.Import(d).Single();File.WriteAllText(Path.Combine(d,"content.txt"),"changed");var b=m.Import(d).Single();Check(a.Key!=b.Key&&m.Packages().Count==2);
});
Test("接管只记录与复制，不修改原安装",()=>{
    var(m,game,_)=Setup("adopt");var d=Mod(Path.Combine(game,"Mods"));var before=Files.Snapshot(d);m.Adopt(m.Scan().Where(i=>!i.Native));Check(Files.Equal(before,Files.Snapshot(d)));Check(m.Deployment.Mods.Count==1&&m.Current.Mods.Count==1);Check(m.Scan().Single(i=>!i.Native).Managed);
});
Test("两个扫描根发现同名 Mod",()=>{
    var(m,game,_)=Setup("duproots");Mod(Path.Combine(game,"Mods"));Mod(m.Settings.UserModsPath);Check(m.Scan().Count(i=>i.Error.StartsWith("重复"))==2);Reject(()=>m.Adopt(m.Scan().Where(i=>!i.Native)));
});
Test("部署、停用、回退，不碰外部 Mod 与 Harmony",()=>{
    var(m,game,src)=Setup("apply");var outside=Mod(Path.Combine(game,"Mods"),"External","External");var snap=Files.Snapshot(outside);var p=m.Import(Mod(src)).Single();m.Select(p);m.Apply();var target=Path.Combine(game,"Mods",p.Folder);Check(Directory.Exists(target));m.Current.Mods[0].Enabled=false;m.Apply();Check(!Directory.Exists(target));m.Undo();Check(Directory.Exists(target));Check(Files.Equal(snap,Files.Snapshot(outside)));Check(Directory.Exists(Path.Combine(game,"Mods","0_TFP_Harmony")));
});
Test("跨扫描根接管后部署、回退",()=>{
    var(m,game,_)=Setup("userroot");var old=Mod(m.Settings.UserModsPath);m.Adopt(m.Scan().Where(i=>!i.Native));m.Apply();Check(!Directory.Exists(old));Check(Directory.Exists(Path.Combine(game,"Mods","TestMod")));m.Undo();Check(Directory.Exists(old));Check(!Directory.Exists(Path.Combine(game,"Mods","TestMod")));
});
Test("外部安装同名阻止覆盖",()=>{
    var(m,game,src)=Setup("external");var d=Mod(Path.Combine(game,"Mods"));m.Select(m.Import(Mod(src)).Single());Reject(m.Apply);Check(File.ReadAllText(Path.Combine(d,"content.txt"))=="first");
});
Test("目标文件夹不同内部名也阻止覆盖",()=>{
    var(m,game,src)=Setup("collision");Mod(Path.Combine(game,"Mods"),"TestMod","OtherName");m.Select(m.Import(Mod(src)).Single());Reject(m.Apply);
});
Test("游戏运行时禁止部署与接管",()=>{
    var(m,game,src)=Setup("running");var d=Mod(Path.Combine(game,"Mods"));m.Select(m.Import(Mod(src)).Single());m.GameRunning=()=>true;Reject(m.Apply);Reject(()=>m.Adopt(m.Scan().Where(i=>!i.Native)));Check(m.Deployment.Mods.Count==0);Check(Directory.Exists(d));
});
Test("仓库内容变动阻止部署",()=>{
    var(m,_,src)=Setup("librarydrift");var p=m.Import(Mod(src)).Single();m.Select(p);File.WriteAllText(Path.Combine(m.Payload(p.Key),"content.txt"),"drift");Reject(m.Apply);Check(m.Deployment.Mods.Count==0);
});
Test("已安装文件外部修改阻止覆盖，重新接管保留修改",()=>{
    var(m,game,src)=Setup("drift");m.Select(m.Import(Mod(src)).Single());m.Apply();var f=Path.Combine(game,"Mods","TestMod","content.txt");File.WriteAllText(f,"user edit");Reject(m.Apply);Check(File.ReadAllText(f)=="user edit");m.Adopt(m.Scan().Where(i=>!i.Native));m.Apply();Check(File.ReadAllText(f)=="user edit");
});
foreach(var point in new[]{"prepared","moved-old","moved-new","state-written"})
Test("故障自动回滚 "+point,()=>{
    var(m,game,src)=Setup("fault-"+point);var d=Mod(src);m.Select(m.Import(d).Single());m.Apply();var before=Files.Snapshot(Path.Combine(game,"Mods","TestMod"));File.WriteAllText(Path.Combine(d,"content.txt"),"second");m.Select(m.Import(d).Single());m.Checkpoint=s=>{if(s==point)throw new IOException("simulated");};Reject(m.Apply);Check(!m.HasPending);Check(Files.Equal(before,Files.Snapshot(Path.Combine(game,"Mods","TestMod"))));
});
foreach(var point in new[]{"moved-old","moved-new","state-written"})
Test("进程中断，下次启动恢复 "+point,()=>{
    var(m,game,src)=Setup("recover-"+point);var d=Mod(src);m.Select(m.Import(d).Single());m.Apply();File.WriteAllText(Path.Combine(d,"content.txt"),"v2");m.Select(m.Import(d).Single());m.Checkpoint=s=>{if(s==point){m.GameRunning=()=>true;throw new IOException("simulated termination");}};Reject(m.Apply);Check(m.HasPending);
    var restarted=new Manager(m.Root){GameRunning=()=>false};Reject(restarted.Apply);restarted.Recover();Check(!restarted.HasPending);Check(File.ReadAllText(Path.Combine(game,"Mods","TestMod","content.txt"))=="first");
});
Test("首次安装故障恢复不残留启用 Mod",()=>{
    var(m,game,src)=Setup("newfault");m.Select(m.Import(Mod(src)).Single());m.Checkpoint=s=>{if(s=="moved-new")throw new IOException();};Reject(m.Apply);Check(!Directory.Exists(Path.Combine(game,"Mods","TestMod")));Check(m.Deployment.Mods.Count==0);
});
Test("回退前检测新改动，拒绝丢失修改",()=>{
    var(m,game,src)=Setup("undodrift");m.Select(m.Import(Mod(src)).Single());m.Apply();var f=Path.Combine(game,"Mods","TestMod","content.txt");File.WriteAllText(f,"new changes");Reject(m.Undo);Check(File.ReadAllText(f)=="new changes");
});
Test("版本切换使用作者新包配置，不合并旧配置，回退恢复整包",()=>{
    var(m,game,src)=Setup("opaque-update");var d=Mod(src);var file=Path.Combine(d,"settings.json");
    byte[] oldBytes=[0xEF,0xBB,0xBF,..System.Text.Encoding.UTF8.GetBytes("{\"author\":1}\r\n")];File.WriteAllBytes(file,oldBytes);
    var old=m.Import(d).Single();m.Select(old);m.Apply();
    byte[] newBytes=System.Text.Encoding.UTF8.GetBytes("// 作者提供的原始文本，不由管理器解析\r\nnew=value\r\n");File.WriteAllBytes(file,newBytes);
    var next=m.Import(d).Single();m.Select(next);m.Apply();var target=Path.Combine(game,"Mods","TestMod","settings.json");
    Check(File.ReadAllBytes(target).SequenceEqual(newBytes));Check(File.ReadAllBytes(Path.Combine(m.Payload(old.Key),"settings.json")).SequenceEqual(oldBytes));
    m.Undo();Check(File.ReadAllBytes(target).SequenceEqual(oldBytes));
});
Test("整合包导出逐字节保留作者配置，包括 BOM 与换行",()=>{
    var(m,_,src)=Setup("opaque-export");var d=Mod(src);byte[] bytes=[0xEF,0xBB,0xBF,..System.Text.Encoding.UTF8.GetBytes("\r\n# 作者配置\r\nx: 42\r\n")];
    File.WriteAllBytes(Path.Combine(d,"config.yml"),bytes);m.Select(m.Import(d).Single());var zip=Path.Combine(root,"opaque.zip");m.Export(zip,true);
    using var z=ZipFile.OpenRead(zip);using var input=z.GetEntry("Mods/TestMod/config.yml")!.Open();using var output=new MemoryStream();input.CopyTo(output);Check(output.ToArray().SequenceEqual(bytes));
});
Test("旧版内嵌配置迁移成普通包，保留旧包与设置备份",()=>{
    var(m,game,src)=Setup("legacy");var d=Mod(src,"JL_SteelSpikes","JL_SteelSpikes");File.WriteAllText(Path.Combine(d,"MaintenanceConfig.json"),config);var original=m.Import(d).Single();m.Select(original);
    var custom=config.Replace("\"Radius\":8","\"Radius\":12");m.Current.Mods[0].Configs=new(){{"MaintenanceConfig.json",custom}};m.Save();var settingsBefore=File.ReadAllBytes(Path.Combine(m.Root,"settings.json"));
    var upgraded=new Manager(m.Root){GameRunning=()=>false};var c=upgraded.Current.Mods[0];Check(c.Configs==null&&c.Key!=original.Key);Check(upgraded.Packages().Count==2);
    Check(File.ReadAllText(Path.Combine(upgraded.Payload(c.Key),"MaintenanceConfig.json"))==custom);
    Check(File.ReadAllText(Path.Combine(upgraded.Payload(original.Key),"MaintenanceConfig.json"))==config);
    Check(Directory.GetFiles(Path.Combine(m.Root,"Backups")).Any(f=>File.ReadAllBytes(f).SequenceEqual(settingsBefore)));
    Check(!File.ReadAllText(Path.Combine(m.Root,"settings.json")).Contains("Configs"));upgraded.Apply();
    Check(File.ReadAllText(Path.Combine(game,"Mods","JL_SteelSpikes","MaintenanceConfig.json"))==custom);
    var again=new Manager(m.Root);Check(again.Packages().Count==2&&again.Current.Mods[0].Key==c.Key);
});
Test("旧配置迁移后再更新，使用新作者包而非旧覆盖值",()=>{
    var(m,game,src)=Setup("legacy-update");var d=Mod(src);File.WriteAllText(Path.Combine(d,"config.txt"),"author v1");m.Select(m.Import(d).Single());m.Current.Mods[0].Configs=new(){{"config.txt","old custom"}};m.Save();
    var upgraded=new Manager(m.Root){GameRunning=()=>false};File.WriteAllText(Path.Combine(d,"config.txt"),"author v2");upgraded.Select(upgraded.Import(d).Single());upgraded.Apply();
    Check(File.ReadAllText(Path.Combine(game,"Mods","TestMod","config.txt"))=="author v2");
});
Test("旧配置迁移失败保留原设置，不允许路径越界",()=>{
    var(m,_,src)=Setup("legacy-invalid");m.Select(m.Import(Mod(src)).Single());m.Current.Mods[0].Configs=new(){{"../escape.txt","bad"}};m.Save();var before=File.ReadAllBytes(Path.Combine(m.Root,"settings.json"));
    Reject(()=>new Manager(m.Root));Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(m.Root,"settings.json"))));Check(m.Packages().Count==1);
});
Test("整合包往返、清单无私有安装路径",()=>{
    var(m,_,src)=Setup("export");m.Select(m.Import(Mod(src)).Single());string z=Path.Combine(root,"pack.zip"),list=Path.Combine(root,"list.json");m.Export(z,true);m.Export(list,false);Check(!File.ReadAllText(list).Contains(m.Root));Check(m.Compare(list).StartsWith("当前方案"));var(m2,_,_)=Setup("importpack");Check(m2.Import(z).Count==1);
});
Test("清单明确报告缺少、多出、文件变化",()=>{
    var(m,_,src)=Setup("compare");var d=Mod(src);m.Select(m.Import(d).Single());string list=Path.Combine(root,"compare.json");m.Export(list,false);m.Current.Mods.Clear();m.Select(m.Import(Mod(src,"Other","Other")).Single());var output=m.Compare(list);Check(output.Contains("缺少")&&output.Contains("多出"));m.Current.Mods.Clear();File.WriteAllText(Path.Combine(d,"content.txt"),"changed");m.Select(m.Import(d).Single());Check(m.Compare(list).Contains("文件或配置不同"));
});
Test("整合包清单检测被篡改内容",()=>{
    var(m,_,src)=Setup("tampered");m.Select(m.Import(Mod(src)).Single());string z=Path.Combine(root,"tampered.zip");m.Export(z,true);using(var a=ZipFile.Open(z,ZipArchiveMode.Update)){a.GetEntry("Mods/TestMod/content.txt")!.Delete();using var w=new StreamWriter(a.CreateEntry("Mods/TestMod/content.txt").Open());w.Write("tampered");}Reject(()=>m.Import(z));
});
Test("方案克隆不共享启用状态",()=>{
    var(m,_,src)=Setup("profile");m.Select(m.Import(Mod(src)).Single());var clone=Json.Clone(m.Current);clone.Mods[0].Enabled=false;Check(m.Current.Mods[0].Enabled);m.Save();var next=new Manager(m.Root);Check(next.Current.Mods[0].Enabled);
});
Test("存档备份、游戏运行禁止备份",()=>{
    var(m,_,src)=Setup("saves");Directory.CreateDirectory(src);File.WriteAllText(Path.Combine(src,"main.ttw"),"fixture save");string z=Path.Combine(root,"saves.zip");m.BackupSave(src,z);using(var a=ZipFile.OpenRead(z)){Check(a.GetEntry("main.ttw")!=null);}m.GameRunning=()=>true;Reject(()=>m.BackupSave(src,z+"2"));
});
Test("游戏自带 Harmony 不进入仓库",()=>{
    var(m,game,_)=Setup("native");Check(m.Import(Path.Combine(game,"Mods","0_TFP_Harmony")).Count==0);Reject(()=>m.Adopt(m.Scan().Where(i=>i.Native)));Check(m.Packages().Count==0);
});
Test("首次运行不自动替用户选择游戏目录",()=>{
    var m=new Manager(Path.Combine(root,"manual-first"));Check(m.Settings.GamePath=="");
});
Test("手动选择 Mods 验证父目录并持久化",()=>{
    var(m,game,src)=Setup("choose-mods");m.Settings.GamePath="";m.SetModsDirectory(Path.Combine(game,"Mods"),m.Settings.UserModsPath);Check(Files.Same(m.Settings.GamePath,game));Check(Files.Same(new Manager(m.Root).GameMods,Path.Combine(game,"Mods")));Directory.CreateDirectory(Path.Combine(src,"Mods"));Reject(()=>m.SetModsDirectory(Path.Combine(src,"Mods"),m.Settings.UserModsPath));Reject(()=>m.SetModsDirectory(game,m.Settings.UserModsPath));
});
Test("下载目录同时发现压缩包与已解压模组且不改动源",()=>{
    var(m,_,src)=Setup("downloads");var dir=Mod(Path.Combine(src,"wrapper"));File.WriteAllText(Path.Combine(src,"pack.zip"),"fixture");File.WriteAllText(Path.Combine(src,"pack.rar"),"fixture");var before=Files.Snapshot(src);var entries=m.ScanDownloads(src);Check(entries.Count==3);Check(entries.Any(e=>e.Kind=="ZIP")&&entries.Any(e=>e.Kind=="已解压目录")&&entries.Any(e=>e.Kind=="RAR"&&e.Status=="待导入"));Check(Files.Equal(before,Files.Snapshot(src)));
});
Test("收录下载内容仅进仓库、不加入方案或部署",()=>{
    var(m,game,src)=Setup("library-only");var p=m.Import(Mod(src)).Single();Check(m.Packages().Count==1&&m.Current.Mods.Count==0&&m.Deployment.Mods.Count==0);Check(!Directory.Exists(Path.Combine(game,"Mods",p.Folder)));m.Select(p);Check(m.Current.Mods.Count==1&&!Directory.Exists(Path.Combine(game,"Mods",p.Folder)));
});
Test("方案版本备注保存、复制与导出导入元数据",()=>{
    var(m,_,src)=Setup("profile-meta");m.Current.TargetGameVersion="v3.3 b18";m.Current.Notes="朋友联机 / 周末档";m.Select(m.Import(Mod(src)).Single());m.Save();var copy=Json.Clone(m.Current);copy.Notes="another";Check(m.Current.Notes!="another");var restart=new Manager(m.Root);Check(restart.Current.TargetGameVersion=="v3.3 b18"&&restart.Current.Notes==m.Current.Notes);string zip=Path.Combine(root,"profile-meta.zip");m.Export(zip,true);var(other,_,_)=Setup("profile-meta-import");other.Import(zip);Check(other.LastImportedManifest!.TargetGameVersion==m.Current.TargetGameVersion&&other.LastImportedManifest.Notes==m.Current.Notes);
});
Test("已有接管记录时禁止更换游戏安装",()=>{
    var(m,game,src)=Setup("switch-install");Mod(Path.Combine(game,"Mods"));m.Adopt(m.Scan().Where(e=>!e.Native));Directory.CreateDirectory(Path.Combine(src,"Mods"));File.WriteAllText(Path.Combine(src,"7DaysToDie.exe"),"fixture");Reject(()=>m.SetModsDirectory(Path.Combine(src,"Mods"),m.Settings.UserModsPath));Check(Files.Same(m.Settings.GamePath,game));
});

Test("指纹区分同名同版本改版，配置变化不改变身份",()=>{
 var a=new string('a',64);var b=new string('b',64);
 var rule=new IdentityRule{Id="original",Name="Shared",Branch="original",Version="1.0",Source="fixture",Fingerprints=new(){{"Core.dll",a}}};
 var fork=new IdentityRule{Id="fork",Name="Shared",Branch="fork",Version="1.0",Source="fixture",Fingerprints=new(){{"Core.dll",b}}};
 var p=new Package{Name="Shared",Version="1.0",Files=new(){{"Core.dll",a},{"config.xml",b}}};
 Check(Identity.Identify(p,[rule,fork]).RuleId=="original");p.Files["config.xml"]=a;Check(Identity.Identify(p,[rule,fork]).RuleId=="original");
 p.Files["Core.dll"]=b;Check(Identity.Identify(p,[rule,fork]).RuleId=="fork");
 p.Version="2.0";Check(Identity.Identify(p,[rule,fork]).Status=="无法确认");
});
Test("未知指纹、多重命中、额外 DLL 不自动确认",()=>{
 var a=new string('a',64);var rule=new IdentityRule{Id="r",Name="Test",Branch="a",Version="1",Source="fixture",Fingerprints=new(){{"core.dll",a}}};
 var p=new Package{Name="Test",Version="1",Files=new(){{"CORE.dll",a}}};
 Check(Identity.Identify(p,[rule]).Status=="已确认");Check(Identity.Identify(p,[rule,rule]).Status=="无法确认");
 p.Files["extra.dll"]=a;Check(Identity.Identify(p,[rule]).Status=="疑似匹配");p.Files.Clear();Check(Identity.Identify(p,[rule]).Status=="疑似匹配");Check(Identity.Identify(p,[]).Status=="无法确认");
});
Test("无效规则被拒绝，损坏指纹库不会放行身份",()=>{
 var(m,_,src)=Setup("identity-invalid");var p=m.Import(Mod(src)).Single();var path=Path.Combine(m.Root,"identity-rules.json");
 File.WriteAllText(path,"[{}");Check(m.Identify(p).Status=="无法确认");
 File.WriteAllText(path,"[{}]");Reject(()=>Identity.Load(path));
});

Test("变更预览区分启用替换停用及无变更",()=>{
 var(m,_,src)=Setup("plan");var dir=Mod(src);var a=m.Import(dir).Single();m.Select(a);Check(m.Plan().Single().Action=="启用");Check(m.RowState(m.Current.Mods[0])=="待启用");m.Apply();Check(m.Plan().Count==0);Check(m.RowState(m.Current.Mods[0])=="已部署");
 File.WriteAllText(Path.Combine(dir,"content.txt"),"other");var b=m.Import(dir).Single();m.Select(b);Check(m.Plan().Single().Action=="替换");m.Current.Mods[0].Enabled=false;Check(m.Plan().Single().Action=="停用");Check(m.RowState(m.Current.Mods[0])=="待停用");
});
Test("版本范围与构建号不混淆",()=>{
 Check(GameVersions.Matches("v3.3 b18","3.*"));Check(GameVersions.Matches("v3.3 b18","3.3.* b18"));Check(!GameVersions.Matches("3.3","3.3.* b18"));Check(!GameVersions.Matches("3.3","3.4.*"));Check(GameVersions.Parse("Steam Build 12345")==null);Check(GameVersions.Parse("2022.3.0")==null);
});
Test("体检区分阻止和待核实，不将手动来源当作已确认",()=>{
 var(m,_,src)=Setup("preflight");var p=m.Import(Mod(src)).Single();m.Select(p);m.Current.TargetGameVersion="3.3 b18";
 var rules=new List<SiteMod>{new(){Id="a",Name="A",Releases=[new(){ModVersion="1.0",Games=["3.2.*"]}],Required=["b"]},new(){Id="b",Name="B"}};
 m.Settings.SourceBindings[p.Key]="a";var checks=m.Preflight(rules);Check(checks.Any(c=>c.Message.Contains("超出")));Check(checks.Any(c=>c.Message.Contains("缺少已启用")));Check(checks.Any(c=>c.Level=="待核实"));
 m.GameRunning=()=>true;Check(m.Preflight(rules).Any(c=>c.Level=="阻止"));
});
Test("取消导入清理临时目录并保留原仓库",()=>{
 var(m,_,src)=Setup("cancel");m.Import(Mod(Path.Combine(src,"existing")));var before=m.Packages().Select(p=>p.Key).ToArray();var incoming=Mod(Path.Combine(src,"incoming"),name:"Other");
 File.WriteAllBytes(Path.Combine(incoming,"large.bin"),new byte[1024*1024]);using var cts=new CancellationTokenSource();
 using(var scope=new WorkScope(cts.Token,new InlineProgress(p=>{if(p.Stage.StartsWith("复制"))cts.Cancel();})))Reject(()=>m.Import(incoming));
 Check(m.Packages().Select(p=>p.Key).SequenceEqual(before));Check(!Directory.EnumerateDirectories(Path.Combine(m.Root,"Import")).Any());
});
Test("批量导入第二项失败不遗留第一项",()=>{
 var(m,_,src)=Setup("batch-rollback");var first=Mod(src,"A","First");var second=Mod(src,"B","Second");File.WriteAllBytes(Path.Combine(second,"large.bin"),new byte[1024*1024]);using var cts=new CancellationTokenSource();
 using(var scope=new WorkScope(cts.Token,new InlineProgress(p=>{if(m.Packages().Count>0)cts.Cancel();})))Reject(()=>m.Import(src));Check(m.Packages().Count==0);
});
Test("压缩目录拒绝越界重复链接和超大文件",()=>{
 string dest=Path.Combine(root,"parse");
 Reject(()=>Archives.Parse("Path = ../escape\nSize = 1\n",dest));Reject(()=>Archives.Parse("Path = a\nSize = 1\n\nPath = A\nSize = 1\n",dest));Reject(()=>Archives.Parse("Path = a\nSize = 1\nSymbolic Link = target\n",dest));Reject(()=>Archives.Parse("Path = a\nSize = 999999999999\n",dest));
 Check(Archives.Parse("Path = folder/file.xml\nSize = 12\nAttributes = A\n",dest).Single().Size==12);
});
if(Archives.Find7Zip() is {} sevenZip)
{
 Test("7z 实际解压及重复导入去重",()=>{
  var(m,_,src)=Setup("sevenzip");Mod(Path.Combine(src,"wrapper"));Directory.CreateDirectory(Path.Combine(src,"empty"));var archive=Path.Combine(root,"fixture.7z");
  var info=new System.Diagnostics.ProcessStartInfo(sevenZip){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};foreach(var a in new[]{"a",archive,src+"/*"})info.ArgumentList.Add(a);using var proc=System.Diagnostics.Process.Start(info)!;_=proc.StandardOutput.ReadToEnd();proc.WaitForExit();Check(proc.ExitCode==0);
  var imported=m.Import(archive);Check(imported.Count==1);Check(m.Import(archive).Single().Key==imported.Single().Key);Check(m.Packages().Count==1);
 });
}

Test("同名多版本可同时收录但方案只保留所选版本",()=>{
 var(m,_,src)=Setup("multi-version");Mod(src,"v1","Same","1.0");Mod(src,"v2","Same","2.0");var packages=m.Import(src);Check(packages.Count==2);foreach(var p in packages)m.Select(p);Check(m.Current.Mods.Count==1);
});
var rarExe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WinRAR","Rar.exe");
if(File.Exists(rarExe)&&Archives.Find7Zip()!=null)
Test("RAR 实际解压",()=>{
 var(m,_,src)=Setup("rar");Mod(src);var archive=Path.Combine(root,"fixture.rar");
 var info=new System.Diagnostics.ProcessStartInfo(rarExe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};foreach(var a in new[]{"a","-r","-ep1",archive,src+"/*"})info.ArgumentList.Add(a);using var proc=System.Diagnostics.Process.Start(info)!;_=proc.StandardOutput.ReadToEnd();proc.WaitForExit();Check(proc.ExitCode==0);Check(m.Import(archive).Single().Name=="TestMod");
});
Test("版本文件差异区分新增移除修改",()=>{
 var a=new Package{Files=new(){{"old.xml","a"},{"same.xml","a"},{"edit.xml","a"}}};var b=new Package{Files=new(){{"new.xml","a"},{"SAME.xml","a"},{"edit.xml","b"}}};var rows=Manager.ComparePackages(a,b);Check(rows.Count==3);Check(rows.Single(r=>r.Path=="edit.xml").Action=="修改");Check(rows.Single(r=>r.Path=="old.xml").Action=="移除");Check(rows.Single(r=>r.Path=="new.xml").Action=="新增");
});
if(Archives.Find7Zip() is {} cancel7zip)
Test("取消实际 7z 解压返回取消状态且无残留",()=>{
 var(m,_,src)=Setup("cancel-sevenzip");var dir=Mod(src);var data=new byte[2*1024*1024];new Random(7).NextBytes(data);File.WriteAllBytes(Path.Combine(dir,"large.bin"),data);var archive=Path.Combine(root,"cancel.7z");
 var info=new System.Diagnostics.ProcessStartInfo(cancel7zip){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};foreach(var a in new[]{"a",archive,src+"/*"})info.ArgumentList.Add(a);using(var proc=System.Diagnostics.Process.Start(info)!){_=proc.StandardOutput.ReadToEnd();proc.WaitForExit();Check(proc.ExitCode==0);}
 using var cts=new CancellationTokenSource();bool cancelled=false;
 using(var scope=new WorkScope(cts.Token,new InlineProgress(p=>{if(p.Stage.StartsWith("解压"))cts.Cancel();})))try{m.Import(archive);}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled);Check(m.Packages().Count==0);Check(!Directory.EnumerateDirectories(Path.Combine(m.Root,"Import")).Any());
});
Json.Write(Path.Combine(root,"results.json"),new{Passed=failed==0,Tests=results.Count,Failed=failed,Results=results});
Console.WriteLine($"RESULT {results.Count-failed}/{results.Count} passed\n{root}");return failed==0?0:1;

sealed class InlineProgress(Action<WorkProgress> callback) : IProgress<WorkProgress> { public void Report(WorkProgress value)=>callback(value); }
