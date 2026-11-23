using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace JLModManager.Core;

public static class Archives
{
    public static string? Find7Zip()
    {
        var dirs=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"7-Zip")};
        if(OperatingSystem.IsWindows())
        {
            using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\7-Zip");
            if(key?.GetValue("Path") is string dir) dirs.Add(dir);
        }
        return dirs.Select(d=>Path.Combine(d,"7z.exe")).FirstOrDefault(File.Exists);
    }
    static Process Start(string exe,IEnumerable<string> args)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8};
        foreach(var a in args)info.ArgumentList.Add(a);
        var p=Process.Start(info)??throw new IOException("无法启动 7-Zip。");p.StandardInput.Close();return p;
    }
    public static void Extract(string archive,string target)
    {
        var exe=Find7Zip()??throw new IOException("请安装 7-Zip，或先解压后导入文件夹。");
        archive=Path.GetFullPath(archive);Files.NoLinks(archive);
        var token=WorkScope.Current?.Token??CancellationToken.None;
        using var list=Start(exe,["l","-slt","-sccUTF-8","-ba","--",archive]);
        using var cancelList=token.Register(()=>{try{list.Kill(true);}catch(InvalidOperationException){}});
        var error=list.StandardError.ReadToEndAsync();
        // Bound listing memory before parsing untrusted entry names.
        var text=new StringBuilder();var buffer=new char[8192];int n;
        while((n=list.StandardOutput.Read(buffer))>0){WorkScope.Check();text.Append(buffer,0,n);if(text.Length>32*1024*1024){list.Kill(true);throw new IOException("压缩包目录过大。");}}
        list.WaitForExit();WorkScope.Check();_ = error.GetAwaiter().GetResult();
        if(list.ExitCode!=0)throw new IOException("无法读取压缩包。加密包请先手动解压。");
        var entries=Parse(text.ToString(),target);
        foreach(var entry in entries)
        {
            WorkScope.Check();var dest=Files.Under(target,entry.Path);
            if(entry.Directory){Directory.CreateDirectory(dest);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // Stream each exact entry into a path validated by our own code.
            // 7-Zip never writes archive-controlled paths or creates links.
            using var process=Start(exe,["e","-so","-spd","-y","--",archive,entry.Path]);
            using var cancel=token.Register(()=>{try{process.Kill(true);}catch(InvalidOperationException){}});
            var errors=process.StandardError.ReadToEndAsync();
            try
            {
                using var output=new FileStream(dest,FileMode.CreateNew);
                WorkScope.CopyStream(process.StandardOutput.BaseStream,output,entry.Size,"解压 "+Path.GetFileName(entry.Path));
                process.WaitForExit();WorkScope.Check();_ = errors.GetAwaiter().GetResult();
                if(process.ExitCode!=0)throw new IOException("解压失败。请检查压缩包是否损坏或加密。");
            }
            catch(Exception) when(token.IsCancellationRequested){throw new OperationCanceledException(token);}
            finally {if(!process.HasExited)process.Kill(true);}
        }
    }
    public record Entry(string Path,long Size,bool Directory);
    public static List<Entry> Parse(string text,string target)
    {
        var result=new List<Entry>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var block in text.Replace("\r\n","\n").Split("\n\n",StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=new Dictionary<string,string>();
            foreach(var line in block.Split('\n',StringSplitOptions.RemoveEmptyEntries))
            {
                int at=line.IndexOf(" = ",StringComparison.Ordinal);if(at<0)throw new InvalidDataException("不支持的压缩包目录格式。");
                if(!fields.TryAdd(line[..at],line[(at+3)..]))throw new InvalidDataException("压缩包目录字段重复。");
            }
            if(!fields.TryGetValue("Path",out var path)||!fields.TryGetValue("Size",out var raw)||!long.TryParse(raw,out var size)||size<0)throw new InvalidDataException("压缩包目录无效。");
            if(fields.Any(f=>f.Key.Contains("Link",StringComparison.OrdinalIgnoreCase)&&f.Value.Length>0)||fields.GetValueOrDefault("Encrypted")=="+"||fields.GetValueOrDefault("Attributes","").Contains("l",StringComparison.Ordinal))throw new InvalidDataException("不支持链接或加密压缩包，请先手动解压。");
            var normalized=path.Replace('\\','/').TrimEnd('/');_=Files.Under(target,normalized);
            if(!names.Add(normalized))throw new InvalidDataException("压缩包路径重复。");
            bool directory=fields.GetValueOrDefault("Folder")=="+"||fields.GetValueOrDefault("Attributes","").StartsWith("D");
            total=checked(total+size);if(total>20L*1024*1024*1024||result.Count>=100000)throw new IOException("压缩包超过大小限制。");
            result.Add(new(path,size,directory));
        }
        return result;
    }
}
