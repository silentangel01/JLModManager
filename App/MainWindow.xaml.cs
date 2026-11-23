using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using JLModManager.Core;

namespace JLModManager.App;

public class ModRow : INotifyPropertyChanged
{
    public Choice Choice {get;init;} = new();
    public Package Package {get;init;} = new();
    public Action? Changed {get;init;}
    public string Name => Package.Title;
    public string Version => Package.Version;
    public string Kind => Package.HasCode ? "代码 · DLL" : "配置 / 资源";
    public bool Enabled {get=>Choice.Enabled;set {Choice.Enabled=value;PropertyChanged?.Invoke(this,new(nameof(Enabled)));Changed?.Invoke();}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow : Window
{
    readonly Manager manager;
    bool refreshing,working;
    public MainWindow(Manager manager)
    {
        this.manager=manager; InitializeComponent();
        manager.Save(); Refresh();
        Closing+=(s,e)=>{ if(working) { e.Cancel=true; MessageBox.Show(this,"请等待当前文件操作结束。","正在处理"); } };
        if(manager.HasPending) Status.Text="发现未完成部署。请到“恢复与备份”先恢复。";
    }
    void Refresh()
    {
        refreshing=true;
        try
        {
            Profiles.ItemsSource=null; Profiles.ItemsSource=manager.Settings.Profiles; Profiles.SelectedItem=manager.Current;
            ModGrid.ItemsSource=manager.Current.Mods.Select(c=>new ModRow {Choice=c,Package=manager.Package(c.Key),Changed=()=>{manager.Save();CountLabel.Text=$"{manager.Current.Mods.Count} 个模组 · {manager.Current.Mods.Count(c=>c.Enabled)} 个启用";Status.Text="方案已保存，待应用。";}}).ToList();
            GameLabel.Text=string.IsNullOrWhiteSpace(manager.Settings.GamePath) ? "尚未连接 · 请手动选择游戏下的 Mods 文件夹" : manager.GameMods;
            VersionNote.Text=manager.Current.TargetGameVersion; ProfileNotes.Text=manager.Current.Notes;
            CatalogGrid.ItemsSource=manager.Packages();
            DownloadLabel.Text=string.IsNullOrEmpty(manager.Settings.DownloadPath)?"选择你的模组下载目录，原文件会保留在原处。":manager.Settings.DownloadPath;
            CountLabel.Text=$"{manager.Current.Mods.Count} 个模组 · {manager.Current.Mods.Count(c=>c.Enabled)} 个启用";
            DeployLabel.Text="当前部署："+manager.Deployment.ProfileName+"   ·   "+manager.GameBuild()+"   ·   修改待应用";
            HistoryText.Text=string.Join("\n",manager.History().Take(12).Select(t=>$"{t.Created.ToLocalTime():MM-dd HH:mm}   {t.After.ProfileName}   {Translate(t.Status)}"));
            Versions.ItemsSource=null; DetailTitle.Text="选择一个 Mod"; DetailText.Text="拖入 ZIP 或文件夹即可导入。已有安装可在“本机安装”中接管。";
        }
        finally { refreshing=false; }
    }
    static string Translate(string s)=>s switch {"Prepared"=>"待恢复","Completed"=>"已完成","Restored"=>"已恢复",_=>s};
    async Task Run(string description,Action action,string success="已完成。")
    {
        if(working) return; working=true; Busy.Visibility=Visibility.Visible; BusyText.Text=description;
        try { await Task.Run(action); Refresh(); Status.Text=success; }
        catch(Exception ex) { manager.Log(ex.ToString()); Status.Text="操作未完成，原因为："+ex.Message; MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning); }
        finally { Busy.Visibility=Visibility.Collapsed; working=false; }
    }
    bool Confirm(string text,string title="确认操作") => MessageBox.Show(this,text,title,MessageBoxButton.OKCancel,MessageBoxImage.Information)==MessageBoxResult.OK;
    string? Folder(string title,string? initial=null)
    {
        var d=new OpenFolderDialog {Title=title}; if(initial!=null && Directory.Exists(initial)) d.InitialDirectory=initial;
        return d.ShowDialog(this)==true ? d.FolderName : null;
    }
    void Open(string path) { if(!Directory.Exists(path)) throw new IOException("目录不存在。"); Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); }
    void PathsClick(object sender,RoutedEventArgs e)
    {
        var dialog=new PathsDialog(manager.Settings.GamePath,manager.Settings.UserModsPath,manager.Root){Owner=this};
        if(dialog.ShowDialog()!=true) return;
        manager.SetModsDirectory(dialog.ModsPath,dialog.UserMods); Refresh(); Tabs.SelectedIndex=1;ScanClick(sender,e);
    }
    void ProfileChanged(object sender,SelectionChangedEventArgs e) { if(refreshing || Profiles.SelectedItem is not Profile p)return; manager.Settings.ActiveProfile=p.Id;manager.Save();Refresh();Status.Text="已选择方案，尚未部署。"; }
    void NewProfileClick(object sender,RoutedEventArgs e)
    {
        var name=TextDialog.Ask(this,"新建方案","方案名称","新方案");if(name==null)return;
        var p=new Profile{Name=name};manager.Settings.Profiles.Add(p);manager.Settings.ActiveProfile=p.Id;manager.Save();Refresh();
    }
    void CloneProfileClick(object sender,RoutedEventArgs e)
    {
        var name=TextDialog.Ask(this,"复制方案","新方案名称",manager.Current.Name+" 副本");if(name==null)return;
        var p=Json.Clone(manager.Current);p.Id=Guid.NewGuid().ToString("N");p.Name=name;manager.Settings.Profiles.Add(p);manager.Settings.ActiveProfile=p.Id;manager.Save();Refresh();
    }
    void RenameProfileClick(object sender,RoutedEventArgs e) { var name=TextDialog.Ask(this,"重命名方案","方案名称",manager.Current.Name);if(name==null)return;manager.Current.Name=name;manager.Save();Refresh(); }
    void DeleteProfileClick(object sender,RoutedEventArgs e)
    {
        if(manager.Settings.Profiles.Count==1) { MessageBox.Show(this,"至少保留一个方案。");return; }
        if(!Confirm("删除方案“"+manager.Current.Name+"”？仓库和当前游戏文件会保留。"))return;
        manager.Settings.Profiles.Remove(manager.Current);manager.Settings.ActiveProfile=manager.Settings.Profiles[0].Id;manager.Save();Refresh();
    }
    async Task ImportPath(string path) => await Run("正在导入并校验…",()=>{var list=manager.Import(path);foreach(var p in list)manager.Select(p); if(manager.LastImportedManifest is { } pack) {manager.Current.TargetGameVersion=pack.TargetGameVersion;manager.Current.Notes=pack.Notes;manager.Save();}},"已加入当前方案。检查启用项后再应用。");
    async void ImportZipClick(object sender,RoutedEventArgs e) { var d=new OpenFileDialog{Filter="ZIP 压缩包|*.zip",Title="导入 Mod 或整合包"};if(d.ShowDialog(this)==true)await ImportPath(d.FileName); }
    async void ImportFolderClick(object sender,RoutedEventArgs e) { var path=Folder("选择 Mod 或包含多个 Mod 的目录");if(path!=null)await ImportPath(path); }
    async void OnDrop(object sender,DragEventArgs e)
    {
        if(working || !e.Data.GetDataPresent(DataFormats.FileDrop))return;
        if(e.Data.GetData(DataFormats.FileDrop) is string[] paths) foreach(var path in paths) await ImportPath(path);
    }
    void LibraryClick(object sender,RoutedEventArgs e)
    {
        var w=new LibraryDialog(manager.Packages()){Owner=this};if(w.ShowDialog()==true && w.Selected!=null){manager.Select(w.Selected);Refresh();Status.Text="已加入方案，待应用。";}
    }
    void ModSelected(object sender,SelectionChangedEventArgs e)
    {
        if(refreshing || ModGrid.SelectedItem is not ModRow row)return;
        refreshing=true;
        try
        {
            string description=row.Package.Description+"\n\n身份识别："+manager.Identify(row.Package);
            DetailTitle.Text=row.Name;DetailText.Text=description+"\n\n作者："+row.Package.Author+"\n内部名称："+row.Package.Name+"\n内容标识："+row.Package.Key[..12];Versions.DisplayMemberPath="VersionLabel";Versions.ItemsSource=manager.Packages().Where(p=>p.Name.Equals(row.Package.Name,StringComparison.OrdinalIgnoreCase)).ToList();Versions.SelectedValuePath="Key";Versions.SelectedValue=row.Package.Key;
        }
        finally {refreshing=false;}
    }
    void VersionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(refreshing || Versions.SelectedItem is not Package p || ModGrid.SelectedItem is not ModRow row || p.Key==row.Choice.Key)return;
        manager.Select(p,row.Enabled);Refresh();Status.Text="版本已切换，待应用。使用所选包的完整文件，不合并旧配置。";
    }
    void OpenPackageClick(object sender,RoutedEventArgs e) {if(ModGrid.SelectedItem is ModRow r)Open(manager.Payload(r.Choice.Key));}
    void RemoveClick(object sender,RoutedEventArgs e) {if(ModGrid.SelectedItem is not ModRow row)return;manager.Current.Mods.Remove(row.Choice);manager.Save();Refresh();Status.Text="已从方案移除，实际停用需应用方案。";}
    async void ScanClick(object sender,RoutedEventArgs e) { List<Installed>? rows=null;await Run("正在扫描安装…",()=>rows=manager.Scan(),"扫描完成；接管前不会改变游戏文件。");if(rows!=null)InstalledGrid.ItemsSource=rows; }
    async Task AdoptRows(List<Installed> rows)
    {
        if(rows.Count==0){MessageBox.Show(this,"请先扫描并选择可接管的 Mod。");return;}
        if(!Confirm("将 "+rows.Count+" 个现有 Mod 保存到仓库并加入当前方案。游戏文件保持原位；之后停用或更新这些 Mod 将由管理器处理。\n\n重新接管会以目前文件作为新基线，并保留一份当前版本。","接管现有安装"))return;
        await Run("正在保存现有安装…",()=>manager.Adopt(rows),"已接管。可以在方案中管理版本与启停。");InstalledGrid.ItemsSource=manager.Scan();
    }
    async void AdoptClick(object sender,RoutedEventArgs e) {if(InstalledGrid.SelectedItem is Installed row)await AdoptRows([row]);}
    async void AdoptAllClick(object sender,RoutedEventArgs e) {await AdoptRows(manager.Scan().Where(r=>!r.Native&&r.Info!=null&&r.Error.Length==0).ToList());}
    void OpenInstalledClick(object sender,RoutedEventArgs e){if(InstalledGrid.SelectedItem is Installed r)Open(r.Path);}
    async Task Apply(bool launch)
    {
        string? review=null;
        await Run("正在检查文件与部署计划…",()=>{manager.EnsureStopped();review=string.Join("\n",manager.Review());},"检查完成。");
        if(review==null)return;
        if(!launch && !Confirm(review+"\n\n将保留旧文件以便回退。新增内容的停用可能影响存档；请为重要存档保留备份。"+(launch?"\n部署成功后，以不启用 EAC 的方式启动游戏。":""),"应用“"+manager.Current.Name+"”"))return;
        bool done=false;
        await Run("正在部署与校验…",()=>{manager.Apply();done=true;},"方案已应用。旧文件已保留，可在恢复页回退。");
        if(done&&launch)
        {
            var start=new ProcessStartInfo(Path.Combine(manager.Settings.GamePath,"7DaysToDie.exe")){WorkingDirectory=manager.Settings.GamePath,UseShellExecute=true};
            start.ArgumentList.Add("-UserDataFolder="+Path.GetDirectoryName(Files.Full(manager.Settings.UserModsPath)));
            Process.Start(start);
        }
    }
    async void ApplyClick(object sender,RoutedEventArgs e)=>await Apply(false);
    async void LaunchClick(object sender,RoutedEventArgs e){SaveProfileFields();await Apply(true);}
    async void RecoverClick(object sender,RoutedEventArgs e)=>await Run("正在恢复未完成部署…",manager.Recover);
    async void UndoClick(object sender,RoutedEventArgs e){if(Confirm("恢复上一次部署前的 Mod 文件？存档进度不变，方案编辑内容也会保留。"))await Run("正在回退…",manager.Undo,"已恢复部署前的 Mod 文件。");}
    void OpenDataClick(object sender,RoutedEventArgs e)=>Open(manager.Root);
    async void BackupClick(object sender,RoutedEventArgs e)
    {
        var path=Folder("选择要备份的存档文件夹",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"7DaysToDie","Saves"));if(path==null)return;
        var d=new SaveFileDialog{Filter="ZIP 备份|*.zip",FileName="7DTD-Saves-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(d.ShowDialog(this)==true)await Run("正在备份存档…",()=>manager.BackupSave(path,d.FileName),"存档备份完成。恢复存档需手动操作。");
    }
    async Task Export(bool full)
    {
        if(full&&!Confirm("仅导出当前方案中已启用的 Mod 和配置，不包含外部安装或存档。请确认这些 Mod 允许再分发。","导出整合包"))return;
        var d=new SaveFileDialog{Filter=full?"整合包 ZIP|*.zip":"版本清单 JSON|*.json",FileName=full?"7DTD-ModPack.zip":"7DTD-ModList.json"};
        if(d.ShowDialog(this)==true)await Run("正在导出并校验…",()=>manager.Export(d.FileName,full),"已导出："+d.FileName);
    }
    async void ExportPackClick(object sender,RoutedEventArgs e)=>await Export(true);
    async void ExportManifestClick(object sender,RoutedEventArgs e)=>await Export(false);
    void SaveProfileFields()
    {
        manager.Current.TargetGameVersion=VersionNote.Text.Trim();manager.Current.Notes=ProfileNotes.Text.Trim();manager.Save();
    }
    void SaveProfileClick(object sender,RoutedEventArgs e){SaveProfileFields();Status.Text="方案已保存，点击启动方案即可应用并启动。";}
    void NoteChanged(object sender,TextChangedEventArgs e){if(!refreshing){SaveProfileFields();Status.Text="方案备注已自动保存。";}}
    async void ChooseDownloadsClick(object sender,RoutedEventArgs e)
    {
        var path=Folder("选择 Mod 网站下载内容所在的目录",manager.Settings.DownloadPath);if(path==null)return;
        manager.Settings.DownloadPath=path;manager.Save();await ScanDownloads();
    }
    async Task ScanDownloads()
    {
        if(string.IsNullOrEmpty(manager.Settings.DownloadPath)){Status.Text="请先选择下载目录。";return;}
        List<DownloadEntry>? entries=null;await Run("正在扫描下载目录…",()=>entries=manager.ScanDownloads(manager.Settings.DownloadPath));if(entries!=null)DownloadGrid.ItemsSource=entries;
    }
    async void ScanDownloadsClick(object sender,RoutedEventArgs e)=>await ScanDownloads();
    async void ImportDownloadClick(object sender,RoutedEventArgs e)
    {
        if(DownloadGrid.SelectedItem is not DownloadEntry entry){Status.Text="请先选择下载列表中的一项。";return;}
        await Run("正在收录到仓库…",()=>manager.Import(entry.Path),"已收录到仓库；选择仓库中的版本可加入方案，尚未安装进游戏。");
    }
    void AddCatalogClick(object sender,RoutedEventArgs e)
    {
        if(CatalogGrid.SelectedItem is Package p){manager.Select(p);Refresh();Status.Text="已加入当前方案，启用状态可在方案页调整。";}
    }
    async void CompareClick(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="版本清单|*.json"};if(d.ShowDialog(this)!=true)return;
        string? result=null;await Run("正在比较清单…",()=>result=manager.Compare(d.FileName));if(result!=null)MessageBox.Show(this,result,"方案比较");
    }
}
