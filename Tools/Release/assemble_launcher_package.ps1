<#
.SYNOPSIS
    组装"启动器版"交付：只有启动器 + 说明；游戏本体与资源全部由更新源分发。

.DESCRIPTION
    当前交付口径（2026-09-18 起，负责人确认）：只出启动器版。
    本体与资源通过更新源发布（upload → publish → verify），玩家侧只拿一个启动器
    （自包含单文件），由它完成下载、增量更新与启动——不再为每次发布重组
    "客户端 + 双端服务端 + 启动器"的完整交付目录（那个仍由 assemble_delivery.ps1 负责，
    只在需要离线包或重出服务端时使用）。

    产物固定落在 Builds/交付/RaidDemo-启动器版/，重复执行可直接替换。

.EXAMPLE
    pwsh -File Tools/Release/assemble_launcher_package.ps1
    pwsh -File Tools/Release/assemble_launcher_package.ps1 -Zip
#>
[CmdletBinding()]
param(
    # 是否额外把整个目录压成一个 zip。
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$deliveryRoot = Join-Path $repoRoot "Builds\交付"
$launcherProject = Join-Path $repoRoot "Tools\Launcher\RaidDemo.Launcher.csproj"
$launcherDirectory = Split-Path -Parent $launcherProject
$launcherPublish = Join-Path $launcherDirectory "bin\Release\net8.0-windows\win-x64\publish"
$target = Join-Path $deliveryRoot "RaidDemo-启动器版"

<# 读取工程 version（与游戏本体保持一致）。 #>
function Read-BundleVersion {
    $settingsPath = Join-Path $repoRoot "ProjectSettings\ProjectSettings.asset"
    $match = Select-String -Path $settingsPath -Pattern "^\s*bundleVersion:\s*(.+)$" | Select-Object -First 1
    if (-not $match) {
        throw "ProjectSettings.asset 里没有 bundleVersion。"
    }

    return $match.Matches[0].Groups[1].Value.Trim()
}

<# 使用说明：启动器版只讲"怎么拿到游戏"，不讲服务端部署。 #>
function Write-Readme {
    param([string]$Target, [string]$Version)

    $content = @"
RaidDemo 启动器版 $Version
==========================

这个文件夹里只有启动器——游戏本体与资源不随包分发，
双击 RaidDemo.Launcher.exe → 选更新源 → 检查更新 → 更新并启动。
启动器会自动完成下载、校验与增量更新（断点续传、失败回滚）。

更新源
  默认使用"云主机"源（地址见 launcher.config.local.json）；
  本机源需要先在开发机起 Tools/UpdateSource 的 RaidDemo.UpdateSource.exe。

怎么玩
  单机：启动器 → 开始游戏 → 继续 / 新游戏。
  联机：主菜单「联机」→ 填服务器地址与昵称 → 建房 / 加入；
        房主在共享安全屋的出口选图出击，全员回屋后才能开下一局。

版本：$Version（本体版本以更新源发布清单为准；发布批号见更新源面板）
构建日期：$(Get-Date -Format "yyyy-MM-dd")
"@

    Set-Content -LiteralPath (Join-Path $Target "说明.txt") -Value $content -Encoding UTF8
}

function Invoke-Assembly {
    # 版本号只用于说明文件；实际以更新源发布的清单为准（启动器按文件哈希比对更新）。
    $version = Read-BundleVersion

    Write-Host "组装启动器版：本体版本 $version" -ForegroundColor Cyan

    # 安全闸：目标必须是"交付"目录下的直接子目录，避免变量写错时误删别处。
    if ((Split-Path -Parent $target) -ne $deliveryRoot) {
        throw "拒绝操作：$target 不在 $deliveryRoot 下。"
    }

    if (Test-Path $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    New-Item -ItemType Directory -Path $target | Out-Null

    Write-Host "→ 发布启动器（自包含单文件）"
    & dotnet publish $launcherProject -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "启动器发布失败，退出码 $LASTEXITCODE。"
    }

    Copy-Item (Join-Path $launcherPublish "RaidDemo.Launcher.exe") -Destination $target -Force

    # 配置模板：发布目录优先，缺了回退到源目录（发布目录被清理过的情形）。
    $configTemplate = Join-Path $launcherPublish "launcher.config.json"
    if (-not (Test-Path $configTemplate)) {
        $configTemplate = Join-Path $launcherDirectory "launcher.config.json"
    }
    Copy-Item $configTemplate -Destination $target -Force

    # 本机私有配置（含真实更新源地址）在仓库里被忽略，但交付目录是给人用的，必须带上；
    # 缺了它启动器只剩占位地址，等于分发了一份坏包。
    $localConfig = Join-Path $launcherPublish "launcher.config.local.json"
    if (-not (Test-Path $localConfig)) {
        $localConfig = Join-Path $launcherDirectory "launcher.config.local.json"
    }
    if (Test-Path $localConfig) {
        Copy-Item $localConfig -Destination $target -Force
        Write-Host "  （已带上私有配置 launcher.config.local.json）"
    }
    else {
        Write-Warning "缺少 launcher.config.local.json：启动器里的云主机地址会是占位符。"
    }

    Write-Readme -Target $target -Version $version

    Write-Host ""
    Write-Host "启动器版目录：$target" -ForegroundColor Green
    Get-ChildItem $target | ForEach-Object { Write-Host ("  {0}" -f $_.Name) -ForegroundColor DarkGray }

    if ($Zip) {
        $archive = Join-Path $deliveryRoot "RaidDemo-启动器版.zip"
        if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
        Write-Host "→ 压缩 $archive"
        Compress-Archive -Path (Join-Path $target "*") -DestinationPath $archive
        Write-Host "启动器版压缩包：$archive" -ForegroundColor Green
    }
}

Invoke-Assembly
