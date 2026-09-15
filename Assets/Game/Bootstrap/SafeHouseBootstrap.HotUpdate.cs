using System.Collections;
using RaidDemo.HotUpdate;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋装配根的热更部分：进入场景时用"已下载的内容"解析目录，并在后台检查更新。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么热更挂在安全屋而不是主菜单：</b>安全屋是玩家进入游戏后的第一站
    /// （单机与联机都是），所有系统在这里装配；把内容解析放在这里，
    /// 就能保证"装配用的目录"与"当前生效的内容版本"一致。</para>
    ///
    /// <para><b>两段式：</b></para>
    /// <list type="number">
    /// <item><b>同步段</b>（<see cref="ApplyHotUpdateContent"/>）：用上一次已经下载好的内容解析目录。
    /// 本地文件读取是毫秒级，放在 <c>Awake</c> 里不会卡住启动；</item>
    /// <item><b>异步段</b>（<see cref="RunHotUpdate"/>）：联网检查是否有新内容；
    /// 若本次确实更新了内容，且玩家还在主菜单（没进战局），就重载一次安全屋场景——
    /// 让新内容立刻生效，而不是"下次启动才生效"。</item>
    /// </list>
    ///
    /// <para><b>为什么重载场景而不是热替换：</b>目录被十几处系统在装配时捕获（背包、商店、图鉴……），
    /// 逐个替换等于把"装配顺序"变成运行期可变状态，出错面极大。
    /// 场景重载是最小、最可控的"重新装配"手段，而且此刻玩家正站在主菜单，代价几乎为零。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>本次装配使用的内容版本（用于判断异步更新后是否需要重载）。</summary>
        private string m_HotUpdateVersionAtStartup = string.Empty;

        /// <summary>
        /// 同步段：确保热更已配置，并用本地已有内容解析目录。
        /// </summary>
        private void ApplyHotUpdateContent()
        {
            var source = ResolveUpdateSource();
            HotUpdateRuntime.Configure(source);
            m_HotUpdateVersionAtStartup = HotUpdateRuntime.ContentVersion;

            // 兜底引用来自场景序列化：取不到 Addressables 内容时游戏仍能跑。
            m_ItemCatalog = HotUpdateRuntime.Resolve(m_ItemCatalog, HotUpdateAddresses.ItemCatalog);
            m_PresentationCatalog = HotUpdateRuntime.Resolve(m_PresentationCatalog, HotUpdateAddresses.PresentationCatalog);

            Debug.Log(
                $"[热更] 装配内容：版本 {(string.IsNullOrEmpty(m_HotUpdateVersionAtStartup) ? "（本体自带）" : m_HotUpdateVersionAtStartup)}" +
                $" ｜ 更新源 {(string.IsNullOrEmpty(source) ? "（未配置）" : source)}");
        }

        /// <summary>
        /// 异步段：检查并下载新内容；若内容变化且仍在主菜单，重载场景使其立即生效。
        /// </summary>
        private IEnumerator RunHotUpdate()
        {
            if (string.IsNullOrEmpty(HotUpdateRuntime.Source))
            {
                yield break;
            }

            yield return HotUpdateRuntime.Initialize();

            var versionNow = HotUpdateRuntime.ContentVersion;
            var changed = !string.Equals(versionNow, m_HotUpdateVersionAtStartup, System.StringComparison.Ordinal);
            if (!changed || string.IsNullOrEmpty(versionNow))
            {
                yield break;
            }

            // 只在"还站在主菜单"时重载：进战局/联机中途重载会把玩家踢出当前对局。
            if (IsMultiplayerProcess || (m_Flow != null && m_Flow.State != RaidFlowController.FlowState.MainMenu))
            {
                Debug.Log($"[热更] 内容已更新到 {versionNow}，将在下次进入场景时生效。");
                yield break;
            }

            Debug.Log($"[热更] 内容已更新到 {versionNow}，正在重载安全屋以立即生效…");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>取本次运行应使用的更新源（启动参数优先，其次默认值）。</summary>
        private static string ResolveUpdateSource()
        {
            if (ClientMode.Options != null && !string.IsNullOrEmpty(ClientMode.Options.UpdateSource))
            {
                return ClientMode.Options.UpdateSource;
            }

            return LaunchOptions.DefaultUpdateSource;
        }
    }
}
