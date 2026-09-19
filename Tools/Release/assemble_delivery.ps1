<#
.SYNOPSIS
    组装"看一眼就能找到"的交付文件夹：客户端 / 服务端 / 启动器 各占一个子目录。

.DESCRIPTION
    仓库里的产物分散在三处（Builds/Client、Builds/ServerPackage-*、Tools/Launcher 的发布输出），
    对开发是合理的（各管各的构建），对"我要把这个 demo 拿给谁看"就不合理了——
    每次都要先回忆哪个目录装的是哪一份。

    本脚本把这些产物归拢成：

      Builds/交付/RaidDemo-<版本>/
        ├─ 说明.txt
        ├─ 客户端/            解压即玩的本体（单机直接双击 RaidDemo.exe）
        ├─ 服务端/
        │    ├─ Windows一键开服/  RaidDemo.ServerHost.exe + RaidDemoServer.exe + 配置
        │    └─ Linux云主机/      RaidDemoServer.x86_64 + server.sh + 配置
        └─ 启动器/            RaidDemo.Launcher.exe（自包含单文件）

    它只做复制，不重新构建客户端/服务端（那两件事分别由 Unity 菜单与
    Tools/ServerHost/pack_server_package.ps1 负责）；启动器则会现场 dotnet publish，
    因为"发布"这件事没有别的入口。

.EXAMPLE
    pwsh -File Tools/Release/assemble_delivery.ps1
    pwsh -File Tools/Release/assemble_delivery.ps1 -Zip
#>
[CmdletBinding()]
param(
    # 版本号；缺省从 ProjectSettings 的 bundleVersion 读取。
    [string]$Version,

    # 客户端构建目录；缺省 Builds/Client/<版本>。
    [string]$ClientDirectory,

    # 是否额外把整个交付目录打成一个 zip。
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$buildsRoot = Join-Path $repoRoot "Builds"
$deliveryRoot = Join-Path $buildsRoot "交付"
$launcherProject = Join-Path $repoRoot "Tools\Launcher\RaidDemo.Launcher.csproj"
$launcherPublish = Join-Path $repoRoot "Tools\Launcher\bin\Release\net8.0-windows\win-x64\publish"

function Invoke-Assembly {
    if (-not $Version) {
        $script:Version = Read-BundleVersion
    }

    if (-not $ClientDirectory) {
        $script:ClientDirectory = Join-Path $buildsRoot "Client\$Version"
    }

    Write-Host "组装交付目录：版本 $Version" -ForegroundColor Cyan

    Assert-Inputs

    $target = New-CleanDirectory (Join-Path $deliveryRoot "RaidDemo-$Version")

    Copy-Client $target
    Copy-ServerPackages $target
    Publish-Launcher $target
    Write-Readme $target

    Write-Host ""
    Write-Host "交付目录：$target" -ForegroundColor Green
    Show-Tree $target

    if ($Zip) {
        $archive = Join-Path $deliveryRoot "RaidDemo-$Version.zip"
        if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
        Write-Host "→ 压缩 $archive（体积大，需要几分钟）"
        Compress-Archive -Path $target -DestinationPath $archive
    }
}

<# 读取工程 version（与游戏本体、服务器包保持同一个版本号）。 #>
function Read-BundleVersion {
    $settingsPath = Join-Path $repoRoot "ProjectSettings\ProjectSettings.asset"
    $match = Select-String -Path $settingsPath -Pattern "^\s*bundleVersion:\s*(.+)$" | Select-Object -First 1
    if (-not $match) {
        throw "ProjectSettings.asset 里没有 bundleVersion，请用 -Version 显式指定。"
    }

    return $match.Matches[0].Groups[1].Value.Trim()
}

<# 三份输入缺一不可；缺了就说清楚"该先做哪一步"，而不是复制到一半失败。 #>
function Assert-Inputs {
    if (-not (Test-Path (Join-Path $ClientDirectory "RaidDemo.exe"))) {
        throw "缺少客户端构建：$ClientDirectory\RaidDemo.exe。请先执行编辑器菜单「RaidDemo/M10/① 构建客户端（Windows）并生成更新清单」。"
    }

    foreach ($platform in @("Windows", "Linux")) {
        $package = Join-Path $buildsRoot "ServerPackage-$platform-$Version"
        if (-not (Test-Path $package)) {
            throw "缺少服务器包：$package。请先执行 pwsh -File Tools/ServerHost/pack_server_package.ps1（它会出 Windows 与 Linux 两份）。"
        }
    }

    $freeGb = (Get-PSDrive -Name (Split-Path -Qualifier $buildsRoot).TrimEnd(':')).Free / 1GB
    if ($freeGb -lt 6) {
        throw ("可用磁盘空间不足（{0:F1} GB）；交付目录需要复制约 4~5 GB（客户端 + 两份服务器包 + 启动器）。" -f $freeGb)
    }
}

<# 建立一个干净的目标目录（重复执行的结果必须可预期：不留上一次的残留文件）。 #>
function New-CleanDirectory {
    param([string]$Path)

    # 安全闸：只允许删除"交付"目录下的直接子目录，避免变量写错时误删别处。
    $parent = Split-Path -Parent $Path
    if ($parent -ne $deliveryRoot) {
        throw "拒绝操作：$Path 不在 $deliveryRoot 下。"
    }

    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path | Out-Null
    return $Path
}

<# 客户端：解压即玩的本体。 #>
function Copy-Client {
    param([string]$Target)

    $directory = Join-Path $Target "客户端"
    Write-Host "→ 客户端：$ClientDirectory"
    # 先建目标目录：Copy-Item 带通配符 + -Recurse 时，目标不存在会报
    # "Container cannot be copied onto existing leaf item"（PowerShell 的已知行为）。
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item (Join-Path $ClientDirectory "*") -Destination $directory -Recurse -Force

    # 把资源层（Addressables 内容包）一并放进交付客户端。
    #
    # 为什么必须这一步：构建客户端时"资源"被拆成独立的内容层（content/ 目录，
    # 由启动器或热更从更新源下载到本地缓存）。交付目录里如果只有本体，
    # 一旦遇到"下载了新内容、本次进图却先用本体 catalog 解析地址"的时刻，
    # Addressables 会去 StreamingAssets/aa/<平台>/ 找内容包——那里只有两个内建包，
    # 于是报 Invalid path in AssetBundleProvider，进图直接失败（M12 云上 4 人局实测）。
    # 放进内容层之后：解压即玩（离线可用），更新源可用时仍照常热更。
    $contentSource = Join-Path $buildsRoot "Update\$Version\content"
    $contentTarget = Join-Path $directory "RaidDemo_Data\StreamingAssets\aa\StandaloneWindows64"
    if (Test-Path $contentSource) {
        Write-Host "-> 客户端资源层：$contentSource"
        New-Item -ItemType Directory -Path $contentTarget -Force | Out-Null
        Copy-Item (Join-Path $contentSource "*") -Destination $contentTarget -Recurse -Force
    }
}

<# 服务端：Windows 一键开服包 + Linux 云主机包，各占一个子目录。 #>
function Copy-ServerPackages {
    param([string]$Target)

    $serverRoot = Join-Path $Target "服务端"
    New-Item -ItemType Directory -Path $serverRoot | Out-Null

    Write-Host "→ 服务端（Windows 一键开服）"
    $windowsPackage = Join-Path $serverRoot "Windows一键开服"
    New-Item -ItemType Directory -Path $windowsPackage -Force | Out-Null
    Copy-Item (Join-Path $buildsRoot "ServerPackage-Windows-$Version\*") `
        -Destination $windowsPackage -Recurse -Force

    Write-Host "→ 服务端（Linux 云主机）"
    $linuxPackage = Join-Path $serverRoot "Linux云主机"
    New-Item -ItemType Directory -Path $linuxPackage -Force | Out-Null
    Copy-Item (Join-Path $buildsRoot "ServerPackage-Linux-$Version\*") `
        -Destination $linuxPackage -Recurse -Force
}

<#
启动器：现场发布（自包含单文件），否则目标机器要有 .NET 运行时。
本机私有配置（launcher.config.local.json，含真实更新源地址）若存在就一并带上——
它在仓库里被忽略，但交付目录是给人用的，缺了它启动器只会看到占位地址。
#>
function Publish-Launcher {
    param([string]$Target)

    Write-Host "→ 启动器：dotnet publish（自包含单文件）"
    & dotnet publish $launcherProject -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "启动器发布失败，退出码 $LASTEXITCODE。"
    }

    $directory = Join-Path $Target "启动器"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item (Join-Path $launcherPublish "RaidDemo.Launcher.exe") -Destination $directory -Force

    # 配置模板优先取发布目录；增量发布偶尔不会重新复制内容文件（发布目录被清理过的情形），
    # 这时回退到源目录——交付目录缺这份模板，启动器只剩占位地址，等于分发了一份坏包。
    $configTemplate = Join-Path $launcherPublish "launcher.config.json"
    if (-not (Test-Path $configTemplate)) {
        $configTemplate = Join-Path (Split-Path $launcherProject) "launcher.config.json"
    }

    Copy-Item $configTemplate -Destination $directory -Force

    $localConfig = Join-Path $launcherPublish "launcher.config.local.json"
    if (Test-Path $localConfig) {
        Copy-Item $localConfig -Destination $directory -Force
        Write-Host "  （已带上本机私有配置 launcher.config.local.json）"
    }
}

<# 顶层说明：三分钟看懂哪份是给谁用的。 #>
function Write-Readme {
    param([string]$Target)

    $content = @"
RaidDemo $Version 交付说明
==========================

本目录把三份东西放在一起，各占一个子目录：

客户端/          游戏本体（解压即玩）
  双击 RaidDemo.exe → 单机直接开玩；
  要走"启动器更新"那条路，请用下面的启动器启动（它会校验并更新本体）。

服务端/
  Windows一键开服/   双击 RaidDemo.ServerHost.exe → 面板里点「启动服务器」；
                     端口、房间名等在同目录的 server.config.json（面板里也能改）。
  Linux云主机/       ./server.sh start（详见包内 使用说明.txt）。

启动器/           RaidDemo.Launcher.exe（自包含单文件，目标机器无需装 .NET）
  双击 → 选更新源 → 检查更新 → 更新并启动。
  更新源地址在 launcher.config.json 里；本机更新源需要先起
  Tools/UpdateSource/RaidDemo.UpdateSource.exe（见 Tools/UpdateSource/README.md）。

怎么和别人一起玩
  1) 开服的人在服务端面板点「启动服务器」，再点「复制连接信息」；
  2) 其他人双击启动器进游戏 → 主菜单「联机」→ 填服务器地址与昵称 → 连接；
  3) 房主创建房间（可设 4 位房间密码），其他人加入；房间成立后从安全屋的出口选图出击。

版本：$Version（与 ProjectSettings 的 bundleVersion 一致）
构建日期：$(Get-Date -Format "yyyy-MM-dd")
"@

    Set-Content -LiteralPath (Join-Path $Target "说明.txt") -Value $content -Encoding UTF8
}

<# 打印交付目录的顶层结构（只列一层，避免刷屏）。 #>
function Show-Tree {
    param([string]$Target)

    Get-ChildItem $Target | ForEach-Object {
        if ($_.PSIsContainer) {
            Write-Host ("  {0}/" -f $_.Name) -ForegroundColor DarkGray
            Get-ChildItem $_.FullName | Select-Object -First 4 | ForEach-Object {
                Write-Host ("      {0}" -f $_.Name) -ForegroundColor DarkGray
            }
        }
        else {
            Write-Host ("  {0}" -f $_.Name) -ForegroundColor DarkGray
        }
    }
}

Invoke-Assembly
