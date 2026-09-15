#!/usr/bin/env bash
# 一次性搭建 Ubuntu 22.04 rootfs（glibc 兼容层），供 server.sh 的 RAIDDEMO_CHROOT 模式使用。
#
# 什么时候需要它：云主机的系统 glibc 低于 Unity 6 的 Linux 构建要求（2.34）时
# （例如 Alibaba Cloud Linux 3 = glibc 2.32），直接运行会报
# "GLIBC_2.34 not found"。rootfs 里的 Ubuntu 22.04 自带 glibc 2.35，
# chroot 进它运行即可，无需重装系统、也不会动主机上的其他数据。
#
# 用法（在服务器上，root）：
#   ./setup_chroot.sh [rootfs 目录]     默认 /root/ubuntu-2204
#   RAIDDEMO_CHROOT=/root/ubuntu-2204 ./server.sh start
#
# 依赖：curl、tar、mount（都是系统自带）。约 30 MB 下载。

set -euo pipefail

CHROOT_DIR="${1:-/root/ubuntu-2204}"
MIRROR="${RAIDDEMO_ROOTFS_MIRROR:-https://mirrors.aliyun.com/ubuntu-cdimage/ubuntu-base/releases/22.04/release/ubuntu-base-22.04-base-amd64.tar.gz}"

if [ "$(id -u)" != "0" ]; then
    echo "需要 root 运行（chroot 与 bind mount 都要 root）。" >&2
    exit 1
fi

if [ -e "$CHROOT_DIR/etc/os-release" ]; then
    echo "rootfs 已存在：$CHROOT_DIR（跳过下载与解包）"
else
    mkdir -p "$CHROOT_DIR"
    tmp="$(mktemp /tmp/ubuntu-base-XXXXXX.tar.gz)"
    echo "下载 rootfs：$MIRROR"
    curl -fL -o "$tmp" "$MIRROR"
    tar -xzf "$tmp" -C "$CHROOT_DIR"
    rm -f "$tmp"
fi

# DNS：chroot 里将来若要 apt 装库需要（当前服务器不需要额外库，留着备用）。
cp -f /etc/resolv.conf "$CHROOT_DIR/etc/resolv.conf" 2>/dev/null || true

# 挂载点：/proc /sys /dev 让 Unity 的运行时探测正常工作。
# --rbind 用于 /dev（它下面还有子挂载）。
for m in proc sys; do
    mountpoint -q "$CHROOT_DIR/$m" || mount --rbind "/$m" "$CHROOT_DIR/$m"
done
mountpoint -q "$CHROOT_DIR/dev" || mount --rbind /dev "$CHROOT_DIR/dev"

# 把本目录（服务器文件所在处）挂到 rootfs 内的 /raid-demo。
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$CHROOT_DIR/raid-demo"
mountpoint -q "$CHROOT_DIR/raid-demo" || mount --bind "$APP_DIR" "$CHROOT_DIR/raid-demo"

echo "完成。验证："
chroot "$CHROOT_DIR" /bin/bash -c 'ldd --version | head -1; echo "内容："; ls /raid-demo | head -5'
echo
echo "启动：RAIDDEMO_CHROOT=$CHROOT_DIR $APP_DIR/server.sh start"
echo "（bind mount 会在重启后消失；server.sh start 每次都会自动补挂，无需手工处理。）"
