using System.IO;
using System.Threading;
using System.Windows;
using JLModManager.Core;

namespace JLModManager.App;
public partial class App : Application
{
    Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string root=Path.Combine(AppContext.BaseDirectory,"Data");
            for(int i=0;i<e.Args.Length-1;i++) if(e.Args[i]=="--data") root=Path.GetFullPath(e.Args[++i]);
            string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..20];
            mutex=new Mutex(true,"Local\\JLModManager-"+key,out bool created);
            if(!created) { MessageBox.Show("这个数据仓库已有管理器窗口在运行。","JL Mod 管理器"); Shutdown(); return; }
            DispatcherUnhandledException+=(s,args)=>{ MessageBox.Show(args.Exception.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning); args.Handled=true; };
            new MainWindow(new Manager(root)).Show();
        }
        catch(Exception ex) { MessageBox.Show("管理器无法启动："+ex.Message+"\n请解压到可写目录，如 F:\\7DTD-ModManager。","JL Mod 管理器"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
