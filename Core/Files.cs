using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JLModManager.Core;

public static class Files
{
    public static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    public static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
    public static bool Within(string p, string root) => Full(p).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string p)
    {
        for (string? current = Full(p); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("路径包含链接或联接点，请选择实际目录：" + current);
    }
    public static void SafeSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') ||
            System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("不支持的文件名：" + name);
    }
    public static string Under(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("不允许绝对路径：" + relative);
        foreach (var part in relative.Replace('\\','/').Split('/')) SafeSegment(part);
        var p = Full(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(p, root)) throw new InvalidDataException("路径越界：" + relative);
        NoLinks(p); return p;
    }
    public static IEnumerable<string> Walk(string root)
    {
        NoLinks(root);
        foreach (var p in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            SafeSegment(Path.GetFileName(p)); NoLinks(p);
            if (Directory.Exists(p)) { foreach (var f in Walk(p)) yield return f; }
            else yield return p;
        }
    }
    public static string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    public static Dictionary<string,string> Snapshot(string root) => Walk(root).ToDictionary(p => Path.GetRelativePath(root,p).Replace('\\','/'), Hash, StringComparer.OrdinalIgnoreCase);
    public static bool Equal(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(x => b.Any(y => string.Equals(x.Key,y.Key,StringComparison.OrdinalIgnoreCase) && x.Value == y.Value));
    public static string ContentKey(Dictionary<string,string> hashes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",hashes.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).Select(x=>x.Key.ToLowerInvariant()+"="+x.Value))))).ToLowerInvariant();
    public static void Copy(string from, string to)
    {
        if (Same(from,to) || Within(to,from)) throw new IOException("目标不能位于源目录内。");
        NoLinks(from); NoLinks(to);
        long bytes=Walk(from).Sum(p=>new FileInfo(p).Length);
        var drive=new DriveInfo(Path.GetPathRoot(Full(to))!);
        if(drive.IsReady && drive.AvailableFreeSpace < bytes+1024*1024) throw new IOException("目标磁盘空间不足。");
        Directory.CreateDirectory(to);
        foreach(var file in Walk(from))
        {
            var dest = Under(to,Path.GetRelativePath(from,file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file,dest,false);
        }
    }
    public static void DeleteOwn(string path, string root)
    {
        if (!Within(path,root)) throw new IOException("拒绝删除工作目录外的路径。");
        if (!Directory.Exists(path)) return;
        _ = Walk(path).ToArray(); Directory.Delete(path,true);
    }
    public static Package Describe(string dir, bool hash = true)
    {
        NoLinks(dir);
        using var reader = XmlReader.Create(Path.Combine(dir,"ModInfo.xml"),new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024*1024 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("ModInfo.xml 没有根节点。");
        if (root.Element("ModInfo") != null) throw new InvalidDataException("旧格式 ModInfo.xml，需由作者更新。");
        string Get(string n) => root.Element(n)?.Attribute("value")?.Value ?? "";
        var name=Get("Name"); var display=Get("DisplayName");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[A-Za-z0-9_-]+$") || string.IsNullOrWhiteSpace(display)) throw new InvalidDataException("ModInfo.xml 缺少有效 Name / DisplayName。");
        var p = new Package { Name=name,DisplayName=display,Version=Get("Version"),Description=Get("Description"),Author=Get("Author"),Folder=Path.GetFileName(dir),HasCode=Directory.GetFiles(dir,"*.dll").Length>0 };
        SafeSegment(p.Folder);
        if(hash) { p.Files=Snapshot(dir); p.Key=ContentKey(p.Files); }
        return p;
    }
    public static bool Native(Package p) => p.Name.Equals("TFP_Harmony",StringComparison.OrdinalIgnoreCase) || p.Name.Equals("0_TFP_Harmony",StringComparison.OrdinalIgnoreCase) || p.Folder.Equals("0_TFP_Harmony",StringComparison.OrdinalIgnoreCase);
    public static List<string> FindMods(string root)
    {
        var result = new List<string>();
        void Visit(string dir, int depth)
        {
            if(depth>15) throw new InvalidDataException("目录嵌套过深。");
            NoLinks(dir);
            if(File.Exists(Path.Combine(dir,"ModInfo.xml"))) { result.Add(dir); return; }
            foreach(var sub in Directory.GetDirectories(dir)) Visit(sub,depth+1);
        }
        Visit(root,0); return result;
    }
    public static void Extract(string zip, string target)
    {
        using var archive=ZipFile.OpenRead(zip);
        if(archive.Entries.Count>100000) throw new InvalidDataException("压缩包文件过多。");
        long total=0; var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in archive.Entries)
        {
            if (((e.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (e.ExternalAttributes & 0x400)!=0) throw new InvalidDataException("压缩包包含链接。");
            var relative=e.FullName.Replace('\\','/').TrimEnd('/');
            if(relative.Length==0) continue;
            var dest=Under(target,relative);
            if(!names.Add(relative)) throw new InvalidDataException("压缩包包含重复路径："+relative);
            total=checked(total+e.Length);
            if(total>20L*1024*1024*1024) throw new InvalidDataException("解压大小超过 20 GB；请先手动解压并使用导入文件夹。");
            if(e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var input=e.Open(); using var output=new FileStream(dest,FileMode.CreateNew);
            var buffer=new byte[81920]; int count; long written=0;
            while((count=input.Read(buffer,0,buffer.Length))>0) { written+=count; if(written>e.Length) throw new InvalidDataException("解压长度不匹配。"); output.Write(buffer,0,count); }
            if(written!=e.Length) throw new InvalidDataException("压缩包文件不完整。");
        }
    }
}
