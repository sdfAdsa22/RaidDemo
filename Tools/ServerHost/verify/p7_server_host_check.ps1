# =============================================================================
# Windows 服务器面板验收（Tools/ServerHost/RaidDemo.ServerHost.exe）
# =============================================================================
# 分组（冒号后面是"为什么这条判据必须存在"）：
#   A 默认配置写出：A1 文件生成 + stdout 绝对路径（打包脚本据此判成败）；A2 11 个字段取值
#     （默认值就是发给玩家的那份配置的事实来源）；A3 幂等（打包会反复执行，内容抖动会让
#     "这次打的包是不是同一份"无法回答）。
#   B --check 输出契约：B1/B2 退出码与关键字段文案（stdout 就是接口）；B3 缺配置时回退内置默认值
#     （首次开服没有配置文件是常态）；B4 14 行齐全且顺序一致（顺序漂移会让断言静默失效）。
#   C 非法配置必须被拒（端口 0 / 坏 JSON / saveDir 绝对路径 / grace 越界）：判的是"拒绝"而不是
#     "报错"——配置被静默忽略时，现象是"我明明改了端口却连不上"。
#   D --render 离屏出界面截图：界面也是交付物；离屏渲染不弹窗，无桌面会话里也能取证。
#   E 真机链路（缺 Builds\ServerWindows\RaidDemoServer.exe 则整段跳过）：配置交给真实服务器后，
#     日志要有配置里的端口 / 房间名（证明配置生效）、状态页要报配置端口、停服要真的停进程。
#
# A~D 全在 %TEMP%\rd-serverhost-check 下自造目录：不依赖本机已有配置，也不污染仓库。
#
# 前置：dotnet publish Tools\ServerHost\RaidDemo.ServerHost.csproj -c Release
#       （面板 exe 是本脚本的被测对象：不在就打印这条命令并以退出码 1 结束，不做"跳过"）
# 用法：pwsh -NoProfile -File Tools/ServerHost/verify/p7_server_host_check.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

# 面板把中文写到 stdout / stderr。不显式指定解码方式时，本机区域设置会把它解成乱码，
# 于是"按文案断言"的判据全部假失败（启动器与更新源的验收脚本踩过同一个坑）。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
# C 段是"故意让面板失败"的：不要让 PowerShell 把子进程的 stderr 当成脚本自身的错误。
$PSNativeCommandUseErrorActionPreference = $false

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$panelExe = Join-Path $projectRoot 'Tools\ServerHost\bin\Release\net8.0-windows\win-x64\publish\RaidDemo.ServerHost.exe'
$serverExe = Join-Path $projectRoot 'Builds\ServerWindows\RaidDemoServer.exe'

# 固定名字便于失败后进去看现场（每次运行先清空）。E 段端口避开 7777 / 8080 与其它脚本的占用。
$tempDirectory = [System.IO.Path]::GetTempPath()
$workRoot = Join-Path $tempDirectory 'rd-serverhost-check'
$realServerPort = 47795
$realDashboardPort = 47796
$realRoomName = '面板验收'

# 兜底：publish -o <目录> 指定过输出位置时产物不在默认路径，按时间取最新的一份，免得假报未构建。
if (-not (Test-Path $panelExe)) {
    $fallback = Get-ChildItem -Path (Join-Path $projectRoot 'Tools\ServerHost\bin') -Recurse -Filter 'RaidDemo.ServerHost.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $fallback) { $panelExe = $fallback.FullName }
}

if (-not (Test-Path $panelExe)) {
    Write-Host '找不到服务器面板产物。请先执行：' -ForegroundColor Red
    Write-Host '  dotnet publish Tools\ServerHost\RaidDemo.ServerHost.csproj -c Release' -ForegroundColor Yellow
    exit 1
}

# 安全闸：只允许删 %TEMP% 下的工作目录，避免变量写错时误删别处。
if (-not $workRoot.StartsWith($tempDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝执行：工作目录 $workRoot 不在临时目录内。"
}
if (Test-Path $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
New-Item -ItemType Directory -Path $workRoot | Out-Null

$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([string]$Name, [bool]$Pass, [string]$Detail, [switch]$Skip)
    $script:results.Add([pscustomobject]@{ Name = $Name; Pass = $Pass; Detail = $Detail; Skip = [bool]$Skip })
}

function Add-Skipped {
    param([string]$Name, [string]$Detail)
    Add-Result -Name $Name -Pass $false -Detail $Detail -Skip
}

# 异常路径也要留下判据记录：否则"少了一条判据"会被读成"跑过了"。
function Add-FailureIfMissing {
    param([string]$Name, [string]$Detail)
    if ($null -eq ($script:results | Where-Object { $_.Name -eq $Name })) {
        Add-Result -Name $Name -Pass $false -Detail $Detail
    }
}

function Read-TextFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return '' }
    $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -eq $text) { return '' }
    return [string]$text
}

# 为什么统一用 Start-Process 而不是 `& $exe`：面板现在是控制台子系统，`&` 会等待、也能拿到退出码；
# 但脚本不该依赖"被测程序恰好是什么子系统"——曾经它是 WinExe，而 PowerShell 调用 GUI 子系统的
# 程序**不会等待**：`& $exe --check …` 立刻返回，读到的是上一次的退出码、stdout 还没写完，
# 现象是"脚本 1 秒跑完但判据全是假通过"。Start-Process -Wait -PassThru 对两种子系统都成立。
# 另外 stdout 与 stderr 必须分开落地：C 段要判"报错写去了 stderr"。
function Invoke-PanelCli {
    param([string[]]$Arguments, [string]$Tag)

    $outFile = Join-Path $script:workRoot "$Tag.stdout.txt"
    $errFile = Join-Path $script:workRoot "$Tag.stderr.txt"
    foreach ($file in @($outFile, $errFile)) {
        if (Test-Path $file) { Remove-Item -LiteralPath $file -Force }
    }

    # Start-Process 会把参数数组用空格拼起来，含空格的路径必须自己加引号。
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } })
    $process = Start-Process -FilePath $script:panelExe -ArgumentList $quoted -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StdOut   = (Read-TextFile $outFile)
        StdErr   = (Read-TextFile $errFile)
    }
}

# 失败时把"第一句有内容的输出"写进说明：报告里能直接读到原因，不用再翻文件。
function Get-CliDetail {
    param([pscustomobject]$Result)
    $firstLine = (($Result.StdErr -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 1)
    if (-not $firstLine) { $firstLine = (($Result.StdOut -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 1) }
    if (-not $firstLine) { $firstLine = '没有任何输出' }
    return "退出码 $($Result.ExitCode)；$firstLine"
}

# 非法用例的"底稿"必须是面板自己写出来的：否则验的是脚本造的格式，不是面板的。
# 底稿不在（A1 已失败）时不抛异常——让报告把每条判据都摆出来，比在第一处异常上中断有用。
function New-CaseDirectory {
    param([string]$Name)
    $directory = Join-Path $script:workRoot $Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    if (Test-Path $script:defaultConfigPath) {
        Copy-Item -LiteralPath $script:defaultConfigPath -Destination (Join-Path $directory 'server.config.json') -Force
    }
    return $directory
}

function Set-ConfigField {
    param([string]$Path, [string]$Name, [object]$Value)
    if (-not (Test-Path $Path)) { return }
    $document = (Get-Content -LiteralPath $Path -Raw -Encoding UTF8) | ConvertFrom-Json
    $property = $document.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "配置里没有字段 $Name，无法构造这条用例。" }
    $property.Value = $Value
    $document | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Wait-LogPattern {
    param([string]$Path, [string]$Pattern, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = Read-TextFile $Path
        if ($text -and $text -match $Pattern) { return [pscustomobject]@{ Matched = $true; Text = $text } }
        Start-Sleep -Milliseconds 500
    }
    return [pscustomobject]@{ Matched = $false; Text = (Read-TextFile $Path) }
}

# 报告一定要打出来：中途异常时"没有输出"会被读成"没跑过"，而判据全在内存里。
function Write-Summary {
    Write-Host ''
    foreach ($result in $script:results) {
        $mark = if ($result.Skip) { 'SKIP' } elseif ($result.Pass) { 'PASS' } else { 'FAIL' }
        Write-Host ("[{0}] {1} —— {2}" -f $mark, $result.Name, $result.Detail)
    }

    $script:passedCount = @($script:results | Where-Object { $_.Pass -and -not $_.Skip }).Count
    $script:failedCount = @($script:results | Where-Object { -not $_.Pass -and -not $_.Skip }).Count
    $skippedCount = @($script:results | Where-Object { $_.Skip }).Count

    Write-Host ''
    Write-Host ("通过 {0} / 失败 {1} / 跳过 {2}" -f $script:passedCount, $script:failedCount, $skippedCount) -ForegroundColor $(
        if ($script:failedCount -eq 0) { 'Green' } else { 'Red' })
    Write-Host "工作目录：$script:workRoot"
}

trap {
    Write-Host "脚本中断：$($_.Exception.Message)" -ForegroundColor Red
    Write-Summary
    exit 2
}

Write-Host "面板产物：$panelExe"

# ------------------------------- A. 默认配置写出 ------------------------------
$configDirectory = Join-Path $workRoot 'config'
$configPath = Join-Path $configDirectory 'server.config.json'
$defaultConfigPath = $configPath
# 契约只承诺"写出到给定目录"，没承诺"自动建目录"：脚本先建好，免得把未承诺的行为变成假失败。
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null

$writeResult = Invoke-PanelCli -Arguments @('--write-default-config', $configDirectory) -Tag 'a1-write-default-config'
$writePass = $writeResult.ExitCode -eq 0 -and (Test-Path $configPath) -and $writeResult.StdOut.Contains('已写出默认配置：' + $configPath)
Add-Result 'A1 --write-default-config 写出配置文件' $writePass $(if ($writePass) { $configPath } else { (Get-CliDetail $writeResult) })

$expectedDefaults = [ordered]@{
    port = 7777; room = '默认房间'; saveDir = 'server_saves'; logLevel = 'info'; map = 'GreyboxRaid'
    raidDuration = 480; autoStart = 0; dashboardPort = 8080; discoveryPort = 47777; grace = 60; watchdog = 2.5
}

$defaultDocument = $null
$jsonProblem = $null
try { $defaultDocument = (Get-Content -LiteralPath $configPath -Raw -Encoding UTF8) | ConvertFrom-Json }
catch { $jsonProblem = $_.Exception.Message }

$fieldProblems = New-Object System.Collections.Generic.List[string]
if ($null -eq $defaultDocument) {
    $fieldProblems.Add("不是合法 JSON：$jsonProblem")
}
else {
    $actualFieldCount = @($defaultDocument.PSObject.Properties.Name).Count
    if ($actualFieldCount -ne $expectedDefaults.Count) { $fieldProblems.Add("字段数 $actualFieldCount（期望 $($expectedDefaults.Count)）") }
    foreach ($key in $expectedDefaults.Keys) {
        if ($defaultDocument.$key -ne $expectedDefaults[$key]) { $fieldProblems.Add("$key=$($defaultDocument.$key)（期望 $($expectedDefaults[$key])）") }
    }
}

$defaultsPass = $fieldProblems.Count -eq 0
Add-Result 'A2 默认值 11 个字段与契约一致' $defaultsPass $(
    if ($defaultsPass) { 'port / room / saveDir / logLevel / map / raidDuration / autoStart / dashboardPort / discoveryPort / grace / watchdog 全部匹配' }
    else { ($fieldProblems -join '；') })

$configBeforeRepeat = Read-TextFile $configPath
$repeatResult = Invoke-PanelCli -Arguments @('--write-default-config', $configDirectory) -Tag 'a3-write-default-config-again'
$configAfterRepeat = Read-TextFile $configPath
$repeatPass = $repeatResult.ExitCode -eq 0 -and $configAfterRepeat -eq $configBeforeRepeat
Add-Result 'A3 重复写出幂等（内容稳定）' $repeatPass $(
    if ($repeatPass) { '两次写出内容逐字符一致' }
    elseif ($repeatResult.ExitCode -ne 0) { (Get-CliDetail $repeatResult) }
    else { '第二次写出的内容与第一次不同（打包结果不可复现）' })

# ------------------------------ B. --check 输出契约 ---------------------------
$checkResult = Invoke-PanelCli -Arguments @('--check', $configDirectory) -Tag 'b1-check'
Add-Result 'B1 --check 对生成的配置返回 0' ($checkResult.ExitCode -eq 0) $(
    if ($checkResult.ExitCode -eq 0) { '退出码 0' } else { (Get-CliDetail $checkResult) })

$requiredFragments = @('端口：7777', '房间：默认房间', '状态页端口：8080')
$missingFragments = @($requiredFragments | Where-Object { -not $checkResult.StdOut.Contains($_) })
Add-Result 'B2 --check 打印端口 / 房间 / 状态页端口' ($missingFragments.Count -eq 0) $(
    if ($missingFragments.Count -eq 0) { '三行关键字段齐全' } else { '缺：' + ($missingFragments -join '、') })

# 没有配置文件是首次开服的常态：这时必须按内置默认值给出一份可读的检查结果。
$withoutConfigDirectory = Join-Path $workRoot 'without-config'
New-Item -ItemType Directory -Path $withoutConfigDirectory -Force | Out-Null
$fallbackResult = Invoke-PanelCli -Arguments @('--check', $withoutConfigDirectory) -Tag 'b3-check-without-config'
$fallbackOk = $fallbackResult.StdOut.Contains('不存在，按内置默认值') -and $fallbackResult.StdOut.Contains('端口：7777')
Add-Result 'B3 缺配置文件时回退内置默认值' ($fallbackResult.ExitCode -eq 0 -and $fallbackOk) $(
    if ($fallbackResult.ExitCode -ne 0) { (Get-CliDetail $fallbackResult) }
    elseif (-not $fallbackOk) { 'stdout 缺少「不存在，按内置默认值」或「端口：7777」' }
    else { '退出码 0，且按内置默认值给出 7777' })

# 顺序也是接口的一部分：读的人按位置找字段、脚本按行断言。这里判"按顺序出现的子序列"
# 而不是"整份输出完全相等"——多一行横幅不该判失败，字段顺序换了必须判失败。
# 注意每条都要用 $() 插值包起来：PowerShell 里逗号比加号绑得更紧，
# 「'前缀' + $变量, '下一条'」会被拼成一个整体，判据就变成了对着一条不存在的行找茬。
$orderedPatterns = @(
    "^服务器目录：$([regex]::Escape($configDirectory))",
    "^配置文件：$([regex]::Escape($configPath))[（(]已存在[）)]",
    "^服务器程序：RaidDemoServer\.exe 缺失[（(]$([regex]::Escape((Join-Path $configDirectory 'RaidDemoServer.exe')))[）)]",
    '^端口：7777\s*$',
    '^房间：默认房间\s*$',
    '^存档目录：server_saves\s*$',
    '^地图：GreyboxRaid\s*$',
    '^日志等级：info\s*$',
    '^战局时长上限：480 秒\s*$',
    '^自动开局：0 秒\s*$',
    '^状态页端口：8080\s*$',
    '^局域网发现端口：47777\s*$',
    '^掉线宽限：60 秒\s*$',
    '^自愈看门狗：2\.5 秒\s*$'
)

$cursor = 0
$firstMissingPattern = $null
foreach ($pattern in $orderedPatterns) {
    $remaining = $checkResult.StdOut.Substring([Math]::Min($cursor, $checkResult.StdOut.Length))
    $match = [regex]::Match($remaining, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) { $firstMissingPattern = $pattern; break }
    $cursor += $match.Index + $match.Length
}

Add-Result 'B4 --check 的 14 行输出齐全且顺序一致' ($null -eq $firstMissingPattern) $(
    if ($null -eq $firstMissingPattern) { '14 行按契约顺序出现（数字用不变文化：2.5 而非 2,5）' }
    else { "缺少（或顺序不对）：$firstMissingPattern" })

# ---------------------------- C. 非法配置必须被拒 -----------------------------
$portCaseDirectory = New-CaseDirectory 'config-port-0'
Set-ConfigField -Path (Join-Path $portCaseDirectory 'server.config.json') -Name 'port' -Value 0
$portCase = Invoke-PanelCli -Arguments @('--check', $portCaseDirectory) -Tag 'c1-check-port-0'
Add-Result 'C1 端口 0 被拒且报错指向端口' ($portCase.ExitCode -eq 1 -and $portCase.StdErr.Contains('端口')) (Get-CliDetail $portCase)

$jsonCaseDirectory = Join-Path $workRoot 'config-bad-json'
New-Item -ItemType Directory -Path $jsonCaseDirectory -Force | Out-Null
Set-Content -LiteralPath (Join-Path $jsonCaseDirectory 'server.config.json') -Value '{ 这不是 JSON' -Encoding UTF8
$jsonCase = Invoke-PanelCli -Arguments @('--check', $jsonCaseDirectory) -Tag 'c2-check-bad-json'
Add-Result 'C2 坏 JSON 被拒且报错提到 JSON' ($jsonCase.ExitCode -eq 1 -and $jsonCase.StdErr.ToUpperInvariant().Contains('JSON')) (Get-CliDetail $jsonCase)

$saveCaseDirectory = New-CaseDirectory 'config-save-absolute'
Set-ConfigField -Path (Join-Path $saveCaseDirectory 'server.config.json') -Name 'saveDir' -Value 'C:\saves'
$saveCase = Invoke-PanelCli -Arguments @('--check', $saveCaseDirectory) -Tag 'c3-check-save-absolute'
Add-Result 'C3 saveDir 绝对路径被拒且提到相对路径' ($saveCase.ExitCode -eq 1 -and $saveCase.StdErr.Contains('相对路径')) (Get-CliDetail $saveCase)

$graceCaseDirectory = New-CaseDirectory 'config-grace-1'
Set-ConfigField -Path (Join-Path $graceCaseDirectory 'server.config.json') -Name 'grace' -Value 1
$graceCase = Invoke-PanelCli -Arguments @('--check', $graceCaseDirectory) -Tag 'c4-check-grace-1'
Add-Result 'C4 grace 小于下限 5 被拒' ($graceCase.ExitCode -eq 1) (Get-CliDetail $graceCase)

# ------------------------------ D. 界面离屏渲染 -------------------------------
$renderDirectory = Join-Path $workRoot 'render'
New-Item -ItemType Directory -Path $renderDirectory -Force | Out-Null
$renderPath = Join-Path $renderDirectory 'panel.png'

$renderResult = Invoke-PanelCli -Arguments @('--render', $renderPath, '--dir', $configDirectory) -Tag 'd1-render'
$renderSize = if (Test-Path $renderPath) { (Get-Item -LiteralPath $renderPath).Length } else { 0 }
$renderPass = $renderResult.ExitCode -eq 0 -and $renderSize -gt 10240
Add-Result 'D1 --render 产出 PNG 且体积大于 10 KB' $renderPass $(
    if ($renderPass) { "$([Math]::Round($renderSize / 1KB, 1)) KB：$renderPath" } else { (Get-CliDetail $renderResult) })

$renderTextPass = $renderResult.StdOut.Contains('已渲染界面：' + $renderPath)
Add-Result 'D2 --render 打印已渲染界面文案' $renderTextPass $(
    if ($renderTextPass) { $renderPath } else { "stdout：$($renderResult.StdOut.Trim())" })

# --------------------------- E. 真机链路（可跳过） -----------------------------
$eNames = @(
    'E1 服务器 45 秒内进入就绪（日志出现「大厅已就绪」）',
    'E2 日志里出现配置的端口或房间名（配置真的生效）',
    'E3 状态页 /status.json 返回 200 且 listening / port 正确',
    'E4 状态页停服入口返回 200 且进程 20 秒内退出'
)

if (-not (Test-Path $serverExe)) {
    foreach ($name in $eNames) { Add-Skipped -Name $name -Detail '跳过：缺少 Builds/ServerWindows/RaidDemoServer.exe' }
}
else {
    $realDirectory = Join-Path $workRoot 'real-server'
    $realConfigPath = Join-Path $realDirectory 'server.config.json'
    $realLogDirectory = Join-Path $realDirectory 'Logs'
    $realLogPath = Join-Path $realLogDirectory 'server.log'
    New-Item -ItemType Directory -Path $realLogDirectory -Force | Out-Null

    $serverProcess = $null
    try {
        # 用面板自己写默认配置再改四个字段：这样"配置文件"的来源与玩家点出来的一致。
        $realWrite = Invoke-PanelCli -Arguments @('--write-default-config', $realDirectory) -Tag 'e-write-default-config'
        if ($realWrite.ExitCode -ne 0 -or -not (Test-Path $realConfigPath)) {
            throw "E 段无法准备配置：$(Get-CliDetail $realWrite)"
        }

        Set-ConfigField -Path $realConfigPath -Name 'port' -Value $realServerPort
        Set-ConfigField -Path $realConfigPath -Name 'room' -Value $realRoomName
        Set-ConfigField -Path $realConfigPath -Name 'dashboardPort' -Value $realDashboardPort
        Set-ConfigField -Path $realConfigPath -Name 'discoveryPort' -Value 0

        # 参数与 Linux 侧 deploy/server.sh 同一组开关；工作目录 = 服务器目录、
        # 配置与日志都用绝对路径传，避免位置漂移。
        $serverProcess = Start-Process -FilePath $serverExe `
            -ArgumentList @('-server', '-batchmode', '-nographics', '-config', "`"$realConfigPath`"", '-logFile', "`"$realLogPath`"") `
            -WorkingDirectory $realDirectory -WindowStyle Hidden -PassThru

        $ready = Wait-LogPattern -Path $realLogPath -Pattern '大厅已就绪' -TimeoutSeconds 45
        Add-Result $eNames[0] $ready.Matched $(
            if ($ready.Matched) { '45 秒内出现「大厅已就绪」' } else { '45 秒内未见「大厅已就绪」' })

        $logText = [string]$ready.Text
        $logHasRoom = $logText.Contains($realRoomName)
        $logHasPort = $logText.Contains([string]$realServerPort)
        $configTookEffect = $ready.Matched -and ($logHasRoom -or $logHasPort)
        Add-Result $eNames[1] $configTookEffect $(
            if (-not $ready.Matched) { '服务器未就绪，无从判定配置是否生效' }
            elseif ($logHasRoom -and $logHasPort) { "日志同时出现房间名「$realRoomName」与端口 $realServerPort" }
            elseif ($logHasRoom) { "日志出现房间名「$realRoomName」" }
            elseif ($logHasPort) { "日志出现端口 $realServerPort" }
            else { "日志既没有房间名「$realRoomName」也没有端口 $realServerPort（配置可能被忽略）" })

        # 状态页可能比"大厅已就绪"晚一点起来，所以给有限次重试而不是只试一次。
        $statusOkay = $false
        $statusDetail = '状态页始终没有可用的 JSON'
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            try {
                $response = Invoke-WebRequest "http://127.0.0.1:$realDashboardPort/status.json" -UseBasicParsing -TimeoutSec 5
                $statusJson = $response.Content | ConvertFrom-Json
                $statusOkay = ($statusJson.listening -eq $true -and $statusJson.port -eq $realServerPort)
                $statusDetail = "HTTP $([int]$response.StatusCode)，listening=$($statusJson.listening)，port=$($statusJson.port)"
                if ($statusOkay) { break }
            }
            catch { $statusDetail = "请求失败：$($_.Exception.Message)" }
            Start-Sleep -Seconds 1
        }

        Add-Result $eNames[2] $statusOkay $statusDetail

        # 停服入口判两件事：请求被受理 + 进程真的退了（只判 200 会漏掉"返回 200 却没停"）。
        $wasRunning = -not $serverProcess.HasExited
        $stopStatusCode = 0
        $stopError = ''
        try {
            $stopResponse = Invoke-WebRequest "http://127.0.0.1:$realDashboardPort/?action=stop-server" -UseBasicParsing -TimeoutSec 5
            $stopStatusCode = [int]$stopResponse.StatusCode
        }
        catch { $stopError = $_.Exception.Message }

        $exited = $serverProcess.WaitForExit(20000)
        Add-Result $eNames[3] ($stopStatusCode -eq 200 -and $wasRunning -and $exited) $(
            if (-not $wasRunning -and $stopStatusCode -eq 0) { '请求停服前进程就已经退出（服务器自己崩了）' }
            elseif ($stopStatusCode -ne 200) { "停服请求未返回 200（$stopStatusCode）$stopError" }
            elseif (-not $exited) { '停服请求返回 200，但进程 20 秒内没有退出' }
            else { '停服请求返回 200，进程已退出' })
    }
    catch {
        # 异常也要留记录：一条判据"没出现"会被读成"跑过了"。
        foreach ($name in $eNames) { Add-FailureIfMissing -Name $name -Detail "E 段执行中断：$($_.Exception.Message)" }
    }
    finally {
        if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Summary
exit $(if ($failedCount -eq 0) { 0 } else { 1 })
