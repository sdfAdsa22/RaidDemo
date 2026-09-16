#!/usr/bin/env bash
# RaidDemo 专用服务器管理脚本（Linux 云主机 / 局域网 Linux 机器共用）。
#
# 用法：
#   ./server.sh start     启动服务器（后台常驻，写 PID 与日志）
#   ./server.sh stop      优雅停止（超时后强杀）
#   ./server.sh restart   重启
#   ./server.sh status    查看状态（PID、端口监听、最近日志行数）
#   ./server.sh logs [N]  查看最近 N 行日志（默认 60）
#
# 可配置项（环境变量，全部有默认值）：
#   RAIDDEMO_PORT            游戏同步端口（UDP，默认 7777）
#   RAIDDEMO_ROOM            房间名（默认「默认房间」）
#   RAIDDEMO_SAVE_DIR        存档目录（默认 server_saves，相对脚本目录）
#   RAIDDEMO_DASHBOARD_PORT  状态页端口（TCP，默认 8080；0 = 关闭）
#   RAIDDEMO_GRACE           掉线宽限秒数（默认 60）
#   RAIDDEMO_WATCHDOG        自愈看门狗秒数（默认 2.5；0 = 关闭）
#   RAIDDEMO_EXTRA_ARGS      追加的启动参数（原样传递）
#   RAIDDEMO_CHROOT          glibc 兼容模式：指向 Ubuntu rootfs（见 setup_chroot.sh）。
#                            设置后服务器在 chroot 里运行——用于"系统 glibc 低于 Unity 6
#                            构建要求（2.34）"的服务器；不设置则直接运行本机二进制。
#
# 示例：
#   RAIDDEMO_ROOM=周末车队 ./server.sh start
#   RAIDDEMO_CHROOT=/root/ubuntu-2204 ./server.sh start
#
# 配置文件（推荐，与 Windows 面板同一份格式）：
#   同目录存在 server.config.json 时，端口 / 房间名 / 存档目录 / 状态页端口等
#   **全部以该文件为准**，脚本不再拼这些参数（游戏侧优先级：命令行 > 配置文件 > 内置默认，
#   因此脚本一传 -port 就会把配置文件压掉——那正是"改了文件却不生效"的经典坑）。
#   RAIDDEMO_EXTRA_ARGS 仍然最后追加，优先级最高，留给临时覆盖用。
#   没有该文件时保持旧行为：由上面的环境变量决定。

set -euo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="${RAIDDEMO_BIN:-$APP_DIR/RaidDemoServer.x86_64}"
PID_FILE="$APP_DIR/server.pid"
CONFIG_FILE="$APP_DIR/server.config.json"
# 日志放在 Logs/ 下：与 Windows 面板使用同一个相对位置，
# "日志在哪"在两个系统上是同一个答案，排障时不用先问"你那边是哪个包"。
LOG_FILE="$APP_DIR/Logs/server.log"

PORT="${RAIDDEMO_PORT:-7777}"
ROOM="${RAIDDEMO_ROOM:-默认房间}"
SAVE_DIR="${RAIDDEMO_SAVE_DIR:-server_saves}"
DASHBOARD_PORT="${RAIDDEMO_DASHBOARD_PORT:-8080}"
GRACE="${RAIDDEMO_GRACE:-60}"
WATCHDOG="${RAIDDEMO_WATCHDOG:-2.5}"
EXTRA_ARGS="${RAIDDEMO_EXTRA_ARGS:-}"
CHROOT_DIR="${RAIDDEMO_CHROOT:-}"

# 从配置文件里取一个数字字段（面板生成的文件是"一行一个字段"，sed 足够；
# 手写文件只要保持同样的写法也能解析）。取不到返回非零，调用方回退环境变量。
config_number() {
    [ -f "$CONFIG_FILE" ] || return 1

    local value
    value="$(sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p" "$CONFIG_FILE" | head -n 1)"
    [ -n "$value" ] || return 1

    printf '%s' "$value"
}

# chroot 模式下，rootfs 内的挂载点（由 setup_chroot.sh 建立，这里做自检与补挂）。
ensure_chroot_mounts() {
    [ -n "$CHROOT_DIR" ] || return 0
    [ -d "$CHROOT_DIR" ] || { echo "找不到 chroot 目录：$CHROOT_DIR（先跑 setup_chroot.sh）" >&2; exit 1; }

    for m in proc sys dev; do
        if ! mountpoint -q "$CHROOT_DIR/$m"; then
            mount --rbind "/$m" "$CHROOT_DIR/$m"
        fi
    done

    if ! mountpoint -q "$CHROOT_DIR/raid-demo"; then
        mkdir -p "$CHROOT_DIR/raid-demo"
        mount --bind "$APP_DIR" "$CHROOT_DIR/raid-demo"
    fi
}

is_running() {
    [ -f "$PID_FILE" ] || return 1
    local pid
    pid="$(cat "$PID_FILE" 2>/dev/null || true)"
    [ -n "$pid" ] || return 1
    kill -0 "$pid" 2>/dev/null
}

start() {
    if is_running; then
        echo "服务器已在运行（PID $(cat "$PID_FILE")）。"
        exit 1
    fi

    if [ ! -x "$BIN" ]; then
        # 从压缩包解出来时常见丢掉执行位：这里补一次，而不是要求人工 chmod。
        if [ -f "$BIN" ]; then
            chmod +x "$BIN"
        else
            echo "找不到服务器可执行文件：$BIN" >&2
            echo "请先把 Linux 服务器构建（Builds/ServerLinux）上传到本目录。" >&2
            exit 1
        fi
    fi

    cd "$APP_DIR"

    ensure_chroot_mounts

    # chroot 模式下日志路径要写成 rootfs 内的视角（/raid-demo 是 APP_DIR 的 bind mount，
    # 与宿主上的同一文件）；直接运行则用宿主路径。
    local log_path="$LOG_FILE"
    if [ -n "$CHROOT_DIR" ]; then
        log_path="/raid-demo/Logs/server.log"
    fi

    # 日志目录要先建出来：Unity 会创建日志"文件"，但不会创建它所在的目录，
    # 目录不存在时现象是"没有日志"，而那正是排查启动失败唯一要看的东西。
    mkdir -p "$APP_DIR/Logs"

    # -batchmode -nographics：无窗口、无图形设备运行（云主机没有显示环境）。
    # 参数用数组承载（房间名等可能带空格，字符串拼接会被再次分词拆坏）。
    local -a app_args=(-server)

    # AR-01：chroot 模式下 -config 必须传 rootfs 内的路径。
    # 直接传宿主路径时 chroot 里看不到文件，服务器读不到配置、启动失败，
    # 甚至会退化成单机客户端（云主机上踩过一次，表现为 7777 不监听）。
    local config_path="$CONFIG_FILE"
    if [ -n "$CHROOT_DIR" ]; then
        config_path="/raid-demo/server.config.json"
    fi

    if [ -f "$CONFIG_FILE" ]; then
        if [ -n "$CHROOT_DIR" ] && ! chroot "$CHROOT_DIR" /bin/bash -c "test -f '$config_path'"; then
            echo "chroot 内看不到配置文件 $config_path" >&2
            echo "请检查 setup_chroot.sh 建立的 bind mount，或先跑一次 /raid-demo/server.sh --check。" >&2
            exit 1
        fi

        app_args+=(-config "$config_path")
    else
        app_args+=(
            -port "$PORT"
            -room "$ROOM"
            -saveDir "$SAVE_DIR"
            -dashboardPort "$DASHBOARD_PORT"
            -grace "$GRACE"
            -watchdog "$WATCHDOG"
        )
    fi

    app_args+=(-batchmode -nographics -logFile "$log_path")

    if [ -n "$EXTRA_ARGS" ]; then
        local -a extra=()
        read -r -a extra <<< "$EXTRA_ARGS"
        app_args+=("${extra[@]}")
    fi

    if [ -n "$CHROOT_DIR" ]; then
        # chroot 模式：日志写 rootfs 内的 /raid-demo/server.log —— 它就是宿主上的同一个文件
        # （/raid-demo 是 APP_DIR 的 bind mount），因此脚本在宿主侧照常 tail。
        # 参数逐个 %q 转义后再交给内层 bash：房间名里的空格与引号都能原样到达。
        local quoted=""
        local arg
        for arg in "${app_args[@]}"; do
            quoted+=" $(printf '%q' "$arg")"
        done

        nohup chroot "$CHROOT_DIR" /bin/bash -c "cd /raid-demo && exec ./RaidDemoServer.x86_64$quoted" \
            >/dev/null 2>&1 &
    else
        nohup "$BIN" "${app_args[@]}" >/dev/null 2>&1 &
    fi

    echo $! > "$PID_FILE"

    # 等它真的在监听再报成功：Unity 进程起来到绑定端口之间有一两秒空窗，
    # 立刻回报"已启动"会让紧接着的客户端连接偶发失败。
    local waited=0
    while [ "$waited" -lt 30 ]; do
        if grep -q "已就绪" "$LOG_FILE" 2>/dev/null; then
            if [ -f "$CONFIG_FILE" ]; then
                echo "服务器已启动（PID $(cat "$PID_FILE")；端口与房间名以 $CONFIG_FILE 为准）。"
            else
                echo "服务器已启动（PID $(cat "$PID_FILE")，UDP $PORT，状态页 TCP $DASHBOARD_PORT）。"
            fi
            echo "日志：$LOG_FILE"
            return 0
        fi
        sleep 1
        waited=$((waited + 1))
    done

    echo "服务器进程已拉起，但 30 秒内没等到「已就绪」日志；请查看 $LOG_FILE。" >&2
    exit 1
}

stop() {
    if ! is_running; then
        echo "服务器未在运行。"
        rm -f "$PID_FILE"
        return 0
    fi

    local pid
    pid="$(cat "$PID_FILE")"
    echo "正在停止（PID $pid）…"
    kill "$pid" 2>/dev/null || true

    local waited=0
    while kill -0 "$pid" 2>/dev/null && [ "$waited" -lt 20 ]; do
        sleep 1
        waited=$((waited + 1))
    done

    if kill -0 "$pid" 2>/dev/null; then
        echo "优雅停止超时，强制结束。"
        kill -9 "$pid" 2>/dev/null || true
    fi

    rm -f "$PID_FILE"
    echo "已停止。"
}

status() {
    if is_running; then
        echo "运行中：PID $(cat "$PID_FILE")"
    else
        echo "未运行。"
    fi

    # 端口以配置文件为准（它优先于环境变量）；取不到才回退。
    # 否则"改完配置再 status"会对着旧端口找监听，得到一句误导人的"未看到监听"。
    local status_port status_dashboard
    status_port="$(config_number port || true)"
    status_port="${status_port:-$PORT}"
    status_dashboard="$(config_number dashboardPort || true)"
    status_dashboard="${status_dashboard:-$DASHBOARD_PORT}"

    echo "--- 端口监听 ---"
    ss -tuln 2>/dev/null | grep -E ":(udp|tcp)" | grep -E ":($status_port|$status_dashboard)\b" \
        || echo "（未看到 $status_port/udp 或 $status_dashboard/tcp 的监听）"

    if [ -f "$LOG_FILE" ]; then
        echo "--- 日志尾部 ---"
        tail -n 5 "$LOG_FILE"
    fi
}

logs() {
    local lines="${1:-60}"
    [ -f "$LOG_FILE" ] || { echo "还没有日志文件。"; exit 1; }
    tail -n "$lines" "$LOG_FILE"
}

case "${1:-}" in
    start) shift || true; start ;;
    stop) shift || true; stop ;;
    restart) shift || true; stop; start ;;
    status) shift || true; status ;;
    logs) shift || true; logs "${1:-60}" ;;
    *)
        echo "用法：$0 {start|stop|restart|status|logs [N]}" >&2
        exit 2
        ;;
esac
