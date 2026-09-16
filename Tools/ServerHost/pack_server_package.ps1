<#
.SYNOPSIS
    组装 RaidDemo 服务器分发包（Windows 面板版 / Linux 脚本版）。

.DESCRIPTION
    服务器分发不是"发一个 exe"：Windows 玩家需要"双击就开服"的面板，
    Linux 云主机需要"一条命令开服"的脚本与配置文件。两者共用同一份 server.config.json 格式，
    因此打包也必须一起做——分开手工拼装，早晚会出现"Windows 包里的字段 Linux 版不认识"。

    本脚本做四件事：
      1) 用 dotnet publish 出面板（自包含单文件），仓库里不存二进制；
      2) 调面板的 --write-default-config 生成默认配置——默认值只有一处定义，
         脚本不另抄一份常量（抄一份就必然漂移）；
      3) 把 Unity 的服务器构建产物与面板/脚本拼成两个目录；
      4) 可选地打成 zip / tar.gz。

    前置条件：先用编辑器菜单出服务器包——
      RaidDemo/M9/构建专用服务器（Windows） → Builds/ServerWindows/
      RaidDemo/M9/构建专用服务器（Linux）   → Builds/ServerLinux/

.EXAMPLE
    pwsh -File Tools/ServerHost/pack_server_package.ps1
    pwsh -File Tools/ServerHost/pack_server_package.ps1 -Target Linux -Zip
#>
[CmdletBinding()]
param(
    # 版本号；缺省从 ProjectSettings 的 bundleVersion 读取（避免手写版本号与工程不一致）。
    [string]$Version,

    # 打包目标：Both / Windows / Linux。
    [ValidateSet("Both", "Windows", "Linux")]
    [string]$Target = "Both",

    # 是否额外产出压缩包（zip / tar.gz）。
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$buildsRoot = Join-Path $repoRoot "Builds"
$panelProject = Join-Path $PSScriptRoot "RaidDemo.ServerHost.csproj"
$panelPublish = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\win-x64\publish\RaidDemo.ServerHost.exe"

# ---------------------------------------------------------------------------
# 下面是实现细节，按打包顺序排列：读版本 → 出面板 → 各平台拼装 → 说明文件。
# 全部先定义、最后由 Invoke-Packaging 调用：PowerShell 是顺序执行的语言，
# 顶层直接调用"后面才定义"的函数会报"无法识别的命令"。
# ---------------------------------------------------------------------------

<#
打包主流程：校验前置产物 → 出面板 → 按目标拼装。
#>
function Invoke-Packaging {
    if (-not $Version) {
        $script:Version = Read-BundleVersion
    }

    Write-Host "RaidDemo 服务器包打包：版本 $Version，目标 $Target" -ForegroundColor Cyan

    if ($Target -eq "Both" -or $Target -eq "Windows") {
        if (-not (Test-Path (Join-Path $buildsRoot "ServerWindows\RaidDemoServer.exe"))) {
            throw "缺少 Windows 服务器构建产物：Builds\ServerWindows\RaidDemoServer.exe。请先在编辑器菜单执行「RaidDemo/M9/构建专用服务器（Windows）」。"
        }
    }

    if ($Target -eq "Both" -or $Target -eq "Linux") {
        if (-not (Test-Path (Join-Path $buildsRoot "ServerLinux\RaidDemoServer.x86_64"))) {
            throw "缺少 Linux 服务器构建产物：Builds\ServerLinux\RaidDemoServer.x86_64。请先在编辑器菜单执行「RaidDemo/M9/构建专用服务器（Linux）」。"
        }
    }

    Publish-Panel

    if ($Target -eq "Both" -or $Target -eq "Windows") {
        New-WindowsPackage
    }

    if ($Target -eq "Both" -or $Target -eq "Linux") {
        New-LinuxPackage
    }

    Write-Host "打包完成。" -ForegroundColor Green
}

<#
读取工程 version（ProjectSettings.asset 里的 bundleVersion）。
这样做是为了让"服务器包版本"与"游戏本体版本"永远一致：
两者不一致时，客户端连服务器会在版本握手里被拒绝，而报错现象与版本号毫无关系。
#>
function Read-BundleVersion {
    $settingsPath = Join-Path $repoRoot "ProjectSettings\ProjectSettings.asset"
    if (-not (Test-Path $settingsPath)) {
        throw "找不到 ProjectSettings.asset，无法读取版本号；请用 -Version 显式指定。"
    }

    $match = Select-String -Path $settingsPath -Pattern "^\s*bundleVersion:\s*(.+)$" | Select-Object -First 1
    if (-not $match) {
        throw "ProjectSettings.asset 里没有 bundleVersion，请用 -Version 显式指定。"
    }

    return $match.Matches[0].Groups[1].Value.Trim()
}

<#
出面板的单文件发布产物。
用 dotnet publish 而不是 build：只有 publish 才带自包含运行时，
目标机器（玩家电脑）没装 .NET 也能跑——这正是"双击就开服"的前提。
#>
function Publish-Panel {
    Write-Host "→ 发布服务器面板（自包含单文件）…"
    & dotnet publish $panelProject -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "面板发布失败，退出码 $LASTEXITCODE。"
    }

    if (-not (Test-Path $panelPublish)) {
        throw "面板发布产物不存在：$panelPublish"
    }
}

<#
拼装 Windows 包：Unity 服务器产物 + 面板 + 默认配置 + 说明。
#>
function New-WindowsPackage {
    $packageName = "ServerPackage-Windows-$Version"
    $package = New-CleanDirectory $packageName

    Write-Host "→ 组装 $packageName"
    Copy-Item (Join-Path $buildsRoot "ServerWindows\*") -Destination $package -Recurse -Force
    Copy-Item $panelPublish -Destination $package -Force

    Write-DefaultConfig $package
    Write-UsageFile (Join-Path $package "使用说明.txt") "Windows"

    Show-PackageSummary $package

    if ($Zip) {
        $archive = Join-Path $buildsRoot "$packageName.zip"
        if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
        Write-Host "→ 压缩 $archive"
        # 连包目录本身一起压：解压后得到一层 ServerPackage-Windows-<版本>/，
        # 而不是把几十个文件直接倒在玩家的下载目录里（Linux 侧 tar 同样保留这一层）。
        Compress-Archive -Path $package -DestinationPath $archive
    }
}

<#
拼装 Linux 包：Unity 服务器产物 + 启动脚本 + 默认配置 + 说明。
Linux 侧刻意不做图形面板：云主机是无头环境，桌面程序跑不起来；
统一的是**配置格式与启动方式**，而不是界面。
#>
function New-LinuxPackage {
    $packageName = "ServerPackage-Linux-$Version"
    $package = New-CleanDirectory $packageName

    Write-Host "→ 组装 $packageName"
    Copy-Item (Join-Path $buildsRoot "ServerLinux\*") -Destination $package -Recurse -Force
    # Linux 侧的管理脚本用 deploy/server.sh：它本来就负责 start/stop/restart/status/logs
    # 与 glibc chroot 兼容，云主机也是用它。包里再放第二份"简化版启动脚本"，
    # 等于给同一个问题准备两个答案——那种重复迟早会分叉成"本地能起、云上起不来"。
    Copy-Item (Join-Path $repoRoot "deploy\server.sh") -Destination $package -Force

    Write-DefaultConfig $package
    Write-UsageFile (Join-Path $package "使用说明.txt") "Linux"

    Show-PackageSummary $package

    if ($Zip) {
        $archive = Join-Path $buildsRoot "$packageName.tar.gz"
        if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
        Write-Host "→ 压缩 $archive"
        # 用 tar 而不是 Compress-Archive：Linux 侧解压后必须保留执行位，
        # zip 在多数 Linux 解压工具里会丢掉权限，tar.gz 不会。
        & tar -czf $archive -C $buildsRoot $packageName
        if ($LASTEXITCODE -ne 0) {
            throw "tar 打包失败，退出码 $LASTEXITCODE。"
        }
    }
}

<#
建立一个干净的目标目录。
先删后建是为了让"重复打包"的结果可预期：旧版本残留的文件混进新包，
是最容易在验收时骗过自己的故障（例如旧的面板 exe 被当成新的）。
#>
function New-CleanDirectory {
    param([string]$Name)

    $target = Join-Path $buildsRoot $Name

    # 安全闸：只允许删除 Builds 目录的直接子目录，避免变量写错时误删别处。
    $parent = Split-Path -Parent $target
    if ($parent -ne $buildsRoot) {
        throw "拒绝操作：$target 不在 Builds 目录下。"
    }

    if (Test-Path $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    New-Item -ItemType Directory -Path $target | Out-Null
    return $target
}

<#
让面板自己写出默认配置。
不在这里硬写一份 JSON：默认值的权威定义在 ServerConfigDocument 里，
脚本抄一遍就等于制造第二个真相，改一处忘一处的那天没人会发现。
#>
function Write-DefaultConfig {
    param([string]$Package)

    Write-Host "→ 生成默认配置 server.config.json"
    & $panelPublish --write-default-config $Package
    if ($LASTEXITCODE -ne 0) {
        throw "生成默认配置失败，退出码 $LASTEXITCODE。"
    }
}

<#
写使用说明。
说明面向"拿到压缩包的人"，因此只讲三件事：怎么启动、怎么改配置、日志与状态页在哪；
不写构建与开发细节——那些在仓库文档里。
#>
function Write-UsageFile {
    param([string]$Path, [string]$Platform)

    $startText = if ($Platform -eq "Windows") {
        @"
1) 双击 RaidDemo.ServerHost.exe 打开服务器面板；
2) 面板里确认端口 / 房间名（默认 7777 / 默认房间），点「启动服务器」；
3) 点「复制连接信息」，把地址发给要一起玩的人；
4) 他们打开客户端（启动器）→ 主菜单 → 联机 → 输入该地址即可。
"@
    }
    else {
        @"
1) 首次使用先赋执行权限：chmod +x RaidDemoServer.x86_64 server.sh
2) 启动：./server.sh start     停止：./server.sh stop     状态：./server.sh status
   日志：./server.sh logs 80   重启：./server.sh restart
3) 改端口 / 房间名：编辑同目录的 server.config.json 后 ./server.sh restart
   （配置文件优先于脚本里的环境变量；临时覆盖可用 RAIDDEMO_EXTRA_ARGS）
4) 日志文件：Logs/server.log   状态页：http://<本机IP>:8080/
"@
    }

    $content = @"
RaidDemo 专用服务器（$Platform）
=================================

启动
$startText
配置
  server.config.json 与服务器程序放在同一目录，字段含义：
    port            监听端口（1~65535）
    room            房间名（最长 24 字）
    saveDir         存档目录，必须是相对路径（例如 server_saves）
    map             开局加载的地图场景（默认 GreyboxRaid）
    logLevel        verbose / info / warning / error
    raidDuration    战局时长上限（秒，0 表示不判定超时）
    autoStart       房间成立后自动开局的等待秒数（0 表示手动开局）
    dashboardPort   只读状态页端口（0 表示关闭）
    discoveryPort   局域网自动发现端口（0 表示关闭）
    grace           掉线宽限秒数
    watchdog        传输层自愈判定秒数（0 表示关闭）

  优先级：命令行参数 > server.config.json > 内置默认值。

日志与状态页
  日志：Logs/server.log（Windows 面板里可直接点「打开日志目录」）
  状态页：http://127.0.0.1:<dashboardPort>/ 可看房间、在线玩家与最近日志

端口与网络
  默认 UDP 7777（游戏流量）、TCP 8080（状态页）。
  公网开服需要在防火墙 / 安全组放行这两个端口。
"@

    Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
}

<# 打印包的大小与顶层文件，方便在日志里一眼看出"这次到底打了什么"。 #>
function Show-PackageSummary {
    param([string]$Package)

    $size = (Get-ChildItem $Package -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("  {0}：{1:F1} MB" -f (Split-Path -Leaf $Package), $size) -ForegroundColor DarkGray
}

Invoke-Packaging
