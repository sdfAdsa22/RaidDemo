<#
.SYNOPSIS
    验收：服务器配置文件（server.config.json）被正确读取，且命令行能覆盖它。

.DESCRIPTION
    覆盖四件事（每件都对应一种失败方式）：
      A 配置文件生效：端口 / 房间名都来自文件，而不是内置默认；
      B 命令行优先：同一个配置文件下传 -port，命令行赢；
      C 日志留痕：启动日志里能看到"已应用服务器配置文件"（排障入口）；
      D 坏配置要吵：端口写成 0 时服务器拒绝启动，报错指向配置文件
        （而不是静默按默认值跑——那会让"我改了端口却没生效"变成悬案）。

    前置：先出 Windows 专用服务器包（编辑器菜单 RaidDemo/M9/构建专用服务器（Windows））。
    产物与日志落在 Builds/ServerConfigCheck/（已忽略，不进仓库）。

.EXAMPLE
    pwsh -File Tools/ServerHost/verify/server_config_check.ps1
#>
[CmdletBinding()]
param(
    # 服务器可执行文件；缺省用 Builds/ServerWindows/RaidDemoServer.exe。
    [string]$ServerExe,

    # 验收用的端口基准（避开日常调试端口）。
    [int]$BasePort = 47801
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
if (-not $ServerExe) {
    $ServerExe = Join-Path $repoRoot "Builds\ServerWindows\RaidDemoServer.exe"
}

$work = Join-Path $repoRoot "Builds\ServerConfigCheck"

# 脚本主体写成函数、最后调用：PowerShell 是顺序执行的语言，
# 顶层直接调用"后面才定义"的函数会报"无法识别的命令"。
function Invoke-Checks {
    if (-not (Test-Path $ServerExe)) {
        Write-Host "[跳过] 找不到服务器可执行文件：$ServerExe" -ForegroundColor Yellow
        Write-Host "       请先执行编辑器菜单：RaidDemo/M9/构建专用服务器（Windows）。"
        exit 2
    }

    New-Item -ItemType Directory -Force -Path $work | Out-Null

    Write-Host "== A / B / C：配置文件生效 + 命令行覆盖 ==" -ForegroundColor Cyan

    $configPath = Join-Path $work "server.config.json"
    @{
        port          = $BasePort
        room          = "配置验收房间"
        saveDir       = "cfg_check_saves"
        logLevel      = "info"
        map           = "GreyboxRaid"
        raidDuration  = 120
        autoStart     = 0
        dashboardPort = $BasePort + 1
        discoveryPort = 0
        grace         = 30
        watchdog      = 0
    } | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8

    $logA = Join-Path $work "server-config.log"
    $processA = Start-Server $logA @("-server", "-config", $configPath)
    $statusA = Wait-Dashboard ($BasePort + 1)
    Stop-Server $processA

    Check "A1 配置文件的端口生效" ($null -ne $statusA -and $statusA.Port -eq $BasePort) "状态页报告端口 $($statusA.Port)"
    Check "A2 配置文件的房间名生效" ((Get-LogText $logA) -match "配置验收房间") "见 server-config.log"
    Check "A3 启动日志留痕" ((Get-LogText $logA) -match "已应用服务器配置文件") "见 server-config.log"

    $logB = Join-Path $work "server-override.log"
    $overridePort = $BasePort + 10
    $processB = Start-Server $logB @("-server", "-config", $configPath, "-port", "$overridePort", "-dashboardPort", "$($BasePort + 11)")
    $statusB = Wait-Dashboard ($BasePort + 11)
    Stop-Server $processB

    Check "B1 命令行端口覆盖配置文件" ($null -ne $statusB -and $statusB.Port -eq $overridePort) "状态页报告端口 $($statusB.Port)"
    Check "B2 未被覆盖的项仍取配置" ((Get-LogText $logB) -match "配置验收房间") "见 server-override.log"

    Write-Host ""
    Write-Host "== D：坏配置必须拒绝启动 ==" -ForegroundColor Cyan

    $badConfigPath = Join-Path $work "server-bad.config.json"
    @{ port = 0 } | ConvertTo-Json | Set-Content -LiteralPath $badConfigPath -Encoding UTF8

    $logD = Join-Path $work "server-badconfig.log"
    $processD = Start-Server $logD @("-server", "-config", $badConfigPath)
    $exited = $processD.WaitForExit(30000)
    if (-not $exited) {
        Stop-Server $processD
    }

    $textD = Get-LogText $logD
    Check "D1 坏配置时进程退出" $exited "30 秒内退出=$exited"
    Check "D2 报错指向配置文件" ($textD -match "配置文件" -and $textD -match "端口") "见 server-badconfig.log"

    Write-Host ""
    if ($script:failed -eq 0) {
        Write-Host "全部通过（7 / 7）。日志目录：Builds/ServerConfigCheck" -ForegroundColor Green
        exit 0
    }

    Write-Host "失败 $($script:failed) 条。日志目录：Builds/ServerConfigCheck" -ForegroundColor Red
    exit 1
}

<#
启动一个隐藏窗口的服务器进程。

参数里含空格的项手动加引号：Start-Process 的 -ArgumentList 是"把数组用空格拼成一行"，
不会替你转义——路径 `D:\unity game\demo\...` 一旦不引号，Unity 会把 `game\demo\...` 当成另一个参数，
表现为"日志文件跑到了奇怪的位置"或"配置没被读到"，且不报错。
#>
function Start-Server {
    param([string]$LogPath, [string[]]$ExtraArguments)

    if (Test-Path $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }

    $arguments = @($ExtraArguments) + @("-batchmode", "-nographics", "-logFile", $LogPath)
    $quoted = $arguments | ForEach-Object { if ($_ -match "\s") { '"' + $_ + '"' } else { $_ } }

    return Start-Process -FilePath $ServerExe -WorkingDirectory (Split-Path $ServerExe -Parent) `
        -WindowStyle Hidden -PassThru -ArgumentList $quoted
}

<# 结束进程（验收脚本用强杀即可；面板里才是"先温和后强杀"的语义）。 #>
function Stop-Server {
    param($Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

<# 轮询状态页，直到拿到 JSON 快照或超时（默认 30 秒）。 #>
function Wait-Dashboard {
    param([int]$Port, [int]$TimeoutSeconds = 30)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/status.json" -TimeoutSec 3 -UseBasicParsing
            return $response.Content | ConvertFrom-Json
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    return $null
}

<# 读日志文本；文件不存在时返回空串（"日志没生成"由判据去报，不在这里抛异常）。 #>
function Get-LogText {
    param([string]$LogPath)
    if (Test-Path $LogPath) {
        return Get-Content $LogPath -Raw
    }

    return ""
}

<# 打印一条判据并累计失败数。 #>
function Check {
    param([string]$Label, [bool]$Passed, [string]$Detail)

    if ($Passed) {
        Write-Host "[PASS] $Label —— $Detail" -ForegroundColor Green
        return
    }

    Write-Host "[FAIL] $Label —— $Detail" -ForegroundColor Red
    $script:failed++
}

$script:failed = 0
Invoke-Checks
