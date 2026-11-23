using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JLModManager.Core;
using JLModManager.App;

class Program
{
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        var game=Path.Combine(root,"FixtureGame");Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,"7DaysToDie.exe"),"TEST FIXTURE - NOT EXECUTABLE");
        var m=new Manager(Path.Combine(root,"Data")){GameRunning=()=>false};m.Settings.GamePath=game;m.Settings.UserModsPath=Path.Combine(root,"FixtureUser","Mods");m.Current.Name="朋友联机 · 电力与陷阱";m.Current.TargetGameVersion="v3.3 b18";m.Current.Notes="周末联机 / 使用作者完整模组包";m.Save();
        if(args.Length==1)
        {
            for(int i=1;i<=2;i++){var folder=Path.Combine(root,"fixtures","Example"+i);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"ModInfo.xml"),$"<xml><Name value=\"Example{i}\"/><DisplayName value=\"示例模组 {i}\"/><Version value=\"1.0\"/></xml>");foreach(var p in m.Import(folder))m.Select(p);}
        }
        else foreach(string source in args.Skip(1))foreach(var p in m.Import(source))m.Select(p);
        var app=new JLModManager.App.App();app.InitializeComponent();
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        int count=0;
        void Assert(bool value,string msg){if(!value)throw new Exception(msg);Console.WriteLine("PASS "+msg);count++;}
        void Flush(){app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Render(Window w,string name)
        {
            w.UpdateLayout();Flush();
            var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);
            var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(w.Background,null,new Rect(0,0,w.ActualWidth,w.ActualHeight));
            bmp.Render(backdrop);bmp.Render(w);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(root,name+".png"));png.Save(f);
            Assert(w.ActualWidth>300&&w.ActualHeight>100,"界面渲染 "+name);
        }
        try
        {
            var w=new MainWindow(m);w.Show();Flush();
            var grid=(DataGrid)w.FindName("ModGrid");Assert(grid.Items.Count==2,"两个 Mod 的仓库数据绑定");
            grid.SelectedIndex=0;Flush();Assert(grid.IsVisible && grid.ActualHeight>100,"首次打开方案列表可见");Render(w,"01-main");
            ((TextBox)w.FindName("VersionNote")).Text="v3.3 b18 · 测试";
            ((TextBox)w.FindName("ProfileNotes")).Text="备注保存测试";Flush();
            Assert(new Manager(m.Root).Current.Notes=="备注保存测试","方案备注自动保存");
            Assert(m.Current.TargetGameVersion=="v3.3 b18 · 测试","游戏版本备注绑定");
            var row=(ModRow)grid.Items[0];bool before=row.Enabled;row.Enabled=!before;Assert(m.Current.Mods[0].Enabled==!before,"启停绑定写入方案");row.Enabled=before;
            var chooser=(ComboBox)w.FindName("Profiles");Assert(chooser.SelectedItem==m.Current,"方案选择绑定");
            var buttons=Descendants(w).OfType<Button>().Select(b=>b.Content?.ToString()??"").ToList();
            Assert(!buttons.Any(b=>b.Contains("配置")),"不再显示单个 Mod 的配置编辑入口");
            var tabs=(TabControl)w.FindName("Tabs");tabs.SelectedIndex=1;Flush();Render(w,"03-installed");tabs.SelectedIndex=2;Flush();Render(w,"04-library");tabs.SelectedIndex=3;Flush();Render(w,"05-recovery");
            var paths=new PathsDialog(m.Settings.GamePath,m.Settings.UserModsPath,m.Root){Owner=w};paths.Show();Flush();
            Assert(paths.ModsPath.EndsWith("Mods"),"目录选择面向 Mods 文件夹");paths.Close();
            tabs.SelectedIndex=0;w.Width=980;w.Height=650;Flush();Render(w,"05-minimum-size");
            Assert(((TextBlock)w.FindName("DeployLabel")).Text.Contains("待应用 2 项"),"部署状态显示实际变更数");
            Assert(buttons.Contains("变更")&&buttons.Contains("检查"),"变更与检查入口");
            Assert(!Descendants(w).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text.Contains("接管后可")),"主界面无常驻解释");
            var review=new ReviewDialog(m.Plan(),m.Preflight(),true){Owner=w};review.Show();Flush();Render(review,"06-review");Assert(Descendants(review).OfType<Button>().Any(b=>Equals(b.Content,"应用并启动")),"启动检查合并确认");review.Close();
            var import=new ImportResultDialog(m.Packages(),[],m.Settings.Profiles,m.Current){Owner=w};import.Show();Flush();Render(import,"07-import");Assert(import.Selected.Count==2&&import.Target==m.Current,"导入选择方案与多个模组");import.Close();
            var sources=new SourceDialog(m.SiteCatalog(),null){Owner=w};sources.Show();Flush();Render(sources,"08-source");Assert(sources.SelectedId==null,"来源不自动关联");sources.Close();
            w.Close();Json.Write(Path.Combine(root,"smoke-results.json"),new{Passed=true,Checks=count,GameLaunched=false,RealModsModified=false});app.Shutdown();return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);Json.Write(Path.Combine(root,"smoke-results.json"),new{Passed=false,Error=e.ToString()});app.Shutdown();return 1;}
    }
}
