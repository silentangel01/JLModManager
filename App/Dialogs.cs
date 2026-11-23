using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using JLModManager.Core;

namespace JLModManager.App;
public class ManagerDialog : Window
{
    public ManagerDialog() { Style=(Style)Application.Current.FindResource(typeof(Window)); }
}
public class TextDialog : ManagerDialog
{
    readonly TextBox input=new();
    TextDialog(string title,string label,string initial)
    {
        Title=title;Width=440;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,12)});input.Text=initial;panel.Children.Add(input);
        var b=new Button{Content="保存",Margin=new Thickness(0,18,0,0),HorizontalAlignment=HorizontalAlignment.Right,IsDefault=true};b.Click+=(s,e)=>{if(!string.IsNullOrWhiteSpace(input.Text))DialogResult=true;};panel.Children.Add(b);Content=panel;
        Loaded+=(s,e)=>{input.Focus();input.SelectAll();};
    }
    public static string? Ask(Window owner,string title,string label,string initial){var w=new TextDialog(title,label,initial){Owner=owner};return w.ShowDialog()==true?w.input.Text.Trim():null;}
}
public class PathsDialog : ManagerDialog
{
    readonly TextBox game=new(),user=new();
    public string ModsPath=>game.Text.Trim();public string UserMods=>user.Text.Trim();
    public PathsDialog(string gamePath,string userMods,string data)
    {
        Title="连接游戏 Mods 目录";Width=720;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
        var p=new StackPanel{Margin=new Thickness(24)};game.Text=string.IsNullOrWhiteSpace(gamePath)?"":Path.Combine(gamePath,"Mods");user.Text=userMods;
        void Row(string label,TextBox input)
        {
            p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,12,0,8)});var row=new DockPanel();var b=new Button{Content="浏览",Margin=new Thickness(10,0,0,0)};DockPanel.SetDock(b,Dock.Right);b.Click+=(s,e)=>{var d=new OpenFolderDialog();if(d.ShowDialog(this)==true)input.Text=d.FolderName;};row.Children.Add(b);row.Children.Add(input);p.Children.Add(row);
        }
        Row("手动选择游戏目录下的 Mods 文件夹",game);Row("用户数据中的 Mods 目录（自定义 UserDataFolder 时请改此项）",user);
        p.Children.Add(new TextBlock{Text="管理器仓库："+data+"\n移动便携程序时，请连同 Data 文件夹一起移动。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,16)});
        var save=new Button{Content="保存目录",HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(s,e)=>{try{if(!Directory.Exists(ModsPath)||!Path.GetFileName(Files.Full(ModsPath)).Equals("Mods",StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(Path.GetDirectoryName(Files.Full(ModsPath))!,"7DaysToDie.exe")))throw new IOException("请选择游戏安装目录下的 Mods 文件夹，上一级应有 7DaysToDie.exe。");if(!Path.IsPathFullyQualified(UserMods))throw new IOException("用户 Mods 路径需为绝对路径。");Files.NoLinks(ModsPath);Files.NoLinks(UserMods);DialogResult=true;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};p.Children.Add(save);Content=p;
    }
}
public class LibraryDialog : ManagerDialog
{
    readonly ListBox list=new(){DisplayMemberPath=""};public Package? Selected=>list.SelectedItem as Package;
    public LibraryDialog(List<Package> packages)
    {
        Title="从仓库添加版本";Width=680;Height=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=new DockPanel{Margin=new Thickness(24)};var button=new Button{Content="加入当前方案",HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(button,Dock.Bottom);button.Click+=(s,e)=>{if(Selected!=null)DialogResult=true;};p.Children.Add(button);list.ItemsSource=packages;list.Margin=new Thickness(0,0,0,18);p.Children.Add(list);Content=p;
    }
}
