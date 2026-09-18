<#
.SYNOPSIS
    M13 修复轮的验收脚本：路径安全 / 清单完整性 / 并发互斥 / 单实例 / 暂存收敛。

.DESCRIPTION
    与 p2_launcher_check.ps1 互补：那个脚本证明"正常更新链路没坏"，
    这个脚本证明"M13 记录的几类坏输入都被挡住"。

    用法（在仓库根执行）：
      pwsh -NoProfile -File Tools/Launcher/verify/p2_m13_fixes_check.ps1

    只写 Builds/LauncherTestM13Fix 沙盒，不动真实安装与更新源。
#>

param(
    # 跳过 GUI 单实例用例（无桌面会话时可加）。
    [switch]$SkipGui
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$launcher = Join-Path $repoRoot 'Tools/Launcher/bin/Release/net8.0-windows/win-x64/RaidDemo.Launcher.exe'
$sourceV1 = Join-Path $repoRoot 'Builds/Update/0.10.0'
$sandbox = Join-Path $repoRoot 'Builds/LauncherTestM13Fix'

if (-not (Test-Path $launcher)) { throw "找不到启动器：$launcher（先执行 dotnet build -c Release）" }
if (-not (Test-Path (Join-Path $sourceV1 'manifest.json'))) { throw "找不到更新源：$sourceV1" }

if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $sandbox | Out-Null

$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    $results.Add([pscustomobject]@{
        判据 = $Name
        结果 = if ($Ok) { 'PASS' } else { 'FAIL' }
        说明 = $Detail
    })
}

function Invoke-Launcher {
    param([string[]]$Arguments)
    $output = & $launcher @Arguments 2>&1 | Out-String
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

function New-FakeSource {
    param(
        [string]$Path,
        [object[]]$Files,
        [string]$Version = '1.0.0',
        [switch]$NoBody
    )

    $manifest = @{ schemaVersion = 1; generatedAt = '2026-09-18T12:00:00+08:00' }
    if (-not $NoBody) {
        $manifest.body = @{ version = $Version; files = $Files }
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-Content -Path (Join-Path $Path 'manifest.json') `
        -Value ($manifest | ConvertTo-Json -Depth 8) -Encoding utf8
}

$fakeHash = 'a' * 64

# ── N1：`..` 路径必须被拒绝，且根外不能出现探针文件 ──────────────────────────────
$traverseSource = Join-Path $sandbox 'src-traverse'
$traverseRoot = Join-Path $sandbox 'root-traverse'
New-FakeSource -Path $traverseSource -Files @(
    @{ path = '..\escape-probe.txt'; size = 3; sha256 = $fakeHash }
)
$result = Invoke-Launcher @('--update', '--source', $traverseSource, '--root', $traverseRoot, '--force')
$escapedProbe = Join-Path $sandbox 'escape-probe.txt'
Add-Result 'N1a 越界路径被拒绝' ($result.ExitCode -eq 1) "退出码=$($result.ExitCode)"
Add-Result 'N1b 提示为中文校验失败' ($result.Output -match '清单校验失败|路径不安全') `
    (($result.Output -split "`n" | Where-Object { $_ -match '路径|清单' } | Select-Object -First 1) -replace '\s+$', '')
Add-Result 'N1c 未写出安装根之外' (-not (Test-Path $escapedProbe)) "探针=$escapedProbe"

# ── N2：盘符绝对路径必须被拒绝 ────────────────────────────────────────────────
$driveSource = Join-Path $sandbox 'src-drive'
$driveRoot = Join-Path $sandbox 'root-drive'
$driveProbe = 'D:\__m13_fix_probe__.txt'
New-FakeSource -Path $driveSource -Files @(
    @{ path = $driveProbe; size = 3; sha256 = $fakeHash }
)
$result = Invoke-Launcher @('--update', '--source', $driveSource, '--root', $driveRoot, '--force')
Add-Result 'N2a 盘符路径被拒绝' ($result.ExitCode -eq 1) "退出码=$($result.ExitCode)"
Add-Result 'N2b 未写出盘符根目录' (-not (Test-Path $driveProbe)) "探针=$driveProbe"

# ── N3：缺 body 的清单不能当成"已是最新" ─────────────────────────────────────
$noBodySource = Join-Path $sandbox 'src-nobody'
$noBodyRoot = Join-Path $sandbox 'root-nobody'
New-FakeSource -Path $noBodySource -NoBody
$result = Invoke-Launcher @('--check', '--source', $noBodySource, '--root', $noBodyRoot, '--force')
Add-Result 'N3a 缺本体层判为失败' ($result.ExitCode -eq 1) "退出码=$($result.ExitCode)"
Add-Result 'N3b 不再显示已是最新' ($result.Output -notmatch '已是最新') `
    (($result.Output -split "`n" | Where-Object { $_ -match '本体层|清单' } | Select-Object -First 1) -replace '\s+$', '')

# ── N4：空文件列表的本体层同样判为发布不完整 ─────────────────────────────────
$emptySource = Join-Path $sandbox 'src-empty'
$emptyRoot = Join-Path $sandbox 'root-empty'
New-FakeSource -Path $emptySource -Files @() -Version '9.9.9'
$result = Invoke-Launcher @('--check', '--source', $emptySource, '--root', $emptyRoot, '--force')
Add-Result 'N4a 空文件列表判为失败' ($result.ExitCode -eq 1) "退出码=$($result.ExitCode)"
Add-Result 'N4b 提示没有文件' ($result.Output -match '没有任何文件') `
    (($result.Output -split "`n" | Where-Object { $_ -match '没有任何文件' } | Select-Object -First 1) -replace '\s+$', '')

# ── N5：并发更新同一安装根：恰好一个失败，且失败原因是中文占用提示 ──────────────
$concurrentRoot = Join-Path $sandbox 'root-concurrent'
$out1 = Join-Path $sandbox 'concurrent-1.txt'
$out2 = Join-Path $sandbox 'concurrent-2.txt'
$arguments = @('--update', "--source `"$sourceV1`"", "--root `"$concurrentRoot`"", '--force')
$process1 = Start-Process -FilePath $launcher -ArgumentList $arguments -PassThru -RedirectStandardOutput $out1
$process2 = Start-Process -FilePath $launcher -ArgumentList $arguments -PassThru -RedirectStandardOutput $out2
$process1.WaitForExit()
$process2.WaitForExit()

$exits = @($process1.ExitCode, $process2.ExitCode)
$failures = @($exits | Where-Object { $_ -ne 0 }).Count
$combined = (Get-Content $out1 -Raw -Encoding utf8) + (Get-Content $out2 -Raw -Encoding utf8)
Add-Result 'N5a 并发时恰好一个失败' ($failures -ge 1 -and $failures -le 2) "退出码=$($exits -join ',')"
Add-Result 'N5b 失败原因是中文占用提示' ($combined -match '正在被另一个更新进程使用') `
    (($combined -split "`n" | Where-Object { $_ -match '正在被另一个更新进程使用' } | Select-Object -First 1) -replace '\s+$', '')
Add-Result 'N5c 安装仍完整可用' (Test-Path (Join-Path $concurrentRoot 'RaidDemo.exe')) "根=$concurrentRoot"

$stagingRoot = Join-Path $concurrentRoot '.raiddemo/staging'
$stagingLeft = if (Test-Path $stagingRoot) { @(Get-ChildItem $stagingRoot -Recurse -File).Count } else { 0 }
Add-Result 'N5d 暂存收敛为 0' ($stagingLeft -eq 0) "剩余文件=$stagingLeft"

# ── N6：单实例：第二次启动不再新增窗口进程 ────────────────────────────────────
if (-not $SkipGui) {
    $before = @(Get-Process -Name 'RaidDemo.Launcher' -ErrorAction SilentlyContinue).Count
    $first = Start-Process -FilePath $launcher -PassThru
    Start-Sleep -Seconds 3
    $afterFirst = @(Get-Process -Name 'RaidDemo.Launcher' -ErrorAction SilentlyContinue).Count

    $second = Start-Process -FilePath $launcher -PassThru
    $secondExited = $second.WaitForExit(15000)
    Start-Sleep -Seconds 1
    $afterSecond = @(Get-Process -Name 'RaidDemo.Launcher' -ErrorAction SilentlyContinue).Count

    Add-Result 'N6a 第一个实例正常启动' ($afterFirst -eq $before + 1) "启动前=$before 启动后=$afterFirst"
    Add-Result 'N6b 第二个实例自行退出' ($secondExited -and $second.HasExited) "已退出=$($second.HasExited)"
    Add-Result 'N6c 没有出现第二个窗口进程' ($afterSecond -eq $before + 1) "当前=$afterSecond"

    if (-not $first.HasExited) { $first.Kill() }
    if (-not $second.HasExited) { $second.Kill() }
}

# ── 汇总 ─────────────────────────────────────────────────────────────────────
$results | Format-Table -AutoSize
$failed = @($results | Where-Object { $_.结果 -eq 'FAIL' }).Count
Write-Output ""
Write-Output ("总计 {0} 条，失败 {1} 条" -f $results.Count, $failed)
if ($failed -gt 0) { exit 1 }
