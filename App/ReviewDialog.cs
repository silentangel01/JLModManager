using System.Windows;
using System.Windows.Controls;
using JLModManager.Core;

namespace JLModManager.App;

public class ReviewDialog : ManagerDialog
{
    public ReviewDialog(List<PlanChange> plan,List<CheckItem> checks,bool launch)
    {
        Title="应用方案";Width=860;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(16)};var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="取消",IsCancel=true};buttons.Children.Add(cancel);
        var ok=new Button{Content=launch?"应用并启动":"应用",IsEnabled=!checks.Any(c=>c.Level=="阻止")};ok.Click+=(s,e)=>DialogResult=true;buttons.Children.Add(ok);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header=$"变更 {plan.Count}",Content=CreateGrid(plan)});
        tabs.Items.Add(new TabItem{Header=$"检查 {checks.Count}",Content=CreateGrid(checks)});
        tabs.SelectedIndex=checks.Count>0?1:0;root.Children.Add(tabs);Content=root;
    }
    static DataGrid CreateGrid(System.Collections.IEnumerable items)
    {
        var grid=new DataGrid{ItemsSource=items,AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false};
        var headers=new Dictionary<string,string>{{"Action","变更"},{"Name","模组"},{"Before","当前"},{"After","应用后"},{"Level","状态"},{"Mod","项目"},{"Message","详情"}};
        grid.AutoGeneratingColumn+=(s,e)=>e.Column.Header=headers.GetValueOrDefault(e.PropertyName,e.PropertyName);return grid;
    }
}
