# HideMe

Windows 桌面小工具：把文件/文件夹从原位置隐藏，内容存放到各盘回收站当前用户 SID 目录的 NTFS 替代数据流（ADS）中。

> **注意：这是"隐藏"而不是"加密"。** 内容是明文存储的，任何能读取 ADS 的工具都可以读出内容。

## 工作原理

- **隐藏**：把内容写入 `X:\$Recycle.Bin\<当前用户SID>` 文件夹的 ADS 流 `HideMe_<id>`，元数据写入 `HideMeMeta_<id>`，然后永久删除原条目。
- **文件夹**：先压缩为 zip，再写入数据流。
- **恢复**：从数据流写回原路径；先写同目录临时文件、校验 SHA-256，再备份并原子换入目标。
- **扫描**：遍历固定/可移动磁盘的保险库，读取元数据流；`%LOCALAPPDATA%\HideMe\index.json` 作为元数据冗余，可在元数据流丢失时恢复条目。

## 界面

- **导航**：基于 [LightUi](https://github.com/vvsnow/lightui) 的无边框窗口，左侧列表导航（文件浏览器 / 隐藏列表 / 设置），右上角切换明暗主题，与 LightUi.Demo 一致。
- **文件浏览器**：盘符树、Shell 图标、右键菜单（隐藏 / 在资源管理器中打开 / 刷新）、进度条与取消；每次切换到该页时自动刷新，保留展开状态与选中项。
- **隐藏列表**：自动/手动扫描，显示状态（正常 / 元数据缺失 / 数据丢失），恢复选中项；恢复期间可取消。
- **设置**：隐藏前确认、是否显示隐藏/系统文件（保存后文件树即时刷新）。

## 数据位置

| 内容 | 路径 |
|---|---|
| 保险库 | `<盘符>:\$Recycle.Bin\<SID>`（`HideMe_*` / `HideMeMeta_*` 流） |
| 索引 | `%LOCALAPPDATA%\HideMe\index.json` |
| 设置 | `%LOCALAPPDATA%\HideMe\settings.json` |
| 日志 | `%LOCALAPPDATA%\HideMe\logs\yyyyMMdd.log`（保留 7 天） |

## 重要风险

- **明文存储**：不是加密，知道 ADS 位置即可读出内容。
- **不参与系统备份**：回收站通常不在文件历史/系统备份范围内。
- **不要删除或重建 `$Recycle.Bin\<SID>`**：配置文件重置、手动清理、重装系统都会导致数据永久丢失。
- **换机器无法恢复**：不同机器的用户 SID 不同，保险库不可见。
- **清理工具/杀毒软件**可能删除或改写 ADS。
- **仅支持 NTFS**：FAT32/exFAT 可移动盘不支持 ADS，隐藏时会给出明确错误。
- 同一文件夹上流数量极多时，枚举会变慢。

## 构建

- .NET 10（`net10.0-windows`），WPF，Caliburn.Micro 4.0.230，[LightUi](https://github.com/vvsnow/lightui) 1.1.3，PropertyChanged.Fody。
- SDK 风格项目：`dotnet build HideMe/HideMe.csproj`。
- `App.xaml` 合并 LightUi 的 `ControlsResources.xaml`；DPI 感知（PerMonitorV2）由 `app.manifest` 声明。
- 旧 `.NET Framework` 时代的 `Properties\AssemblyInfo.cs`、`Settings.Designer.cs` 仍保留在磁盘上，但已通过 csproj 的 `Compile Remove` 排除出编译。
- 部署：目标机需要 .NET 10 Desktop Runtime；或使用 `dotnet publish -c Release -r win-x64 --self-contained true` 发布自包含版本。
- Fody 约定：继承 `Screen` 的 ViewModel 不需要 `[AddINotifyPropertyChangedInterface]`；普通模型类（如 `HiddenItem`）需要。属性变化后的副作用可写成 `On<属性名>Changed` 方法。
