# RaidDemo 启动器（M10 批次 2）

更新游戏**本体**的独立程序：拉取更新清单 → 比对本地版本 → 下载（支持断点续传）→
逐文件 SHA-256 校验 → 备份并原子替换 → 启动游戏。决策背景见
[`Docs/Decisions/ADR-007`](../../Docs/Decisions/ADR-007-采用自研启动器实现本体分发与更新.md)，
协议与批次计划见 [`Docs/Modules/11_热更新与分发.md`](../../Docs/Modules/11_热更新与分发.md)。

## 1. 为什么它是独立程序

Windows 下**运行中的 exe / dll 无法被覆盖**，所以"把游戏本体换成新版本"这件事
必须发生在游戏进程之外。这不是设计偏好，而是平台事实——把这条边界讲清楚本身就是工程判断的一部分。

它同时**不依赖 Unity 运行时**，因此可以单独发布、单独打开，甚至在游戏还没装的时候就存在。

## 2. 与 Unity 共用同一份协议实现

`RaidDemo.Launcher.csproj` 通过 `<Compile Include="…">` **链接**以下文件（不复制）：

```text
Assets/Game/Kernel/Pure/Updates/UpdateManifest.cs       ← 清单模型
Assets/Game/Kernel/Pure/Updates/UpdateManifestDiff.cs   ← 差异比对
Assets/Game/Kernel/Pure/Updates/UpdateFileHash.cs       ← SHA-256 工具
```

这三个文件只依赖 `System.*`，所以能被 Unity 与 .NET 工具同时编译。
好处是**协议只有一份实现**：不会出现"启动器认为该更新、游戏认为不用更新"这类
两端各自演化出来的差异。EditMode 里的 11 条测试同时覆盖了这份实现。

## 3. 构建与发布

```bash
# 调试构建（输出到 bin/Release/net8.0-windows/win-x64/）
dotnet build Tools/Launcher/RaidDemo.Launcher.csproj -c Release

# 自包含单文件发布（目标机器无需安装 .NET 运行时）
dotnet publish Tools/Launcher/RaidDemo.Launcher.csproj -c Release -o Builds/Tools/Launcher
```

## 4. 配置

| 文件 | 是否入库 | 用途 |
| --- | --- | --- |
| `launcher.config.json` | ✅ | 配置模板，只有占位地址（公开仓库不能带真实 IP） |
| `launcher.config.local.json` | ❌（.gitignore） | 本机真实地址；存在时**完全覆盖**模板 |

字段说明：

| 字段 | 含义 |
| --- | --- |
| `installRoot` | 游戏安装根（相对启动器目录），本体文件直接放在这里 |
| `gameExecutable` | 游戏可执行文件名（相对安装根） |
| `selectedSource` | 默认选中的更新源名称 |
| `sources[].manifestSource` | 更新源地址：`http(s)://…` 或本地目录（本机演示可不起服务） |
| `sources[].gameServer` | 该源对应的默认游戏服务器地址（启动游戏时作为 `-connect` 传入） |
| `sources[].editable` | 是否允许在界面上直接编辑地址（自定义源） |
| `extraGameArguments` | 追加给游戏的额外参数（空格分隔，可留空） |

安装根下的运行时目录：

```text
<安装根>/
├─ RaidDemo.exe                 ← 游戏本体
└─ .raiddemo/
   ├─ manifest.json             ← 本地清单快照（"我现在是什么版本"的唯一依据）
   ├─ launcher.log              ← 日志（超过 2 MB 轮转一次）
   ├─ staging/                  ← 下载暂存区（校验通过前不参与比对）
   └─ backup/<时间戳>/          ← 替换前的旧文件（回滚用；保留最近两份）
```

## 5. 两种用法

### 5.1 图形界面（玩家）

直接双击 `RaidDemo.Launcher.exe`：选更新源 → 看本地版本 → `检查更新` / `更新并启动`。
更新期间界面保持可响应，日志实时显示。

### 5.2 命令行（验收脚本 / CI）

```bash
RaidDemo.Launcher.exe --check  [--source <地址>] [--root <安装根>]
RaidDemo.Launcher.exe --update [--source <地址>] [--root <安装根>] [--launch] [--force]
```

退出码：`0` 成功（含"已是最新"），`1` 失败（含校验失败）。
输出固定为 UTF-8，便于脚本按文案断言。

## 6. 验收

```bash
pwsh -NoProfile -File Tools/Launcher/verify/p2_launcher_check.ps1
```

脚本在 `Builds/LauncherTest/` 沙盒里跑 13 条判据：

| 组 | 内容 |
| --- | --- |
| A | 首次安装：空安装根 → 全量计划 → 安装落盘 → 再检查为最新 |
| B | 增量更新：2 个文件变更 + 1 个文件删除 → 只处理这 3 项，快照更新 |
| C | 源切换：换源后按同一流程更新，文件生效、快照更新 |
| D | 失败回退：源哈希不符 → 退出码 1、安装文件未改动、快照保持旧版本 |

## 7. 边界（刻意的设计约束）

- **只更新本体**：资源层与代码层由游戏内热更（批次 4 / 5）负责，启动器不做这件事；
- **不做二进制差分**：按文件哈希比对已有足够增量，收益与复杂度不成正比（ADR-007）；
- **启动器自身不热更**：自身更新 = 重新下载压缩包（成本高、收益低）；
- **明文 HTTP + SHA-256**：演示环境够用，且不引入杀软误报风险；正式发行才需要 HTTPS 体系。
