namespace JLModManager.Core;

public class DownloadEntry
{
    public string Path {get;set;}="";
    public string Name {get;set;}="";
    public string Kind {get;set;}="";
    public string Status {get;set;}="待导入";
}
public partial class Manager
{
    public void SetModsDirectory(string mods, string userMods)
    {
        mods=Files.Full(mods);Files.NoLinks(mods);
        if(!Directory.Exists(mods)||!Path.GetFileName(mods).Equals("Mods",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择游戏安装目录下的 Mods 文件夹。");
        var game=Path.GetDirectoryName(mods)!;
        if(!File.Exists(Path.Combine(game,"7DaysToDie.exe")))throw new IOException("所选 Mods 的上一级没有 7DaysToDie.exe，请检查位置。");
        if(!Path.IsPathFullyQualified(userMods)||!Path.GetFileName(Files.Full(userMods)).Equals("Mods",StringComparison.OrdinalIgnoreCase))throw new IOException("用户数据目录应指向 Mods 文件夹。");
        Files.NoLinks(userMods);
        if(HasPending)throw new IOException("请先恢复未完成的部署，再更改目录。");
        if(Deployment.Mods.Count>0&&(!Files.Same(game,Settings.GamePath)||!Files.Same(userMods,Settings.UserModsPath)))throw new IOException("已有接管记录，不能直接更换安装。请为另一安装使用独立的数据仓库。");
        Settings.GamePath=game;Settings.UserModsPath=Files.Full(userMods);Save();
    }
    public List<DownloadEntry> ScanDownloads(string path)
    {
        path=Files.Full(path);Files.NoLinks(path);
        var result=new List<DownloadEntry>();int visited=0;
        void Visit(string dir,int depth)
        {
            if(++visited>10000)throw new IOException("扫描目录过大，请选择更具体的下载子目录。");
            Files.NoLinks(dir);
            if(File.Exists(System.IO.Path.Combine(dir,"ModInfo.xml")))
            {
                var item=new DownloadEntry{Path=dir,Name=System.IO.Path.GetFileName(dir),Kind="已解压目录"};
                try{item.Name=Files.Describe(dir,false).Title;}catch(Exception ex){item.Status=ex.Message;}
                result.Add(item);return;
            }
            foreach(var file in Directory.GetFiles(dir))
            {
                string ext=System.IO.Path.GetExtension(file).ToLowerInvariant();
                if(ext is ".zip" or ".rar" or ".7z")result.Add(new(){Path=file,Name=System.IO.Path.GetFileName(file),Kind=ext[1..].ToUpperInvariant(),Status="待导入"});
            }
            if(depth<8)foreach(var sub in Directory.GetDirectories(dir))
            {
                if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)!=0)continue;
                Visit(sub,depth+1);
            }
        }
        Visit(path,0);return result.OrderBy(e=>e.Name).ToList();
    }
}
