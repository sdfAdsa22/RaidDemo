# RaidDemo 更新源服务端（M10 批次 3）

把更新源目录托管出去的独立程序，附一个 Web 可视化面板：看当前版本、上传新版本包、
发布 / 回滚、逐文件校验、看访问日志。协议与批次计划见
[`Docs/Modules/11_热更新与分发.md`](../../Docs/Modules/11_热更新与分发.md) 第 3~4 章。

## 1. 为什么是独立程序

它要同时跑在**开发机**（本机更新源）与**云主机**（对外分发）上，而且必须能脱离 Unity 工作。
用 `HttpListener` 而不是 ASP.NET Core，是为了不引入额外依赖、自包含单文件体积可控；
面板是内嵌的单页 HTML，不需要前端构建链——**别人克隆仓库后一条命令就能跑起来**是本项目的硬要求。

与 Unity 侧共用同一份协议实现（`<Compile Include>` 链接编译，不复制）：

```text
Assets/Game/Kernel/Pure/Updates/UpdateManifest.cs   ← 清单模型
Assets/Game/Kernel/Pure/Updates/UpdateFileHash.cs   ← SHA-256 工具
```

## 2. 构建与发布

```bash
# 本机调试（输出到 bin/Release/net8.0/win-x64/）
dotnet build Tools/UpdateSource/RaidDemo.UpdateSource.csproj -c Release

# 云主机用：Linux 自包含单文件（目标机器不需要装 .NET）
dotnet publish Tools/UpdateSource/RaidDemo.UpdateSource.csproj -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o Builds/Tools/UpdateSource-linux

# Windows 自包含单文件（本机演示也能用）
dotnet publish Tools/UpdateSource/RaidDemo.UpdateSource.csproj -c Release -r win-x64 \
    --self-contained true -p:PublishSingleFile=true -o Builds/Tools/UpdateSource-win
```

## 3. 运行

```bash
# 本机（Windows 默认只监听 127.0.0.1：绑定通配地址需要管理员权限或 URL 预留）
RaidDemo.UpdateSource --root <更新源目录> --port 8090 --token <写操作口令>

# 云主机（Linux 默认监听全部地址）
RAIDDEMO_UPDATE_TOKEN=<口令> ./updatesource.sh start
```

| 参数 | 说明 |
| --- | --- |
| `--root <目录>` | 更新源根目录（含 `manifest.json` 与 `versions/`）；默认 `./update-source` |
| `--port <端口>` | 监听端口，默认 8090 |
| `--host <地址>` | 监听地址；Windows 默认 `127.0.0.1`，Linux 默认 `+` |
| `--token <口令>` | 写操作口令；也可用环境变量 `RAIDDEMO_UPDATE_TOKEN`；缺省时自动生成并打印 |
| `--readonly` | 只读模式：只托管下载，关闭上传 / 发布 / 删除 |

## 4. 接口

| 方法 | 路径 | 口令 | 说明 |
| --- | --- | --- | --- |
| GET | `/` | 否 | 管理面板 |
| GET | `/health` | 否 | 存活探测（`ok`） |
| GET | `/api/status` | 否 | 当前版本 + 版本列表 + 磁盘余量 |
| GET | `/api/log` | 否 | 最近 50 条访问日志 |
| GET | `/api/verify?version=` | 否 | 逐文件重算 SHA-256 并与清单比对 |
| GET | `/manifest.json` | 否 | 当前发布清单（**no-store**，否则客户端永远看不到更新） |
| GET | `/body/…`、`/content/…`、`/code/…` | 否 | 按当前发布版本解析的文件下载，支持 `Range` |
| GET | `/versions/<版本>/…` | 否 | 磁盘上的原始文件（校验与排障用） |
| POST | `/api/upload?version=[&overwrite=true]` | 是 | 上传版本包（原始 zip 字节流） |
| POST | `/api/publish?version=` | 是 | 发布 / 回滚到指定版本 |
| POST | `/api/delete?version=` | 是 | 删除版本（当前发布版本受保护） |

口令通过请求头 `X-Auth-Token` 传递。

> **命令行小坑**：`HttpListener` 对没有 `Content-Length` 的 POST 直接返回
> `411 Length Required`（该判断发生在业务代码之前）。用 curl 调写接口时请带一个空体，
> 例如 `curl -X POST -H "X-Auth-Token: <口令>" -d '' …`；浏览器（面板）与 PowerShell
> 的 `Invoke-RestMethod` 会自动带上 `Content-Length: 0`，无需特殊处理。

## 5. 更新源目录布局

```text
<root>/
├─ manifest.json                 ← 当前发布清单（"发布"= 用某个版本的清单覆盖它）
└─ versions/<版本>/
   ├─ manifest.json              ← 该版本自己的清单
   └─ body/ · content/ · code/   ← 三层文件
```

服务端的 URL 命名空间是"客户端视角"：`/body/…` 由服务端解析到
`versions/<当前版本>/body/…`，因此**磁盘按版本分目录、客户端只认一个入口**。
这样将来换存储形态（对象存储 / CDN）时，客户端一行都不用改。

## 6. 本机验收

```bash
pwsh -NoProfile -File Tools/UpdateSource/verify/p3_updatesource_check.ps1
```

在沙盒里起真实服务端进程，跑 17 条判据：面板与只读接口、真实版本包上传（逐文件校验）、
导入 ≠ 发布、经 HTTP 全量安装、`Range` 断点续传、校验接口、口令鉴权、当前版本删除保护、
发布 / 回滚。

## 7. 部署到云主机

见 [`deploy/README.md`](../../deploy/README.md)；一键管理脚本是 `deploy/updatesource.sh`
（`start` / `stop` / `status` / `logs`）。需要在**系统防火墙**与**云平台安全组**两层放行
TCP 8090。
