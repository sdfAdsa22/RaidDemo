using System;
using System.Collections.Generic;
using RaidDemo.Kernel;

namespace RaidDemo.Bootstrap
{
    public sealed partial class LaunchOptions
    {

        /// <summary>
        /// 解析命令行参数。
        /// </summary>
        /// <param name="args">命令行参数（通常是 <see cref="Environment.GetCommandLineArgs"/>）。</param>
        /// <param name="options">解析结果。解析失败时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>参数合法时返回 true。非服务器启动同样返回 true（只是 <see cref="IsServerRequested"/> 为 false）。</returns>
        /// <remarks>
        /// 只有「本类认识的参数取值非法」才算失败：端口不是数字、存档目录是绝对路径等。
        /// 完全不认识的参数一律忽略——引擎会往命令行里塞大量自有参数。
        /// </remarks>
        public static bool TryParse(string[] args, out LaunchOptions options, out string error)
        {
            options = null;
            error = null;

            var result = new LaunchOptions();
            var list = args ?? Array.Empty<string>();

            for (var i = 0; i < list.Length; i++)
            {
                var arg = list[i];
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                switch (arg)
                {
                    case "-server":
                    case "--server":
                        result.IsServerRequested = true;
                        break;

                    case "-batchmode":
                    case "-nographics":
                        result.IsHeadless = true;
                        break;

                    case "-autowalk":
                        result.AutoWalk = true;
                        break;

                    case "-spawnzone":
                        result.SpawnAtExtraction = true;
                        break;

                    case "-downtest":
                        result.DownTest = true;
                        break;

                    case "-rescueonly":
                        result.RescueOnly = true;
                        break;

                    case "-port":
                        if (!TryReadValue(list, ref i, arg, out var portText, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
                        {
                            error = $"参数 {arg} 需要 1~65535 之间的端口号，实际收到「{portText}」。";
                            return false;
                        }

                        result.Port = port;
                        break;

                    case "-room":
                        if (!TryReadValue(list, ref i, arg, out var roomText, out error))
                        {
                            return false;
                        }

                        var room = roomText.Trim();
                        if (room.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空。";
                            return false;
                        }

                        if (room.Length > MaxRoomNameLength)
                        {
                            error = $"参数 {arg} 最长 {MaxRoomNameLength} 个字符，实际 {room.Length} 个。";
                            return false;
                        }

                        result.RoomName = room;
                        break;

                    case "-saveDir":
                        if (!TryReadValue(list, ref i, arg, out var saveDir, out error))
                        {
                            return false;
                        }

                        if (!IsRelativeDirectory(saveDir))
                        {
                            error = $"参数 {arg} 只接受相对路径（不得以盘符、斜杠开头，也不得包含 .. ），实际收到「{saveDir}」。";
                            return false;
                        }

                        result.SaveDirectory = saveDir;
                        break;

                    case "-connect":
                        if (!TryReadValue(list, ref i, arg, out var connectAddress, out error))
                        {
                            return false;
                        }

                        var address = connectAddress.Trim();
                        if (address.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空。";
                            return false;
                        }

                        result.ConnectAddress = address;
                        break;

                    case "-updatesource":
                        if (!TryReadValue(list, ref i, arg, out var updateSource, out error))
                        {
                            return false;
                        }

                        var source = updateSource.Trim().TrimEnd('/');
                        if (source.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空。";
                            return false;
                        }

                        result.UpdateSource = source;
                        break;

                    case "-map":
                        if (!TryReadValue(list, ref i, arg, out var mapScene, out error))
                        {
                            return false;
                        }

                        var scene = mapScene.Trim();
                        if (scene.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空。";
                            return false;
                        }

                        result.MapSceneName = scene;
                        break;

                    case "-logLevel":
                        if (!TryReadValue(list, ref i, arg, out var levelText, out error))
                        {
                            return false;
                        }

                        if (!TryParseLogLevel(levelText, out var level))
                        {
                            error = $"参数 {arg} 需要 verbose / info / warning / error 之一，实际收到「{levelText}」。";
                            return false;
                        }

                        result.MinimumLogLevel = level;
                        break;

                    case "-nickname":
                        if (!TryReadValue(list, ref i, arg, out var nickname, out error))
                        {
                            return false;
                        }

                        var trimmedNickname = nickname.Trim();
                        if (!LobbyLimits.IsValidNickname(trimmedNickname))
                        {
                            error =
                                $"参数 {arg} 需要 1~{LobbyLimits.MaxNicknameLength} 个字符，且不能包含竖线或控制字符，" +
                                $"实际收到「{nickname}」。";
                            return false;
                        }

                        result.Nickname = trimmedNickname;
                        break;

                    case "-passphrase":
                        if (!TryReadValue(list, ref i, arg, out var passphrase, out error))
                        {
                            return false;
                        }

                        if (!LobbyLimits.IsValidPassphrase(passphrase))
                        {
                            error = $"参数 {arg} 需要 {LobbyLimits.MinPassphraseDigits}~{LobbyLimits.MaxPassphraseDigits} 位数字。";
                            return false;
                        }

                        result.Passphrase = passphrase;
                        break;

                    case "-buildid":
                        if (!TryReadValue(list, ref i, arg, out var buildId, out error))
                        {
                            return false;
                        }

                        var trimmedBuildId = buildId.Trim();
                        if (trimmedBuildId.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空（形如 0.10.0+c6f6221e7）。";
                            return false;
                        }

                        if (trimmedBuildId.Length > BuildIdentity.MaxLength)
                        {
                            error = $"参数 {arg} 最长 {BuildIdentity.MaxLength} 个字符（协议字段上限），"
                                + $"实际 {trimmedBuildId.Length} 个。";
                            return false;
                        }

                        result.BuildIdOverride = trimmedBuildId;
                        break;

                    case "-roompass":
                        if (!TryReadValue(list, ref i, arg, out var roomPass, out error))
                        {
                            return false;
                        }

                        if (!LobbyLimits.IsValidRoomPassword(roomPass))
                        {
                            error = $"参数 {arg} 需要留空或 {LobbyLimits.RoomPasswordDigits} 位数字。";
                            return false;
                        }

                        result.RoomPassword = roomPass;
                        break;

                    case "-autostart":
                        if (!TryReadValue(list, ref i, arg, out var autoStartText, out error))
                        {
                            return false;
                        }

                        if (!float.TryParse(autoStartText, out var autoStart) || autoStart < 0f || autoStart > 600f)
                        {
                            error = $"参数 {arg} 需要 0~600 之间的秒数，实际收到「{autoStartText}」。";
                            return false;
                        }

                        result.AutoStartSeconds = autoStart;
                        break;

                    case "-dashboardPort":
                        if (!TryReadValue(list, ref i, arg, out var dashboardText, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(dashboardText, out var dashboardPort) || dashboardPort < 0 || dashboardPort > 65535)
                        {
                            error = $"参数 {arg} 需要 0~65535 之间的端口号（0 表示关闭），实际收到「{dashboardText}」。";
                            return false;
                        }

                        result.DashboardPort = dashboardPort;
                        break;

                    case "-discoveryPort":
                        if (!TryReadValue(list, ref i, arg, out var discoveryText, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(discoveryText, out var discoveryPort) || discoveryPort < 0 || discoveryPort > 65535)
                        {
                            error = $"参数 {arg} 需要 0~65535 之间的端口号（0 表示关闭），实际收到「{discoveryText}」。";
                            return false;
                        }

                        result.DiscoveryPort = discoveryPort;
                        break;

                    case "-grace":
                        if (!TryReadValue(list, ref i, arg, out var graceText, out error))
                        {
                            return false;
                        }

                        if (!float.TryParse(
                                graceText,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var graceSeconds)
                            || graceSeconds < LaunchOptions.MinReconnectGraceSeconds
                            || graceSeconds > LaunchOptions.MaxReconnectGraceSeconds)
                        {
                            error = $"参数 {arg} 需要 {LaunchOptions.MinReconnectGraceSeconds:F0}~"
                                    + $"{LaunchOptions.MaxReconnectGraceSeconds:F0} 秒之间的时长，实际收到「{graceText}」。";
                            return false;
                        }

                        result.ReconnectGraceSeconds = graceSeconds;
                        break;

                    case "-watchdog":
                        if (!TryReadValue(list, ref i, arg, out var watchdogText, out error))
                        {
                            return false;
                        }

                        if (!float.TryParse(
                                watchdogText,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var watchdogSeconds)
                            || (watchdogSeconds != 0f
                                && (watchdogSeconds < LaunchOptions.MinTransportWatchdogSeconds
                                    || watchdogSeconds > LaunchOptions.MaxTransportWatchdogSeconds)))
                        {
                            error = $"参数 {arg} 需要 0（关闭）或 {LaunchOptions.MinTransportWatchdogSeconds:F0}~"
                                    + $"{LaunchOptions.MaxTransportWatchdogSeconds:F0} 秒之间的时长，实际收到「{watchdogText}」。";
                            return false;
                        }

                        result.TransportWatchdogSeconds = watchdogSeconds;
                        break;

                    case "-autoroom":
                        result.AutoRoom = true;
                        break;

                    case "-autotrade":
                        result.AutoTrade = true;
                        break;
                }
            }

            if (!result.IsServerRequested && result.IsHeadless)
            {
                result.m_Warnings.Add("检测到无头启动参数，但缺少 -server：本进程仍按客户端模式初始化。");
            }

            options = result;
            return true;
        }
    }
}
