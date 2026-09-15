<#
.SYNOPSIS
    M10 批次 3 验收脚本：更新源服务端（HTTP 托管 / Range / 上传 / 发布 / 回滚 / 校验 / 权限）。

.DESCRIPTION
    在本地沙盒里起一个真实的服务端进程，然后用 HTTP 请求与启动器分别验证：
      A. 面板与只读接口可用（/、/api/status、/health）
      B. 上传真实版本包（158 MB，逐文件哈希校验）→ 未发布时客户端拿不到清单
      C. 发布后：客户端（启动器）能通过 HTTP 全量下载并安装
      D. Range 断点续传：Range 请求返回 206 且字节数正确
      E. 校验接口、权限（错误口令 401）、当前版本不可删除
      F. 回滚：发布旧版本后客户端能看到旧版本

    用法（在仓库根执行）：
      pwsh -NoProfile -File Tools/UpdateSource/verify/p3_updatesource_check.ps1

.NOTES
    只写 Builds/UpdateSourceTest/ 沙盒目录；端口默认 8091（避开 8090 与游戏服务器端口）。
#>

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$serverExe = Join-Path $repoRoot 'Tools/UpdateSource/bin/Release/net8.0/win-x64/RaidDemo.UpdateSource.exe'
$launcherExe = Join-Path $repoRoot 'Tools/Launcher/bin/Release/net8.0-windows/win-x64/RaidDemo.Launcher.exe'
$packageV1 = Join-Path $repoRoot 'Builds/Update/0.10.0'
$sandbox = Join-Path $repoRoot 'Builds/UpdateSourceTest'
$sourceRoot = Join-Path $sandbox 'source'
$installRoot = Join-Path $sandbox 'Game'
$port = 8091
$baseUrl = "http://127.0.0.1:$port"
$token = 'test-token-8091'

if (-not (Test-Path $serverExe)) { throw "找不到服务端：$serverExe（先执行 dotnet build -c Release）" }
if (-not (Test-Path $launcherExe)) { throw "找不到启动器：$launcherExe" }
if (-not (Test-Path (Join-Path $packageV1 'manifest.json'))) { throw "找不到 0.10.0 版本包：$packageV1（先执行 Unity 菜单 ①）" }

if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $sandbox, $sourceRoot, $installRoot | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Add-Result { param([string]$Name, [bool]$Passed, [string]$Detail) $results.Add([pscustomobject]@{ 判据 = $Name; 结果 = $(if ($Passed) { 'PASS' } else { 'FAIL' }); 说明 = $Detail }) }

$server = $null
try {
    # ---------- 启动服务端 ----------
    Write-Host '== 启动更新源服务端（127.0.0.1:' -NoNewline; Write-Host "$port）" -ForegroundColor Cyan
    # 注意：Start-Process 的 -ArgumentList 数组是用空格拼接的，路径含空格必须自己加引号，
    # 否则 "D:\unity game\…" 会被切成两个参数，服务端就会把更新源建到 "D:\unity" 去。
    $server = Start-Process -FilePath $serverExe `
        -ArgumentList "--root `"$sourceRoot`" --port $port --host 127.0.0.1 --token $token" `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $sandbox 'server.out.txt') -RedirectStandardError (Join-Path $sandbox 'server.err.txt')

    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        try { if ((Invoke-WebRequest "$baseUrl/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { $ready = $true; break } } catch { }
    }
    if (-not $ready) { throw "服务端未在 10 秒内就绪；stderr：$(Get-Content (Join-Path $sandbox 'server.err.txt') -Raw -ErrorAction SilentlyContinue)" }

    # 防呆：确认服务端用的真的是沙盒目录（路径被截断时这里会立刻失败，而不是污染别的目录）。
    $startupLog = Get-Content (Join-Path $sandbox 'server.out.txt') -Raw -Encoding UTF8
    if ($startupLog -notmatch [regex]::Escape((Resolve-Path $sourceRoot).Path)) {
        throw "服务端使用了错误的更新源目录；启动日志：$startupLog"
    }

    # ---------- A. 面板与只读接口 ----------
    $panel = Invoke-WebRequest "$baseUrl/" -UseBasicParsing
    Add-Result 'A1 管理面板可访问' ($panel.StatusCode -eq 200 -and $panel.Content -match 'RaidDemo · 更新源') ("HTTP " + $panel.StatusCode)

    $status = (Invoke-WebRequest "$baseUrl/api/status" -UseBasicParsing).Content | ConvertFrom-Json
    Add-Result 'A2 状态接口返回根目录与版本列表' ($status.root -eq (Resolve-Path $sourceRoot).Path) ("versions=" + $status.versions.Count)

    $manifest404 = $false
    try { Invoke-WebRequest "$baseUrl/manifest.json" -UseBasicParsing | Out-Null } catch { $manifest404 = $_.Exception.Response.StatusCode.value__ -eq 404 }
    Add-Result 'A3 未发布时清单返回 404' $manifest404 '避免客户端拿到空清单'

    # ---------- B. 上传真实版本包 ----------
    Write-Host '== 上传 0.10.0 版本包（约 158 MB，逐文件校验）' -ForegroundColor Cyan
    $zipPath = Join-Path $sandbox 'package-0.10.0.zip'
    Compress-Archive -Path (Join-Path $packageV1 '*') -DestinationPath $zipPath -CompressionLevel Fastest
    $zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

    $upload = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/upload?version=0.10.0" -Headers @{ 'X-Auth-Token' = $token } -InFile $zipPath -ContentType 'application/zip'
    Add-Result 'B1 真实版本包导入成功' ($upload.success -and $upload.fileCount -eq 231) ("$zipMb MB 上传；" + $upload.message)

    $badToken = $false
    try { Invoke-RestMethod -Method Post -Uri "$baseUrl/api/publish?version=0.10.0" -Headers @{ 'X-Auth-Token' = 'wrong' } | Out-Null }
    catch { $badToken = $_.Exception.Response.StatusCode.value__ -eq 401 }
    Add-Result 'B2 错误口令被拒绝（401）' $badToken '写操作鉴权生效'

    $stillUnpublished = $false
    try { Invoke-WebRequest "$baseUrl/manifest.json" -UseBasicParsing | Out-Null } catch { $stillUnpublished = $true }
    Add-Result 'B3 导入不等于发布' $stillUnpublished '根清单仍为空，客户端看不到未发布的版本'

    # ---------- C. 发布 + 经 HTTP 全量安装 ----------
    Write-Host '== 发布 0.10.0 并让启动器经 HTTP 全量安装' -ForegroundColor Cyan
    $publish = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/publish?version=0.10.0" -Headers @{ 'X-Auth-Token' = $token }
    Add-Result 'C1 发布成功' $publish.success $publish.message

    $check = & $launcherExe --check --source $baseUrl --root $installRoot 2>&1 | Out-String
    Add-Result 'C2 启动器经 HTTP 算出全量计划' ($LASTEXITCODE -eq 0 -and $check -match '231 个文件') (($check.Trim() -split "`n")[-1])

    $update = & $launcherExe --update --source $baseUrl --root $installRoot 2>&1 | Out-String
    Add-Result 'C3 启动器经 HTTP 全量安装成功' ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $installRoot 'RaidDemo.exe'))) (($update.Trim() -split "`n")[-1])

    $recheck = & $launcherExe --check --source $baseUrl --root $installRoot 2>&1 | Out-String
    Add-Result 'C4 再检查为最新' ($LASTEXITCODE -eq 0 -and $recheck -match '已是最新版本') (($recheck.Trim() -split "`n")[-1])

    # ---------- D. Range 断点续传 ----------
    $rangeResponse = Invoke-WebRequest "$baseUrl/body/RaidDemo.exe" -Headers @{ Range = 'bytes=0-99' } -UseBasicParsing
    $contentRange = [string]$rangeResponse.Headers['Content-Range']
    $rangeOk = $rangeResponse.StatusCode -eq 206 -and $rangeResponse.RawContentLength -eq 100 `
        -and $contentRange -match '^bytes 0-99/\d+$'
    Add-Result 'D1 Range 请求返回 206 与正确片段' $rangeOk ("Content-Range=" + $contentRange)

    $fullResponse = Invoke-WebRequest "$baseUrl/body/RaidDemo.exe" -UseBasicParsing -Method Head
    $acceptRanges = [string]$fullResponse.Headers['Accept-Ranges']
    Add-Result 'D2 文件响应声明支持 Range' ($acceptRanges -eq 'bytes') ("Accept-Ranges=" + $acceptRanges)

    # ---------- E. 校验 / 删除保护 ----------
    Write-Host '== 校验接口与删除保护' -ForegroundColor Cyan
    $verify = (Invoke-WebRequest "$baseUrl/api/verify?version=0.10.0" -UseBasicParsing).Content | ConvertFrom-Json
    Add-Result 'E1 校验接口逐文件核对通过' ($verify.passed -and $verify.checkedFiles -eq 231) ("checked=" + $verify.checkedFiles)

    # 400 是预期结果（当前版本不可删），用 -SkipHttpErrorCheck 让脚本拿到响应体自己判断。
    $deleteCurrent = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/delete?version=0.10.0" -Headers @{ 'X-Auth-Token' = $token } -SkipHttpErrorCheck
    Add-Result 'E2 当前发布版本不可删除' (-not $deleteCurrent.success) $deleteCurrent.message

    # ---------- F. 回滚 ----------
    Write-Host '== 回滚（发布一个改动过的版本，再回滚到 0.10.0）' -ForegroundColor Cyan
    $tinyRoot = Join-Path $sandbox 'tiny-0.9.0'
    New-Item -ItemType Directory -Path (Join-Path $tinyRoot 'body') -Force | Out-Null
    $tinyFile = Join-Path $tinyRoot 'body/readme.txt'
    Set-Content -Path $tinyFile -Value 'rollback test' -Encoding UTF8
    $tinyManifest = [ordered]@{
        schemaVersion = 1
        generatedAt   = (Get-Date -Format o)
        body          = [ordered]@{
            version = '0.9.0'
            files   = @([ordered]@{ path = 'readme.txt'; size = (Get-Item $tinyFile).Length; sha256 = (Get-FileHash $tinyFile -Algorithm SHA256).Hash.ToLower() })
        }
        content       = $null
        code          = $null
    }
    $tinyManifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $tinyRoot 'manifest.json') -Encoding UTF8

    $tinyZip = Join-Path $sandbox 'package-0.9.0.zip'
    Compress-Archive -Path (Join-Path $tinyRoot '*') -DestinationPath $tinyZip
    $tinyUpload = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/upload?version=0.9.0" -Headers @{ 'X-Auth-Token' = $token } -InFile $tinyZip -ContentType 'application/zip'
    Add-Result 'F1 小版本包导入成功' $tinyUpload.success $tinyUpload.message

    Invoke-RestMethod -Method Post -Uri "$baseUrl/api/publish?version=0.9.0" -Headers @{ 'X-Auth-Token' = $token } | Out-Null
    $rolledStatus = (Invoke-WebRequest "$baseUrl/api/status" -UseBasicParsing).Content | ConvertFrom-Json
    Add-Result 'F2 发布旧版本后当前版本回退' ($rolledStatus.current.version -eq '0.9.0') ("current=" + $rolledStatus.current.version)

    Invoke-RestMethod -Method Post -Uri "$baseUrl/api/publish?version=0.10.0" -Headers @{ 'X-Auth-Token' = $token } | Out-Null
    $restoredStatus = (Invoke-WebRequest "$baseUrl/api/status" -UseBasicParsing).Content | ConvertFrom-Json
    Add-Result 'F3 回滚回 0.10.0 成功' ($restoredStatus.current.version -eq '0.10.0') ("current=" + $restoredStatus.current.version)
}
finally {
    if ($server -and -not $server.HasExited) { $server | Stop-Process -Force }
}

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host
$failedCount = ($results | Where-Object { $_.结果 -eq 'FAIL' }).Count
Write-Host ("总计 {0} 条，失败 {1} 条" -f $results.Count, $failedCount) -ForegroundColor $(if ($failedCount -eq 0) { 'Green' } else { 'Red' })
exit $failedCount
