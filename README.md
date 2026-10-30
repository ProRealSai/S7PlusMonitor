# S7Plus 监视器

面向西门子 S7-1200 / S7-1500 的 Windows 桌面监视工具。通过 S7CommPlus（TLS 或 Legacy Challenge）浏览符号变量、周期读取、强制写入，并把监视点与趋势采样保存在本地 SQLite。

## 功能

- **连接**：多档案、主机/端口 102、超时、用户名密码（DPAPI 保护）、TLS / Legacy / Auto 回退
- **浏览**：在线符号目录、筛选、导出 Excel、加入监视
- **监视**：分批轮询（每批 800 点）、BOOL 翻转、写值、托盘驻留
- **趋势**：实时曲线与 SQLite 历史采样（单视图最多 500 条趋势项）

数据目录：`%LocalAppData%\S7PlusMonitor\`（库文件 `s7plus_monitor.db`）。

## 系统要求

- Windows 10 19041 或更高
- 开发：.NET 10 SDK
- 运行：使用仓库旁 `installer/output` 中的安装包时为 **win-x64 自包含**，无需预装运行时

## 构建

```powershell
dotnet build S7PlusMonitor.slnx -c Release
```

发布自包含包（供 Inno Setup 打包）：

```powershell
dotnet publish S7PlusMonitor\S7PlusMonitor.csproj -c Release -r win-x64 --self-contained true
```

安装脚本在上一级目录 `installer/S7PlusMonitor.iss`，编译后安装包输出到 `installer/output/`。

## PLC 驱动（S7CommPlusDriver 5.0）

程序已升级到 `DotNetProjects.S7CommPlusDriver` 5.0.0。现有连接 / 浏览 / 分批读 / 写走的是 `S7CommPlusClient`，**可以继续工作**，并自动获得 5.0 的批量读组包与更少分配。

当前 **已经用到**：

- `ConnectAsync`、`BrowseAsync`、`GetTagsBySymbolsAsync`、`ReadAsync`、`WriteAsync`、`LegitimateAsync`
- Auto / TLS / Legacy Challenge、BouncyCastle TLS
- 浏览结果里的数组维度（`ArrayElementCount` / `ArrayDimensions`）

当前 **没有接到界面或 `IPlcService`**（驱动里有、本程序未封装）：

- 读流水线 `ReadPipelineDepth`（默认 1，等于未开启）
- 变量/报警订阅、活动报警与文本表
- CPU 状态 / 启停 / 扫描周期 / 内存
- 块元数据、在线监视、TIS 轨迹
- 紧凑符号目录缓存、`GetCommunicationResourcesAsync`
- 工程会话角色 `SessionRole.EngineeringSystem`

要使用这些能力，需要扩展 `IPlcService` 和对应页面，不是换包就能出现菜单。

## 第三方组件

本仓库源码以 MIT 授权。依赖包各自遵循其许可证，主要包括：

- [DotNetProjects.S7CommPlusDriver](https://github.com/dotnetprojects/S7CommPlusDriver)（LGPL-3.0）
- sqlite-net-pcl、SQLitePCLRaw、SourceGear.sqlite3
- MaterialDesignThemes、LiveCharts、CommunityToolkit.Mvvm、ClosedXML

## 许可

见 [LICENSE](LICENSE)（MIT）。
