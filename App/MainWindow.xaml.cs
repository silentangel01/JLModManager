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
    public Func<string>? StateProvider {get;init;}
    public string State => StateProvider?.Invoke() ?? "";
    public string Kind => Package.HasCode ? "代码 · DLL" : "配置 / 资源";
    public bool Enabled {get=>Choice.Enabled;set {Choice.Enabled=value;PropertyChanged?.Invoke(this,new(nameof(Enabled)));Changed?.Invoke();PropertyChanged?.Invoke(this,new(nameof(State)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow : Window
{
    readonly Manager manager;
    bool refreshing,working;
    CancellationTokenSource? operationCancel;
    public MainWindow(Manager manager)
    {
        this.manager=manager; InitializeComponent();
        manager.Save(); Refresh();
        Closing+=(s,e)=>{ if(working) { e.Cancel=true; MessageBox.Show(this,"请等待当前文件操作结束。","正在处理"); } };
        if(manager.HasPending) Status.Text="部署待恢复";
    }
    void Refresh()
    {
        refreshing=true;
        try
        {
            Profiles.ItemsSource=null; Profiles.ItemsSource=manager.Settings.Profiles; Profiles.SelectedItem=manager.Current;
            ModGrid.ItemsSource=manager.Current.Mods.Select(c=>new ModRow {Choice=c,Package=manager.Package(c.Key),StateProvider=()=>manager.RowState(c),Changed=()=>{manager.Save();UpdateDeployment();CountLabel.Text=$"{manager.Current.Mods.Count} 个模组 · {manager.Current.Mods.Count(c=>c.Enabled)} 个启用";Status.Text="待应用";}}).ToList();
            GameLabel.Text=string.IsNullOrWhiteSpace(manager.Settings.GamePath) ? "未选择目录" : manager.GameMods;
            VersionNote.Text=manager.Current.TargetGameVersion; ProfileNotes.Text=manager.Current.Notes;
            var view=new System.Windows.Data.ListCollectionView(manager.Packages().Select(p=>new CatalogEntry(p,BranchLabel(p),manager.Current.Mods.Any(c=>c.Key==p.Key)?"已选择":"")).ToList());view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Name"));CatalogGrid.ItemsSource=view;
            DownloadLabel.Text=string.IsNullOrEmpty(manager.Settings.DownloadPath)?"未选择下载目录":manager.Settings.DownloadPath;
            CountLabel.Text=$"{manager.Current.Mods.Count} 个模组 · {manager.Current.Mods.Count(c=>c.Enabled)} 个启用";
            UpdateDeployment();
            HistoryText.Text=string.Join("\n",manager.History().Take(12).Select(t=>$"{t.Created.ToLocalTime():MM-dd HH:mm}   {t.After.ProfileName}   {Translate(t.Status)}"));
            Versions.ItemsSource=null; DetailTitle.Text="选择一个 Mod"; DetailText.Text="";
        }
        finally { refreshing=false; }
    }

    string BranchLabel(Package p) {var id=manager.Identify(p,false);return id.Status=="已确认"?id.Detail.Split(" · ")[0]:"未确认";}
    void UpdateDeployment() { var n=manager.Plan().Count;DeployLabel.Text="已部署："+manager.Deployment.ProfileName+"  ·  "+(n==0?"无变更":$"待应用 {n} 项"); }
    async void CheckClick(object sender,RoutedEventArgs e)
    {
        List<CheckItem>? rows=null;await Run("检查",()=>rows=manager.Preflight());
        if(rows!=null)new TableDialog(rows.Count==0?"未发现问题":"检查结果",rows){Owner=this}.ShowDialog();
    }
    void SourceClick(object sender,RoutedEventArgs e)
    {
        if(ModGrid.SelectedItem is not ModRow row)return;
        var dialog=new SourceDialog(manager.SiteCatalog(),manager.Settings.SourceBindings.GetValueOrDefault(row.Package.Key)){Owner=this};
        if(dialog.ShowDialog()==true){if(dialog.SelectedId is {} id)manager.Settings.SourceBindings[row.Package.Key]=id;else manager.Settings.SourceBindings.Remove(row.Package.Key);manager.Save();}
    }
    void PlanClick(object sender,RoutedEventArgs e)=>new TableDialog("方案变更",manager.Plan()){Owner=this}.ShowDialog();
    async void DetailsClick(object sender,RoutedEventArgs e) {if(ModGrid.SelectedItem is not ModRow r)return;IdentityResult? identity=null;await Run("校验",()=>identity=manager.Identify(r.Package));if(identity!=null)MessageBox.Show(this,r.Package.Description+"\n\n"+identity+"\n作者："+r.Package.Author+"\n内部名称："+r.Package.Name,"详情");}
    static string Translate(string s)=>s switch {"Prepared"=>"待恢复","Completed"=>"已完成","Restored"=>"已恢复",_=>s};
    async Task Run(string description,Action action,string success="已完成",bool cancellable=false)
    {
        if(working)return;working=true;operationCancel=new();Busy.Visibility=Visibility.Visible;BusyText.Text=description;
        CancelWork.Visibility=cancellable?Visibility.Visible:Visibility.Collapsed;CancelWork.IsEnabled=true;WorkBar.IsIndeterminate=true;WorkPercent.Text="";
        var progress=new Progress<WorkProgress>(p=>{BusyText.Text=p.Stage;WorkBar.IsIndeterminate=p.Total<=0;if(p.Total>0)WorkBar.Value=100.0*p.Done/p.Total;WorkPercent.Text=p.Total>0?$"{100.0*p.Done/p.Total:0}%":"";});
        try { await Task.Run(()=>{using var scope=new WorkScope(cancellable?operationCancel.Token:CancellationToken.None,progress);action();});Refresh();Status.Text=success; }
        catch(OperationCanceledException){Refresh();Status.Text="已取消";}
        catch(Exception ex){manager.Log(ex.ToString());Status.Text=ex.Message.StartsWith("应用失败，已恢复")?"应用失败，已恢复":"操作失败";if(manager.HasPending){Tabs.SelectedIndex=3;Status.Text="部署待恢复";}MessageBox.Show(this,ex.Message,"操作失败",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally {Busy.Visibility=Visibility.Collapsed;working=false;operationCancel.Dispose();operationCancel=null;}
    }
    void CancelClick(object sender,RoutedEventArgs e){operationCancel?.Cancel();CancelWork.IsEnabled=false;BusyText.Text="正在取消";}
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
    void ProfileChanged(object sender,SelectionChangedEventArgs e) { if(refreshing || Profiles.SelectedItem is not Profile p)return; manager.Settings.ActiveProfile=p.Id;manager.Save();Refresh();Status.Text="已选择方案"; }
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
    async Task ImportPath(string path)
    {
        var before=manager.Packages().Select(p=>p.Key).ToHashSet();List<Package>? imported=null;
        await Run("导入",()=>imported=manager.Import(path),"已收录",true);
        if(imported==null)return;
        var dialog=new ImportResultDialog(imported,before,manager.Settings.Profiles,manager.Current){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.Target is {} target)
        {
            var previous=manager.Settings.ActiveProfile;
            try {manager.Settings.ActiveProfile=target.Id;foreach(var p in dialog.Selected)manager.Select(p);if(manager.LastImportedManifest is {} pack){target.TargetGameVersion=pack.TargetGameVersion;target.Notes=pack.Notes;}}
            finally {manager.Settings.ActiveProfile=previous;manager.Save();}
            Refresh();Status.Text="已加入方案";
        }
    }
    async void ImportZipClick(object sender,RoutedEventArgs e) { var d=new OpenFileDialog{Filter="压缩包|*.zip;*.rar;*.7z",Title="导入 Mod 或整合包"};if(d.ShowDialog(this)==true)await ImportPath(d.FileName); }
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

            DetailTitle.Text=row.Name;DetailText.Text=manager.Identify(row.Package,false).Status=="已确认"?"指纹已收录":"指纹未确认";Versions.DisplayMemberPath="Label";Versions.ItemsSource=manager.Packages().Where(p=>p.Name.Equals(row.Package.Name,StringComparison.OrdinalIgnoreCase)).Select(p=>new PackageOption(p,BranchLabel(p))).ToList();Versions.SelectedValuePath="Package.Key";Versions.SelectedValue=row.Package.Key;
        }
        finally {refreshing=false;}
    }
    void VersionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(refreshing || Versions.SelectedItem is not PackageOption option || ModGrid.SelectedItem is not ModRow row || option.Package.Key==row.Choice.Key)return;
        var p=option.Package;var old=row.Package;var changes=Manager.ComparePackages(old,p);
        if(new TableDialog($"{old.Version} → {p.Version}",changes,true){Owner=this}.ShowDialog()!=true){refreshing=true;Versions.SelectedValue=old.Key;refreshing=false;return;}
        manager.Select(p,row.Enabled);Refresh();Status.Text="已完成";
    }
    void OpenPackageClick(object sender,RoutedEventArgs e) {if(ModGrid.SelectedItem is ModRow r)Open(manager.Payload(r.Choice.Key));}
    void RemoveClick(object sender,RoutedEventArgs e) {if(ModGrid.SelectedItem is not ModRow row)return;manager.Current.Mods.Remove(row.Choice);manager.Save();Refresh();Status.Text="待停用";}
    async void ScanClick(object sender,RoutedEventArgs e) { List<Installed>? rows=null;await Run("正在扫描安装…",()=>rows=manager.Scan(),"扫描完成");if(rows!=null)InstalledGrid.ItemsSource=rows; }
    async Task AdoptRows(List<Installed> rows)
    {
        if(rows.Count==0){MessageBox.Show(this,"请先扫描并选择可接管的 Mod。");return;}
        if(!Confirm("将 "+rows.Count+" 个现有 Mod 保存到仓库并加入当前方案。游戏文件保持原位；之后停用或更新这些 Mod 将由管理器处理。\n\n重新接管会以目前文件作为新基线，并保留一份当前版本。","接管现有安装"))return;
        await Run("正在保存现有安装…",()=>manager.Adopt(rows),"已接管");InstalledGrid.ItemsSource=manager.Scan();
    }
    async void AdoptClick(object sender,RoutedEventArgs e) {if(InstalledGrid.SelectedItem is Installed row)await AdoptRows([row]);}
    async void AdoptAllClick(object sender,RoutedEventArgs e) {await AdoptRows(manager.Scan().Where(r=>!r.Native&&r.Info!=null&&r.Error.Length==0).ToList());}
    void OpenInstalledClick(object sender,RoutedEventArgs e){if(InstalledGrid.SelectedItem is Installed r)Open(r.Path);}
    async Task Apply(bool launch)
    {
        List<CheckItem>? checks=null;List<PlanChange>? plan=null;
        await Run("检查",()=>{checks=manager.Preflight();plan=manager.Plan();});
        if(checks==null||plan==null)return;
        if(plan.Count>0||checks.Count>0||!launch)
        {
            while(true)
            {
                var review=new ReviewDialog(plan,checks,launch){Owner=this};
                if(review.ShowDialog()==true)break;
                if(!review.BackupRequested)return;
                await BackupSave();
            }
        }
        bool done=false;
        await Run("正在部署与校验…",()=>{manager.Apply();done=true;},"已应用");
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
    async void BackupClick(object sender,RoutedEventArgs e)=>await BackupSave();
    async Task BackupSave()
    {
        var path=Folder("选择要备份的存档文件夹",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"7DaysToDie","Saves"));if(path==null)return;
        var d=new SaveFileDialog{Filter="ZIP 备份|*.zip",FileName="7DTD-Saves-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(d.ShowDialog(this)==true)await Run("正在备份存档…",()=>manager.BackupSave(path,d.FileName),"已备份");
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
    void SaveProfileClick(object sender,RoutedEventArgs e){SaveProfileFields();Status.Text="已保存";}
    void NoteChanged(object sender,TextChangedEventArgs e){if(!refreshing){SaveProfileFields();Status.Text="已保存";}}
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
        await ImportPath(entry.Path);
    }
    void AddCatalogClick(object sender,RoutedEventArgs e)
    {
        if(CatalogGrid.SelectedItem is CatalogEntry entry){manager.Select(entry.Package);Refresh();Status.Text="已加入方案";}
    }
    async void CompareClick(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="版本清单|*.json"};if(d.ShowDialog(this)!=true)return;
        string? result=null;await Run("正在比较清单…",()=>result=manager.Compare(d.FileName));if(result!=null)MessageBox.Show(this,result,"方案比较");
    }
}
