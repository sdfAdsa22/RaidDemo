using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 编辑器里的联机客户端入口：把自己当成一个客户端去连本机服务器。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>联机验收要开两个客户端，而"改参数 → 出包 → 起进程"每验证一次要一分钟。
    /// 用编辑器当其中一个客户端、再配一个 <c>RaidDemo.exe -server</c>，
    /// 就能直接看效果、打断点、查状态。</para>
    ///
    /// <para>它只设置启动参数并进入播放模式，走的是与命令行完全相同的代码路径，
    /// 因此它验证不到的环节也不会被它掩盖。</para>
    /// </remarks>
    public static class ClientPlayMenu
    {
        /// <summary>默认连接的地址：本机服务器的标准端口。</summary>
        private const string DefaultAddress = "127.0.0.1";

        /// <summary>默认端口。</summary>
        private const int DefaultPort = 7777;

        /// <summary>
        /// 连接参数的 EditorPrefs 键（P6 起）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么可覆盖：</b>全流程验收要连的不一定是本机服务器——例如连云主机。
        /// 把地址写死在代码里会让"连云上环境验收"变成一次改代码 + 提交；
        /// 而写进仓库又会把公网 IP 带进公开仓库。EditorPrefs 存在本机、不进版本库，
        /// 正好落在两者之间。</para>
        ///
        /// <para>设置方式（编辑器命令行）：
        /// <c>unity command --project-path &lt;工程&gt; eval --code "UnityEditor.EditorPrefs.SetString(\"RaidDemo.EditorConnect.Address\", \"&lt;服务器地址&gt;\")"</c>。
        /// 不设置时全部走上面的默认值（本机 7777）。</para>
        /// </remarks>
        public const string AddressPrefKey = "RaidDemo.EditorConnect.Address";

        /// <summary>端口覆盖键。</summary>
        public const string PortPrefKey = "RaidDemo.EditorConnect.Port";

        /// <summary>昵称覆盖键。</summary>
        public const string NicknamePrefKey = "RaidDemo.EditorConnect.Nickname";

        /// <summary>口令覆盖键。</summary>
        public const string PassphrasePrefKey = "RaidDemo.EditorConnect.Passphrase";

        /// <summary>安全屋场景：联机流程的起点（大厅界面叠在它上面）。</summary>
        private const string SafeHouseScenePath = "Assets/Game/Content/Scenes/SafeHouse.unity";

        /// <summary>编辑器里的玩家昵称。与命令行客户端同时上线时要换一个名字。</summary>
        private const string EditorNickname = "编辑器玩家";

        /// <summary>
        /// 以联机客户端身份进入播放模式，连接本机 7777 端口的服务器。
        /// </summary>
        /// <remarks>
        /// <para><b>P4 起从安全屋进入</b>：客户端先在大厅登录、自动进房（<c>-autoroom</c>），
        /// 等服务器开局后才切到地图场景——与真实玩家的路径完全一致。
        /// 上一版直接打开战局场景，那种做法绕过了大厅，P4 之后连上的玩家不会被放进战局世界。</para>
        /// </remarks>
        [MenuItem("RaidDemo/M9/以联机客户端进入（本机 127.0.0.1）")]
        public static void PlayAsLocalClient()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[联机] 已在播放模式中，请先停止再执行本菜单。");
                return;
            }

            var address = EditorPrefs.GetString(AddressPrefKey, DefaultAddress);
            if (string.IsNullOrWhiteSpace(address))
            {
                address = DefaultAddress;
            }

            var port = EditorPrefs.GetInt(PortPrefKey, DefaultPort);
            var nickname = EditorPrefs.GetString(NicknamePrefKey, EditorNickname);
            var passphrase = EditorPrefs.GetString(PassphrasePrefKey, "123456");

            var arguments = new[]
            {
                "-connect", address,
                "-port", port.ToString(),
                "-map", "GreyboxRaid",
                "-nickname", nickname,
                "-passphrase", passphrase,
                "-autoroom",
            };

            if (!LaunchOptions.TryParse(arguments, out var options, out var error))
            {
                Debug.LogError($"[联机] 启动参数解析失败：{error}");
                return;
            }

            ClientMode.Activate(options);

            // 从安全屋开始：大厅会话在这里建立，地图由服务器开局通知驱动加载。
            EditorSceneManager.OpenScene(SafeHouseScenePath);
            EditorApplication.isPlaying = true;

            Debug.Log(
                $"[联机] 以客户端身份进入，连接 {address}:{port}（请先启动服务器），昵称「{nickname}」。");
        }
    }
}
