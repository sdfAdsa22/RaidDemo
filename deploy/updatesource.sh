#!/usr/bin/env bash
# RaidDemo 更新源服务端管理脚本（Linux 云主机 / 局域网 Linux 机器共用）。
#
# 用法：
#   ./updatesource.sh start     启动（后台常驻，写 PID 与日志）
#   ./updatesource.sh stop      停止
#   ./updatesource.sh restart   重启
#   ./updatesource.sh status    查看状态（PID、端口监听、当前版本）
#   ./updatesource.sh logs [N]  查看最近 N 行日志（默认 60）
#
# 可配置项（环境变量，全部有默认值）：
#   RAIDDEMO_UPDATE_PORT   监听端口（TCP，默认 8090）
#   RAIDDEMO_UPDATE_ROOT   更新源根目录（默认 <脚本目录>/update-source）
#   RAIDDEMO_UPDATE_TOKEN  写操作口令（必填；未设置时启动会失败并提示）
#   RAIDDEMO_UPDATE_HOST   监听地址（默认 + = 全部地址；只想本机可访问时用 127.0.0.1）
#   RAIDDEMO_UPDATE_BIN    可执行文件路径（默认 <脚本目录>/RaidDemo.UpdateSource）
#
# 示例：
#   RAIDDEMO_UPDATE_TOKEN=你的口令 ./updatesource.sh start
#   RAIDDEMO_UPDATE_TOKEN=你的口令 RAIDDEMO_UPDATE_ROOT=/root/raid-demo-updatesource/data ./updatesource.sh start

set -euo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="${RAIDDEMO_UPDATE_BIN:-$APP_DIR/RaidDemo.UpdateSource}"
PID_FILE="$APP_DIR/updatesource.pid"
LOG_FILE="$APP_DIR/updatesource.log"

PORT="${RAIDDEMO_UPDATE_PORT:-8090}"
ROOT_DIR="${RAIDDEMO_UPDATE_ROOT:-$APP_DIR/update-source}"
HOST="${RAIDDEMO_UPDATE_HOST:-+}"
TOKEN="${RAIDDEMO_UPDATE_TOKEN:-}"

is_running() {
    [ -f "$PID_FILE" ] || return 1
    local pid
    pid="$(cat "$PID_FILE" 2>/dev/null || true)"
    [ -n "$pid" ] || return 1
    kill -0 "$pid" 2>/dev/null
}

start() {
    if is_running; then
        echo "更新源已在运行（PID $(cat "$PID_FILE")）。"
        exit 1
    fi

    if [ -z "$TOKEN" ]; then
        echo "缺少写操作口令：请设置 RAIDDEMO_UPDATE_TOKEN（上传 / 发布 / 删除需要它）。" >&2
        exit 1
    fi

    if [ ! -x "$BIN" ]; then
        # 上传后常见丢掉执行位：这里补一次，而不是要求人工 chmod。
        if [ -f "$BIN" ]; then
            chmod +x "$BIN"
        else
            echo "找不到可执行文件：$BIN" >&2
            echo "请先上传发布产物（Tools/UpdateSource 的 linux-x64 自包含单文件）。" >&2
            exit 1
        fi
    fi

    mkdir -p "$ROOT_DIR"

    echo "启动更新源：端口 $PORT，根目录 $ROOT_DIR"
    nohup "$BIN" --root "$ROOT_DIR" --port "$PORT" --host "$HOST" --token "$TOKEN" \
        >> "$LOG_FILE" 2>&1 &
    echo $! > "$PID_FILE"
    sleep 1

    if is_running; then
        echo "已启动（PID $(cat "$PID_FILE")）。面板：http://<公网IP>:$PORT/"
        echo "日志：$LOG_FILE"
    else
        echo "启动失败，请看日志：$LOG_FILE" >&2
        tail -n 20 "$LOG_FILE" >&2 || true
        exit 1
    fi
}

stop() {
    if ! is_running; then
        echo "更新源未在运行。"
        rm -f "$PID_FILE"
        return 0
    fi

    local pid
    pid="$(cat "$PID_FILE")"
    kill "$pid" 2>/dev/null || true

    for _ in $(seq 1 20); do
        kill -0 "$pid" 2>/dev/null || break
        sleep 0.5
    done

    if kill -0 "$pid" 2>/dev/null; then
        echo "进程未响应，强制结束。"
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

    echo "端口监听（$PORT）："
    (ss -lntp 2>/dev/null | grep -E ":$PORT\b") || echo "  （未监听）"

    echo "当前发布版本："
    curl -s --max-time 3 "http://127.0.0.1:$PORT/api/status" | head -c 400 || true
    echo
}

logs() {
    local lines="${1:-60}"
    [ -f "$LOG_FILE" ] && tail -n "$lines" "$LOG_FILE" || echo "暂无日志。"
}

case "${1:-}" in
    start) start ;;
    stop) stop ;;
    restart) stop; start ;;
    status) status ;;
    logs) logs "${2:-60}" ;;
    *)
        echo "用法：$0 {start|stop|restart|status|logs [N]}" >&2
        exit 2
        ;;
esac
