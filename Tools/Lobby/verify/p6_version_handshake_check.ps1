# =============================================================================
# 版本握手验收（M10 批次 6 / Docs/03_模块设计/11_热更新与分发.md 第 13.1 节）
# =============================================================================
# 它在真实进程上验证三件事：
#   A 同版本客户端能正常进房（握手不误伤）
#   B 声称旧版本的客户端被拒，且提示里同时出现两个版本号
#   C 服务器日志留下了拒绝原因（含两端标识，便于排查）
#
# 为什么用 -buildid 参数而不是准备两个不同版本的构建：握手的判定依据是"客户端上报的标
# 识"，而标识本来就允许由启动器传入。用启动参数伪造一个旧版本，走的仍然是同一条判定路
# 径，但不需要为此每天重打一个包。真实双版本构建的验收记录见模块文档第 13.2 节。
#
# 前置：先执行菜单 RaidDemo/M10/① 构建客户端（Windows）并生成更新清单。
#
# 用法：pwsh -NoProfile -File Tools/Lobby/verify/p6_version_handshake_check.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path

$port = 47788
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Name, [bool]$Pass, [string]$Detail) {
    $script:results.Add([pscustomobject]@{ Name = $Name; Pass = $Pass; Detail = $Detail })
}

function Find-ClientExecutable {
    $clientRoot = Join-Path $projectRoot 'Builds\Client'
    if (-not (Test-Path $clientRoot)) {
        return $null
    }

    $newest = Get-ChildItem $clientRoot -Directory |
        Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } -Descending |
        Select-Object -First 1
    if ($null -eq $newest) {
        return $null
    }

    $exe = Join-Path $newest.FullName 'RaidDemo.exe'
    if (Test-Path $exe) {
        return $exe
    }

    return $null
}

function Wait-LogPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            $text = Get-Content $Path -Raw -ErrorAction SilentlyContinue
            if ($text -and $text -match $Pattern) {
                return $text
            }
        }

        Start-Sleep -Milliseconds 500
    }

    if (Test-Path $Path) {
        return (Get-Content $Path -Raw -ErrorAction SilentlyContinue)
    }

    return $null
}

function Start-Game([string]$Exe, [string[]]$Arguments, [string]$WorkingDirectory) {
    return Start-Process -FilePath $Exe -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru
}

$exe = Find-ClientExecutable
if ($null -eq $exe) {
    Write-Host '找不到客户端产物（Builds/Client/<版本>/RaidDemo.exe）。请先执行菜单 ①。' -ForegroundColor Red
    exit 1
}

$installRoot = Split-Path $exe -Parent
$workRoot = Join-Path $projectRoot 'Builds\HandshakeCheck'
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

$serverLog = Join-Path $workRoot 'server.log'
$goodClientLog = Join-Path $workRoot 'client-same-version.log'
$badClientLog = Join-Path $workRoot 'client-old-version.log'
foreach ($log in @($serverLog, $goodClientLog, $badClientLog)) {
    if (Test-Path $log) {
        Remove-Item -LiteralPath $log -Force
    }
}

Write-Host "客户端产物：$exe"

$serverProcess = $null
$goodProcess = $null
$badProcess = $null

try {
    $serverProcess = Start-Game $exe @(
        '-server',
        '-port', "$port",
        '-dashboardPort', '0',
        '-discoveryPort', '0',
        '-saveDir', 'handshake_saves',
        '-batchmode', '-nographics',
        '-logFile', "`"$serverLog`""
    ) $installRoot

    $serverText = Wait-LogPattern $serverLog '大厅已就绪' 45
    $serverReady = $serverText -and $serverText -match '大厅已就绪'
    Add-Result 'A0 服务器启动并进入监听' $serverReady $(if ($serverReady) { "端口 $port" } else { '超时未见「大厅已就绪」' })
    if (-not $serverReady) {
        throw '服务器未就绪，后续判据无法执行。'
    }

    $goodProcess = Start-Game $exe @(
        '-connect', "127.0.0.1:$port",
        '-nickname', '握手甲',
        '-passphrase', '123456',
        '-autoroom',
        '-batchmode', '-nographics',
        '-logFile', "`"$goodClientLog`""
    ) $installRoot

    # 注意别用「房间「」当等待条件：服务器启动日志里就有一句「默认房间名「默认房间」」，
    # 它会立刻命中，于是判据在房间真正建立之前就拿到快照，出现"其实成功却判失败"的假阴性。
    $serverText = Wait-LogPattern $serverLog '已创建：房主|加入房间（' 30
    $joined = $serverText -and $serverText -match '已创建：房主|加入房间（'
    Add-Result 'A1 同版本客户端进房成功' $joined $(if ($joined) { '服务器已建立房间并接纳成员' } else { '30 秒内未见房间建立' })

    $badProcess = Start-Game $exe @(
        '-connect', "127.0.0.1:$port",
        '-nickname', '握手乙',
        '-passphrase', '123456',
        '-autoroom',
        '-buildid', '0.0.0+old',
        '-batchmode', '-nographics',
        '-logFile', "`"$badClientLog`""
    ) $installRoot

    $badText = Wait-LogPattern $badClientLog '不一致' 40
    $rejected = $badText -and $badText -match '与服务器.*不一致'
    Add-Result 'B1 旧版本客户端被拒' $rejected $(if ($rejected) { '客户端拿到版本不一致提示' } else { '客户端日志里没有拒绝提示' })

    $hasBothVersions = $badText -and $badText -match '0\.0\.0' -and $badText -match '与服务器\s*\d+\.\d+\.\d+'
    Add-Result 'B2 提示里含两个版本号' $hasBothVersions $(if ($hasBothVersions) { '提示同时给出两端版本' } else { '提示缺少版本号' })

    $serverText = Wait-LogPattern $serverLog '被版本握手拒绝' 15
    $loggedOnServer = $serverText -and $serverText -match '被版本握手拒绝'
    Add-Result 'C1 服务器日志记录拒绝原因' $loggedOnServer $(if ($loggedOnServer) { '服务器日志含两端标识' } else { '服务器日志没有拒绝记录' })

    # 被拒玩家只该出现在"拒绝"那一条日志里；一旦出现在房间记录里，说明拦截发生在写入之后。
    $ghost = $serverText -match '握手乙」已创建|握手乙」加入房间|握手乙.*（[0-9]/4）'
    Add-Result 'C2 被拒客户端未进入名册' (-not $ghost) $(if ($ghost) { '服务器上出现了被拒玩家的房间记录' } else { '名册未被污染' })
}
finally {
    foreach ($process in @($badProcess, $goodProcess, $serverProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
$passed = ($results | Where-Object { $_.Pass }).Count
foreach ($result in $results) {
    $mark = if ($result.Pass) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1} —— {2}" -f $mark, $result.Name, $result.Detail)
}

Write-Host ''
Write-Host ("{0}/{1} PASS ｜ 日志目录：Builds/HandshakeCheck" -f $passed, $results.Count)
exit $(if ($passed -eq $results.Count) { 0 } else { 1 })
