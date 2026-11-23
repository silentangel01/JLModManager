using System.Windows;
using System.Windows.Controls;
using JLModManager.Core;

namespace JLModManager.App;

public record PackageOption(Package Package,string Branch)
{
    public string Label=>$"{Package.Version} · {Branch} · {Package.Key[..8]}";
}
public class ImportResultDialog : ManagerDialog
{
    readonly ListBox list=new(){SelectionMode=SelectionMode.Extended};
    readonly ComboBox target=new(){DisplayMemberPath="Name",MinWidth=220};
    public Profile? Target=>target.SelectedItem as Profile;
    public List<Package> Selected=>list.SelectedItems.Cast<ImportItem>().Select(i=>i.Package).ToList();
    record ImportItem(Package Package,bool Exists)
    { public override string ToString()=>$"{Package.Title} · {Package.Version}"+(Exists?" · 已收录":" · 新增"); }
    public ImportResultDialog(List<Package> packages,HashSet<string> existing,List<Profile> profiles,Profile current)
    {
        Title="导入完成";Width=650;Height=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(16)};var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        target.Items.Add("仅仓库");foreach(var p in profiles)target.Items.Add(p);target.SelectedItem=current;
        // Mixed string/profile items use their own ToString implementations.
        target.DisplayMemberPath="";row.Children.Add(target);
        var ok=new Button{Content="完成",Margin=new Thickness(8,0,0,0)};ok.Click+=(s,e)=>{if(Target!=null&&Selected.GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)){MessageBox.Show(this,"同一模组只能选择一个版本。","选择版本");return;}DialogResult=true;};row.Children.Add(ok);DockPanel.SetDock(row,Dock.Bottom);root.Children.Add(row);
        list.ItemsSource=packages.Select(p=>new ImportItem(p,existing.Contains(p.Key))).ToList();foreach(var item in list.Items.Cast<ImportItem>().GroupBy(i=>i.Package.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()==1).Select(g=>g.Single()))list.SelectedItems.Add(item);root.Children.Add(list);Content=root;
    }
}
public class SourceDialog : ManagerDialog
{
    readonly ComboBox source=new(){MinWidth=350};
    public string? SelectedId=>(source.SelectedItem as SiteMod)?.Id;
    public SourceDialog(List<SiteMod> catalog,string? current)
    {
        Title="来源";Width=500;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel{Margin=new Thickness(16)};source.Items.Add("未关联");foreach(var item in catalog)source.Items.Add(item);source.SelectedItem=catalog.FirstOrDefault(c=>c.Id==current) as object??"未关联";root.Children.Add(source);
        var link=new Button{Content="查看网页",Margin=new Thickness(0,12,0,0)};
        link.Click+=(s,e)=>{if(source.SelectedItem is SiteMod mod&&Uri.TryCreate(mod.Url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="www.7risha.com")System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});};root.Children.Add(link);
        var ok=new Button{Content="保存",Margin=new Thickness(0,12,0,0)};ok.Click+=(s,e)=>DialogResult=true;root.Children.Add(ok);Content=root;
    }
}

public record CatalogEntry(Package Package,string Branch,string State)
{
    public string Name=>Package.Name;
    public string Title=>Package.Title;
    public string VersionLabel=>Package.Version+" · "+Package.Key[..8];
    public string Author=>Package.Author;
}
