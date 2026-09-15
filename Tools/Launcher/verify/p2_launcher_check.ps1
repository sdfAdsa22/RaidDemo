<#
.SYNOPSIS
    M10 批次 2 验收脚本：启动器的四条判据（首次安装 / 增量更新 / 源切换 / 失败回退）。

.DESCRIPTION
    全部在本地更新源上验证，不需要起任何服务器：
      A. 首次安装   —— 空安装根 → 全量下载 → 再检查应显示"已是新版"
      B. 增量更新   —— 改动 2 个文件、删除 1 个文件 → 只处理这 3 项
      C. 源切换     —— 换一个源地址执行同样的流程
      D. 失败回退   —— 源里有一个文件的哈希对不上 → 更新失败，且安装根保持原样

    用法（在仓库根执行）：
      pwsh -NoProfile -File Tools/Launcher/verify/p2_launcher_check.ps1

.NOTES
    脚本只写 Builds/LauncherTest/ 沙盒目录，不动任何已安装的游戏与更新源目录。
#>

$ErrorActionPreference = 'Stop'

# 与启动器的输出编码保持一致，否则脚本按本机代码页解码会把中文判据变成乱码。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$launcher = Join-Path $repoRoot 'Tools/Launcher/bin/Release/net8.0-windows/win-x64/RaidDemo.Launcher.exe'
$sourceV1 = Join-Path $repoRoot 'Builds/Update/0.10.0'
$sandbox = Join-Path $repoRoot 'Builds/LauncherTest'
$installRoot = Join-Path $sandbox 'Game'
$sourceV2 = Join-Path $sandbox 'source-0.10.1'
$sourceV3 = Join-Path $sandbox 'source-0.10.2'
$sourceBad = Join-Path $sandbox 'source-bad'

if (-not (Test-Path $launcher)) { throw "找不到启动器：$launcher（先执行 dotnet build -c Release）" }
if (-not (Test-Path (Join-Path $sourceV1 'manifest.json'))) { throw "找不到更新源：$sourceV1（先执行菜单 ①）" }

if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $sandbox | Out-Null

function Invoke-Launcher {
    param([string[]]$Arguments)
    $output = & $launcher @Arguments 2>&1 | Out-String
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Get-ManifestJson { param([string]$Path) Get-Content (Join-Path $Path 'manifest.json') -Raw | ConvertFrom-Json }
function Save-ManifestJson { param([string]$Path, $Manifest) $Manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $Path 'manifest.json') -Encoding UTF8 }

$results = New-Object System.Collections.Generic.List[object]
function Add-Result { param([string]$Name, [bool]$Passed, [string]$Detail) $results.Add([pscustomobject]@{ 判据 = $Name; 结果 = $(if ($Passed) { 'PASS' } else { 'FAIL' }); 说明 = $Detail }) }

# ---------- A. 首次安装 ----------
Write-Host '== A. 首次安装（全量下载）' -ForegroundColor Cyan
$check = Invoke-Launcher @('--check', '--source', $sourceV1, '--root', $installRoot)
$planOk = $check.ExitCode -eq 0 -and $check.Output -match '发现更新'

$update = Invoke-Launcher @('--update', '--source', $sourceV1, '--root', $installRoot)
$installedExe = Join-Path $installRoot 'RaidDemo.exe'
$installedOk = $update.ExitCode -eq 0 -and (Test-Path $installedExe)

$recheck = Invoke-Launcher @('--check', '--source', $sourceV1, '--root', $installRoot)
$cleanOk = $recheck.ExitCode -eq 0 -and $recheck.Output -match '已是最新版本'

Add-Result 'A1 空安装根能算出全量计划' $planOk ($check.Output.Trim() -split "`n" | Select-Object -Last 1)
Add-Result 'A2 全量安装成功且落盘' $installedOk ($update.Output.Trim() -split "`n" | Select-Object -Last 1)
Add-Result 'A3 安装后再检查为最新' $cleanOk ($recheck.Output.Trim() -split "`n" | Select-Object -Last 1)

# ---------- B. 增量更新（2 改 1 删） ----------
Write-Host '== B. 增量更新' -ForegroundColor Cyan
Copy-Item $sourceV1 $sourceV2 -Recurse
$manifestV2 = Get-ManifestJson $sourceV2
$manifestV2.body.version = '0.10.1'

$changedPaths = @('UnityCrashHandler64.exe', 'RaidDemo_Data/Managed/RaidDemo.Shared.dll')
foreach ($relative in $changedPaths) {
    $file = Join-Path (Join-Path $sourceV2 'body') ($relative -replace '/', '\')
    Add-Content -Path $file -Value ("`n# patched {0}" -f (Get-Date -Format o)) -Encoding utf8
    $entry = $manifestV2.body.files | Where-Object { $_.path -eq $relative }
    $entry.size = (Get-Item $file).Length
    $entry.sha256 = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
}

# 删除一个文件：清单里也要去掉，这样安装根里的旧文件才会被清理
$removedPath = 'D3D12/D3D12Core.dll'
Remove-Item (Join-Path (Join-Path $sourceV2 'body') 'D3D12/D3D12Core.dll') -Force
$manifestV2.body.files = @($manifestV2.body.files | Where-Object { $_.path -ne $removedPath })
Save-ManifestJson $sourceV2 $manifestV2

$incremental = Invoke-Launcher @('--update', '--source', $sourceV2, '--root', $installRoot)
$expectedSummary = '3 个文件'
$summaryOk = $incremental.ExitCode -eq 0 -and $incremental.Output -match $expectedSummary
$removedGone = -not (Test-Path (Join-Path $installRoot 'D3D12/D3D12Core.dll'))
$patchedOk = $true
foreach ($relative in $changedPaths) {
    $installed = Join-Path $installRoot ($relative -replace '/', '\')
    $expected = Join-Path (Join-Path $sourceV2 'body') ($relative -replace '/', '\')
    if ((Get-FileHash $installed -Algorithm SHA256).Hash -ne (Get-FileHash $expected -Algorithm SHA256).Hash) { $patchedOk = $false }
}
$snapshot = Get-Content (Join-Path $installRoot '.raiddemo/manifest.json') -Raw | ConvertFrom-Json
$snapshotOk = $snapshot.body.version -eq '0.10.1'

Add-Result 'B1 计划只包含 3 个变更文件' $summaryOk ($incremental.Output.Trim() -split "`n" | Select-Object -Last 1)
Add-Result 'B2 变更文件已替换' $patchedOk '与源文件哈希一致'
Add-Result 'B3 已删除文件被清理' $removedGone $removedPath
Add-Result 'B4 本地快照更新为 0.10.1' $snapshotOk ("快照版本=" + $snapshot.body.version)

# ---------- C. 源切换 ----------
Write-Host '== C. 源切换' -ForegroundColor Cyan
Copy-Item $sourceV2 $sourceV3 -Recurse
$manifestV3 = Get-ManifestJson $sourceV3
$manifestV3.body.version = '0.10.2'

# 让新源真的有内容要更新：改一个文件并同步哈希，验证"换源后走的是同一套完整流程"，
# 而不是只在"两个源内容相同"时碰巧通过。
$switchedPath = 'RaidDemo_Data/Managed/RaidDemo.Data.dll'
$switchedFile = Join-Path (Join-Path $sourceV3 'body') ($switchedPath -replace '/', '\')
Add-Content -Path $switchedFile -Value ("`n# switched-source {0}" -f (Get-Date -Format o)) -Encoding utf8
$switchedEntry = $manifestV3.body.files | Where-Object { $_.path -eq $switchedPath }
$switchedEntry.size = (Get-Item $switchedFile).Length
$switchedEntry.sha256 = (Get-FileHash $switchedFile -Algorithm SHA256).Hash.ToLower()
Save-ManifestJson $sourceV3 $manifestV3

$switch = Invoke-Launcher @('--update', '--source', $sourceV3, '--root', $installRoot)
$switchOk = $switch.ExitCode -eq 0 -and $switch.Output -match '1 个文件'
$switchedInstalled = Join-Path $installRoot ($switchedPath -replace '/', '\')
$switchedHashOk = (Get-FileHash $switchedInstalled -Algorithm SHA256).Hash -eq (Get-FileHash $switchedFile -Algorithm SHA256).Hash
$switchSnapshot = (Get-Content (Join-Path $installRoot '.raiddemo/manifest.json') -Raw | ConvertFrom-Json).body.version
Add-Result 'C1 换源后按同一流程更新' $switchOk ($switch.Output.Trim() -split "`n" | Select-Object -Last 1)
Add-Result 'C2 换源下载的文件已生效' $switchedHashOk '与新源文件哈希一致'
Add-Result 'C3 换源后快照更新为 0.10.2' ($switchSnapshot -eq '0.10.2') ("快照=" + $switchSnapshot)

# ---------- D. 失败回退 ----------
Write-Host '== D. 失败回退（哈希不符）' -ForegroundColor Cyan
Copy-Item $sourceV2 $sourceBad -Recurse
$manifestBad = Get-ManifestJson $sourceBad
$manifestBad.body.version = '0.10.3'
$target = $manifestBad.body.files | Where-Object { $_.path -eq 'UnityCrashHandler64.exe' }
$target.sha256 = ('0' * 64)   # 故意写一个永远不会匹配的哈希
Save-ManifestJson $sourceBad $manifestBad

$beforeHash = (Get-FileHash (Join-Path $installRoot 'UnityCrashHandler64.exe') -Algorithm SHA256).Hash
$beforeSnapshot = (Get-Content (Join-Path $installRoot '.raiddemo/manifest.json') -Raw | ConvertFrom-Json).body.version

$failed = Invoke-Launcher @('--update', '--source', $sourceBad, '--root', $installRoot)
$afterHash = (Get-FileHash (Join-Path $installRoot 'UnityCrashHandler64.exe') -Algorithm SHA256).Hash
$afterSnapshot = (Get-Content (Join-Path $installRoot '.raiddemo/manifest.json') -Raw | ConvertFrom-Json).body.version

Add-Result 'D1 校验失败时退出码为 1' ($failed.ExitCode -eq 1) ("退出码=" + $failed.ExitCode)
Add-Result 'D2 失败后安装文件未被改动' ($beforeHash -eq $afterHash) '文件哈希不变'
Add-Result 'D3 失败后版本快照保持不变' ($beforeSnapshot -eq $afterSnapshot -and $beforeSnapshot -eq '0.10.2') ("快照=" + $afterSnapshot)

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host
$failedCount = ($results | Where-Object { $_.结果 -eq 'FAIL' }).Count
Write-Host ("总计 {0} 条，失败 {1} 条" -f $results.Count, $failedCount) -ForegroundColor $(if ($failedCount -eq 0) { 'Green' } else { 'Red' })
exit $failedCount
