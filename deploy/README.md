# 专用服务器部署（Linux）

> 本文档给出**可复现**的部署步骤：从构建 Linux 服务器包到云主机上跑起来、
> 玩家用公网 IP 连入、浏览器查看状态页。
> 文中用 `<云主机IP>` 占位——**公网 IP 与 SSH 私钥不入库**（本仓库是公开仓库，
> 真实接入信息记在开发机本地的 `Docs/Local/云主机接入.local.md`）。

## 0. 前置条件

| 项 | 要求 |
| --- | --- |
| 服务器 | Linux x86_64（云主机 / 局域网机器均可），**glibc ≥ 2.34**（Unity 6 的构建要求；低于它见第 3 节末尾的兼容层） |
| 端口 | **UDP 7777**（游戏同步）、**TCP 8080**（状态页）需要在系统防火墙与云防火墙两层都放行 |
| SSH | 能从开发机登录服务器（本文用 `ssh <user>@<云主机IP>` 表示） |
| Unity 侧 | 已安装 **Linux Build Support（Mono）** 模块（Unity Hub → 添加模块） |

## 1. 构建 Linux 服务器包

Unity 菜单：**`RaidDemo/M9/构建专用服务器（Linux）`**
（命令行等价：`unity command --project-path <工程> menu --path "RaidDemo/M9/构建专用服务器（Linux）"`）。

产物在 `Builds/ServerLinux/`：

```text
RaidDemoServer.x86_64        ← 可执行文件
RaidDemoServer_Data/         ← 资源与托管程序集
UnityPlayer.so 等            ← Unity 运行时
```

> 首次为 Linux 构建时编辑器会切换活动平台（重新导入资源），耗时明显更长；
> 构建完成后菜单会自动切回原平台。

## 2. 上传到服务器

把 `Builds/ServerLinux/` 的**全部内容**与 `deploy/server.sh` 一起放到服务器的同一个目录，
例如 `/root/raid-demo/`：

```bash
# 在开发机执行（Windows PowerShell 的 scp 同样可用）
scp -i <私钥路径> -r Builds/ServerLinux/* <user>@<云主机IP>:/root/raid-demo/
scp -i <私钥路径> deploy/server.sh     <user>@<云主机IP>:/root/raid-demo/
```

服务器上确认目录结构：

```bash
ls /root/raid-demo/          # 应看到 RaidDemoServer.x86_64、RaidDemoServer_Data、server.sh
chmod +x /root/raid-demo/server.sh
```

## 3. 放行端口（两层防火墙）

**系统层**（firewalld，宝塔面板守护的机器默认开启）：

```bash
firewall-cmd --permanent --add-port=7777/udp
firewall-cmd --permanent --add-port=8080/tcp
firewall-cmd --reload
firewall-cmd --list-ports
```

**云厂商层**（阿里云轻量 → 防火墙）：在规则列表里**同时**保留/加入
`UDP 7777` 与 `TCP 8080`（以及运维用的 `22`、面板口的 `8888`）。
轻量控制台的规则是"整表保存"的——改的时候别把 SSH 那条挤掉。

### glibc 低于 2.34 时：用 Ubuntu rootfs 兼容层（一条命令）

Unity 6 的 Linux 构建需要 **glibc ≥ 2.34**。若服务器系统更老
（例如 Alibaba Cloud Linux 3 是 2.32），直接运行会报
`GLIBC_2.34 not found`——**不用重装系统**，在 rootfs 里跑即可：

```bash
cd /root/raid-demo
./setup_chroot.sh                     # 一次性：下载 Ubuntu 22.04 base（约 30 MB）并挂载
RAIDDEMO_CHROOT=/root/ubuntu-2204 ./server.sh start
```

原理：rootfs 里的 Ubuntu 22.04 自带 glibc 2.35，`chroot` 进去运行 Unity
时用的是 rootfs 的用户空间、宿主机内核——进程、端口、文件与直接运行完全一样
（`/raid-demo` 是服务器目录的 bind mount，日志与存档仍写在宿主同一位置）。
bind mount 在重启后会消失，但 `server.sh start` 每次都会自动补挂，不需要手工维护。

## 4. 启动 / 停止

```bash
cd /root/raid-demo
./server.sh start     # 后台常驻；等日志出现「已就绪」才算成功
./server.sh status    # PID、端口监听、日志尾部
./server.sh logs 80   # 最近 80 行日志
./server.sh stop      # 优雅停止（超时后强杀）
./server.sh restart
```

可配置项（环境变量，均有默认值）：

```bash
RAIDDEMO_PORT=7777 RAIDDEMO_ROOM=周末车队 RAIDDEMO_GRACE=60 ./server.sh start
```

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `RAIDDEMO_PORT` | 7777 | 游戏同步端口（UDP） |
| `RAIDDEMO_ROOM` | 默认房间 | 房间名（显示在客户端角标与状态页） |
| `RAIDDEMO_SAVE_DIR` | server_saves | 存档目录（`profiles.json` 写在里面） |
| `RAIDDEMO_DASHBOARD_PORT` | 8080 | 状态页端口；0 = 关闭 |
| `RAIDDEMO_GRACE` | 60 | 掉线宽限秒数 |
| `RAIDDEMO_WATCHDOG` | 2.5 | 自愈看门狗秒数；0 = 关闭 |
| `RAIDDEMO_EXTRA_ARGS` | 空 | 追加参数（原样传递） |

### 用 systemd 常驻（可选）

需要开机自启时，把下面的单元写到 `/etc/systemd/system/raid-demo.service`：

```ini
[Unit]
Description=RaidDemo Dedicated Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/root/raid-demo
ExecStart=/root/raid-demo/RaidDemoServer.x86_64 -server -port 7777 -room 默认房间 \
    -saveDir server_saves -dashboardPort 8080 -grace 60 -watchdog 2.5 \
    -batchmode -nographics -logFile /root/raid-demo/server.log
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload
systemctl enable --now raid-demo
systemctl status raid-demo --no-pager
```

> 二选一：systemd 与 `server.sh` 都只是"把进程拉起来"，同时用会让 PID 文件失去意义。

## 5. 验收

**玩家侧**（开发机 / 任意 Windows 机器）：

```powershell
RaidDemo.exe -connect <云主机IP> -port 7777 -nickname 你的昵称 -passphrase 246810 -autoroom
```

**状态页**（浏览器打开，只读）：

```text
http://<云主机IP>:8080/          ← HTML 页面：房间、玩家列表、最近日志、自愈次数
http://<云主机IP>:8080/status.json  ← 同数据的 JSON 版本（脚本用它做验收）
```

**排障常用命令**：

```bash
ss -tuln | grep -E ':(7777|8080)'      # 端口在不在听
tail -n 100 /root/raid-demo/server.log # 服务器日志
curl -s localhost:8080/status.json     # 云主机本机看状态页数据
```

## 6. 存档与备份

- 全部持久化数据在 `server_saves/profiles.json`：**房间共享仓库 + 每个账号的金币 / 任务 / 随身装备**。
- 备份就是复制这一个文件；节流落盘间隔 4 秒，撤离结算与关服各自立刻写一次。
- 客户端本地存档（`%USERPROFILE%\AppData\LocalLow\RaidDemo\...`）与联机进度**互不相通**。

## 7. 常见问题

| 症状 | 先查什么 |
| --- | --- |
| 客户端连不上（超时） | 两层防火墙的 **UDP 7777**；`./server.sh status` 的端口监听 |
| 状态页打不开 | 两层防火墙的 **TCP 8080**；`curl localhost:8080/status.json` 是否本机可通 |
| 日志里 「监听失败：端口可能已被占用」 | 同端口已有另一个进程：`ss -tulnp \| grep 7777` |
| 玩家掉线后房间解散 | 看日志里有没有「自愈」与「宽限」两行（P-51 的自愈正常时会在几秒内恢复） |
