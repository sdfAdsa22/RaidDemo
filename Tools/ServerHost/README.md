# RaidDemo 服务器面板（M9 补充：Windows 一键开服）

Windows 玩家开服用的图形界面：改 `server.config.json` → 一键启动服务器 → 看日志与状态。
它**不接管服务器逻辑**，只负责"把配置写对、把进程起停好、把现场（日志 / 状态页）摆出来"。
Linux 云主机上的对应物是 [`deploy/server.sh`](../../deploy/server.sh)（随 Linux 包分发、云主机同款）：
**两边共用同一份配置格式**
（`server.config.json`）与同一套启动参数。背景见
[`Docs/Modules/10_联机.md`](../../Docs/Modules/10_联机.md) 与
[`ADR-001`](../../Docs/Decisions/ADR-001-服务端权威架构与Netcode选型.md)（为什么是专用服务器）。

## 1. 为什么它是独立程序

Unity 的专用服务器构建（`Builds/ServerWindows/`）**没有界面**：要开服就得敲
`RaidDemoServer.exe -server -port 7777 -logFile Logs/server.log …`——记不住、敲错了还看不出来，
而这恰恰是"和朋友一起玩"的第一步。面板把这件事变成"双击 → 选目录 → 点启动"。

三个前提决定了它的形态：

- **不能依赖 Unity 运行时**：面板要能单独发布、单独打开，甚至在游戏没装的时候就能跑；
- **不能要求目标机器装 .NET**：包是发给玩家的，因此发布成自包含单文件（见第 3 节）；
- **不能只有界面**：打包脚本要生成默认配置、验收脚本要取证，所以同一份代码提供命令行形态
  （`--write-default-config` / `--check` / `--render`，见第 5 节）。界面只能人点，判据跑不起来。

## 2. 与 Unity 共用同一份配置模型

`RaidDemo.ServerHost.csproj` 通过 `<Compile Include="…">` **链接**（不复制）这一份源码：

```text
Assets/Game/Kernel/Pure/Server/ServerConfigDocument.cs   ← 配置模型（字段名就是 JSON 键）
```

该文件只依赖 `System.*`，所以能被 Unity 与 .NET 程序同时编译。好处是**字段只有一处定义**：
不会出现"面板写出的字段名，服务器不认识"这类只会在别人机器上出现的故障——字段名一旦发布
就不能再改，它同时是文件名协议与 JSON 键。

## 3. 构建与发布

```bash
# 调试构建（输出到 bin/Release/net8.0-windows/win-x64/）
dotnet build Tools/ServerHost/RaidDemo.ServerHost.csproj -c Release

# 自包含单文件发布（目标机器无需安装 .NET 运行时）
dotnet publish Tools/ServerHost/RaidDemo.ServerHost.csproj -c Release
# → bin/Release/net8.0-windows/win-x64/publish/RaidDemo.ServerHost.exe
```

打成给玩家的服务器包（含服务器产物 + 面板 + 默认配置 + 使用说明）：

```bash
pwsh -File Tools/ServerHost/pack_server_package.ps1                 # Windows + Linux
pwsh -File Tools/ServerHost/pack_server_package.ps1 -Target Linux -Zip
# → Builds/ServerPackage-Windows-<版本>/ 、Builds/ServerPackage-Linux-<版本>/
```

打包脚本的前置是先用编辑器菜单出服务器构建产物：
`RaidDemo/M9/构建专用服务器（Windows）` → `Builds/ServerWindows/`，
`RaidDemo/M9/构建专用服务器（Linux）` → `Builds/ServerLinux/`。
默认配置由**面板自己**写出（脚本不另抄一份默认值，抄第二份就必然漂移）。

## 4. 图形界面（玩家用法）

双击 `RaidDemo.ServerHost.exe`：面板默认管理**自己旁边**这台服务器（打包后服务器程序就在同一目录），
也可以点「浏览...」换到别的目录去管另一台。

| 区域 | 内容 |
| --- | --- |
| 服务器目录 | 显示**生效的绝对路径**（只读）；「浏览...」切换目录、「打开目录」在资源管理器里打开 |
| 配置 | 11 个配置项，两列排布；鼠标停在标签或输入框上会显示取值范围提示 |
| 按钮 | 「保存配置」「恢复默认」「启动服务器」「停止服务器」「复制连接信息」「打开日志目录」「打开状态页」 |
| 状态行 | 进程状态（运行中并带进程号 / 未运行）＋最近一次操作的提示；状态页读数每 2 秒刷新 |
| 日志 | 实时跟随 `Logs/server.log`（只读；超过 20 万字符会裁掉旧的一截，面板不会越开越卡） |

每个按钮的行为：

- **保存配置**：校验界面上的值 → 写入 `server.config.json`（先写临时文件再替换，并留下 `.bak`）。
  校验不通过会弹出具体原因并**不落盘**；
- **恢复默认**：把界面填回内置默认值（不立即落盘，点保存或直接启动才写；服务器运行中该项会变灰）；
- **启动服务器**：先自动保存配置，再以
  `-server -batchmode -nographics -logFile Logs/server.log -config server.config.json`
  启动服务器（工作目录 = 服务器目录；参数与 Linux 侧 `deploy/server.sh` 用同一组开关
  `-server -config … -batchmode -nographics -logFile …`；运行中该按钮会变灰）；
- **停止服务器**：先请状态页的停服入口停服（等 15 秒），仍未退出才结束服务器进程树；
- **复制连接信息**：把 `本机：127.0.0.1:<端口>` 与所有局域网 IPv4 地址（跳过回环与 169.254.*）
  放进剪贴板，直接发给要一起玩的人；
- **打开日志目录** / **打开状态页**：目录不存在就先建出来；状态页端口为 0（已关闭）时会说明原因。

关闭窗体时若服务器还在运行，会先问一次「停止它再关闭面板吗？」——避免"关掉面板以为服务器也关了"。

## 5. 命令行（打包脚本 / 验收脚本）

```bash
RaidDemo.ServerHost.exe [--dir <服务器目录>]                     # 图形界面
RaidDemo.ServerHost.exe --write-default-config <目录>            # 写出默认 server.config.json
RaidDemo.ServerHost.exe --check <目录>                           # 校验并打印生效值
RaidDemo.ServerHost.exe --render <输出.png> [--dir <目录>]       # 把界面渲染成 PNG
RaidDemo.ServerHost.exe --help
```

| 形态 | 成功 | 失败 |
| --- | --- | --- |
| `--write-default-config` | stdout `已写出默认配置：<绝对路径>`，退出码 0 | stderr `写出默认配置失败：<原因>`，退出码 1 |
| `--check` | 逐行打印生效值（服务器目录 / 配置文件 / 服务器程序 / 11 个字段），退出码 0 | stderr 逐行 `配置有误：<原因>`，退出码 1 |
| `--render` | stdout `已渲染界面：<绝对路径>`，退出码 0 | stderr `渲染界面失败：<原因>`，退出码 1 |

`--dir` 缺省 = 面板所在目录（双击时工作目录可能是 `C:\Windows\System32`，所以不能按工作目录算）。
输出固定为 UTF-8：标准输出被重定向到管道时 `Console.OutputEncoding` 不生效（写出的仍是系统
ANSI 代码页），脚本按 UTF-8 读会得到乱码，基于文案的断言就会莫名其妙地失败；所以命令行形态
显式重开一遍输出流。数字一律用不变文化（`2.5`，不是 `2,5`）。

> **为什么产物是控制台子系统（`OutputType=Exe`）而不是 `WinExe`：**
> PowerShell 对 GUI 子系统的进程**不会等待**——`& RaidDemo.ServerHost.exe --check …` 会立刻返回，
> 脚本读到的是上一条命令的 `$LASTEXITCODE`，stdout 也还没写完（打包脚本里的
> `& $panelPublish --write-default-config …` 正好踩在这一条上，会误判成败）。
> 控制台子系统总是被等待，退出码与输出都可靠。
> 代价是双击时 Windows 会为本进程建一个控制台窗口，因此 `Main` 的第一件事就是
> `ConsoleBridge.HideOwnConsoleWindow()` 把它藏起来；该函数先数一遍关联到本控制台的进程数，
> **只隐藏"本进程独占"的窗口**——从脚本调用时本进程共用父进程的控制台，绝不能动那个窗口。
> 验收脚本为了与"面板 exe 是什么子系统"解耦，仍统一用 `Start-Process -Wait -PassThru` 调用。

## 6. 配置文件 `server.config.json`

与服务器程序放在同一目录。**优先级：命令行参数 > 配置文件 > 内置默认值**——保留命令行优先，
是为了让既有的验收脚本与云主机部署命令一行都不用改。没写的字段回退内置默认（`-1` 是"未设置"的哨兵值，
因为 `raidDuration = 0`、`dashboardPort = 0` 都是合法且含义完全不同的取值）。

| 字段 | 默认 | 取值范围 | 说明 |
| --- | --- | --- | --- |
| `port` | `7777` | 1~65535 | 监听端口（UDP），客户端用它连接服务器 |
| `room` | `默认房间` | 1~24 个字符 | 房间名（一台服务器 = 一个房间） |
| `saveDir` | `server_saves` | 相对路径 | 只接受相对路径：不得以盘符 / 斜杠开头，也不得含 `..` |
| `logLevel` | `info` | `verbose` / `info` / `warning` / `error` | 写入日志的详细程度 |
| `map` | `GreyboxRaid` | 场景名 | 开局加载的地图场景；空串 = 不额外加载（自动化测试用法） |
| `raidDuration` | `480` | 0~7200 秒 | 战局时长上限；`0` = 不做超时判定 |
| `autoStart` | `0` | 0~600 秒 | 房间成立后自动开局的等待；`0` = 等房主手动开局 |
| `dashboardPort` | `8080` | 0~65535 | 只读状态页端口；`0` = 关闭状态页 |
| `discoveryPort` | `47777` | 0~65535 | 局域网自动发现端口；`0` = 关闭自动发现 |
| `grace` | `60` | 5~600 秒 | 掉线玩家的位置保留时长 |
| `watchdog` | `2.5` | `0` 或 1~30 秒 | 传输层自愈判定；`0` = 关闭看门狗 |

面板侧的取值范围与游戏侧解析器一致（权威定义在 `LaunchOptions.Parsing.cs`）：面板提前校验只是
让错误在"点启动之前"出现，而不是让玩家对着一段服务器日志猜。

## 7. 日志与状态页

- **日志**：`Logs/server.log`（与 Linux 侧 `deploy/server.sh` 同一个相对位置），面板里实时跟随；
  排查时只认这一份，避免满屏滚动丢历史。
- **状态页**：`http://127.0.0.1:<dashboardPort>/`（只读）。面板状态行每 2 秒刷新一次读数：
  `阶段 ｜ 房间 ｜ 在线 N ｜ 在局 N`。
- **脚本可读的状态**：`/status.json`（含 `listening`、`port`、房间与在线人数等），
  以及停服入口 `/?action=stop-server`（面板的「停止服务器」用的就是它）。

## 8. 验收

```bash
pwsh -NoProfile -File Tools/ServerHost/verify/p7_server_host_check.ps1
```

前置：面板产物已发布（`dotnet publish Tools\ServerHost\RaidDemo.ServerHost.csproj -c Release`）；
没构建时脚本会打印这条命令并以退出码 1 结束——面板是本脚本的被测对象，不做"跳过"。

| 组 | 内容 |
| --- | --- |
| A | 默认配置写出：文件生成 + stdout 绝对路径、11 个字段取值、重复执行幂等 |
| B | `--check` 输出契约：退出码、关键字段文案、缺配置时回退内置默认值、14 行齐全且顺序一致 |
| C | 非法配置必须被拒：端口 0 / 坏 JSON / `saveDir` 绝对路径 / `grace` 越界 |
| D | `--render` 离屏出界面截图（体积 > 10 KB 且 stdout 文案正确） |
| E | 真机链路：临时目录里生成配置 → 直接启动 `RaidDemoServer.exe` → 日志出现「大厅已就绪」
且带上配置里的端口 / 房间名 → `/status.json` 报文正确 → 停服入口返回 200 且进程 20 秒内退出 |

A~D 全部在 `%TEMP%\rd-serverhost-check` 下自造目录，不污染仓库；E 段缺少
`Builds\ServerWindows\RaidDemoServer.exe` 时整段记为**跳过**（并在 Detail 里写明原因）。

**实测（2026-09-16，本机 Windows）**：17 / 17 通过（面板 exe 已发布、服务器产物存在），
失败的 0 条、跳过的 0 条。

另有一个**服务器侧**的配置文件验收脚本（配置生效 / 命令行覆盖 / 坏配置拒绝启动，共 7 条）：

```bash
pwsh -NoProfile -File Tools/ServerHost/verify/server_config_check.ps1
```

它与 p7 的分工：p7 判"面板写出的配置能被服务器接受"，`server_config_check` 判
"配置与命令行同时存在时谁说了算"——两条都是分发时最容易出错的地方。

## 9. 界面截图（界面证据）

界面本身也是交付物，而自动化测试点不了鼠标。`--render` 把窗体离屏渲染成 PNG，用来核对
布局是否错位、控件是否被裁掉、字段有没有漏掉：

```bash
RaidDemo.ServerHost.exe --render %TEMP%\rd-serverhost-ui.png --dir Builds\ServerWindows
```

渲染图落在 `%TEMP%\rd-serverhost-ui.png`（临时产物，不入库）。核对时重点看三处：
分组与标签是否两列对齐、按钮行是否齐全且没有被挤出窗体、日志区是否占据剩余高度。

> **渲染图里能看到字段值**：本机在自包含单文件产物上实测，11 个输入框都画出了当前生效值
> （`7777` / `默认房间` / `server_saves` / `info` / `480` / `0` / `60` / `8080` / `47777` / `2.5`）。
> 但它仍不等于像素级真机截图：字体渲染、悬停提示与滚动条位置都可能与真实窗口有差别，
> 所以"布局对不对"看渲染图、"字段值对不对"看 `--check`（第 5 节），关键改动再在真机上打开看一眼。
>
> 渲染前会跑几轮消息循环再截图：首次运行自包含单文件时要把原生库解出来，启动明显更慢，
> 抓早了会得到"控件还没画出来"的图——那种图看着像缺陷，其实只是抓得太早。

## 10. 边界（刻意的设计约束）

- **Linux 没有图形面板**：云主机是无头环境，桌面程序跑不起来；统一的是配置格式与启动方式，
  而不是界面——Linux 侧用 [`deploy/server.sh`](../../deploy/server.sh)；
- **面板不接管服务器逻辑**：它只改配置、起停进程、读日志与状态页；游戏规则、存档、结算都在服务器里；
- **绿色包**：不写注册表、不安装系统服务、不申请管理员权限，删目录即卸载；
- **只管理自己启动的进程**：面板跟踪的是它自己拉起的那一个进程；已经在外面跑着的服务器，
  面板不会去猜、也不会去动它；
- **不做面板自更新**：面板随服务器包一起分发，更新 = 重新下载压缩包。
