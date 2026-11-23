# JLModManager

七日杀 Windows Mod 管理器。

- 接管已安装模组，扫描下载目录，导入 ZIP、RAR、7z 和文件夹。
- 保存方案，启用、禁用及切换版本；应用前查看变更，一键启动。
- 同名版本归组，查看文件差异，通过关键文件指纹识别已收录分支。
- 启动前检查部署冲突、前置关联和作者声明的游戏版本范围。
- 导入进度与取消、整合包导入导出、部署恢复和存档备份。

## 使用

从 [Releases](https://github.com/silentangel01/JLModManager/releases) 下载并完整解压，运行 `JLModManager.exe`，选择游戏 Mods 目录。

RAR、7z 需要安装 7-Zip；加密包请先手动解压。更新保留 `Data`，部署前关闭游戏。

中文网资料来自 [7risha.com](https://www.7risha.com/)，当前仅收录少量条目；来源需手动关联，未核实项会提醒，不代表兼容性保证。指纹目前收录两个自制模组。

## 构建

Windows、.NET 10 SDK、PowerShell：

```powershell
./Build.ps1 -Test
```

输出目录：`publish`。测试使用隔离目录，不启动游戏。
