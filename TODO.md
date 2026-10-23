# TODO

## 路径处理

### UNC 路径未正确前缀化（潜在缺陷）

- **位置**：`HideMe/Services/AdStreamService.cs:70-71`（`Prefixed`）
- **现状**：无条件添加 `\\?\` 前缀。本地路径 `C:\a\b` 正确；UNC 路径 `\\server\share\a` 会被拼成 `\\?\\\server\share\a`（非法），正确形式应为 `\\?\UNC\server\share\a`。
- **触发条件**：当前 UI 仅列出固定盘/可移动盘（`BrowserViewModel.cs:39`），网络路径不可达，因此是潜在缺陷而非现存故障。
- **修复方案（审核 5-2 已批准，方案 A，待执行）**：
  1. `Prefixed` 增加 UNC 分支：`\\server\share\...` → `\\?\UNC\server\share\...`；
  2. 字符串比较显式使用 `StringComparison.Ordinal`；
  3. `ZipFile` / `Directory.Delete` 等 BCL 调用保持原样（`App.config` 已启用长路径开关，无需额外前缀）。
- **验证**：编译通过；本地长路径（>260）隐藏/恢复回归；UNC 分支代码审查。
- **状态**：已修复（2026-10-24）；`Prefixed` 已支持 `\\?\UNC\` 并改用 `StringComparison.Ordinal` 比较

## 后续可选（不在本轮 12 条内）

- **危险路径保护**：隐藏/恢复时应拒绝盘符根（如 `C:\`）、系统目录、程序自身目录和保险库目录，避免误删。
- **恢复保真**：目前不保留原文件的属性、创建/修改时间与 ACL。
