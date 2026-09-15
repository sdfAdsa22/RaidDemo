using RaidDemo.HotUpdate;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局装配根的热更部分：用当前生效的内容解析目录。
    /// </summary>
    /// <remarks>
    /// <para><b>这里只做同步解析、不再发起更新检查：</b>战局是"安全屋之后的第二站"
    /// （或服务器加载的场景），检查更新已经在安全屋做过一次；在战局里再联网检查
    /// 只会在开打前多一次等待，而且万一下载到"另一个版本"还会造成同局内容不一致。</para>
    ///
    /// <para>服务器进程走的是同一条路径：它同样需要一个 <c>ItemCatalog</c> 来做权威判定，
    /// 但服务器的更新源为空（它是版本锚点），因此拿到的就是本体自带内容。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>用当前生效的内容解析目录（本地缓存优先，其次本体自带）。</summary>
        private void ApplyHotUpdateContent()
        {
            var source = ServerMode.IsActive
                ? string.Empty
                : (ClientMode.Options != null && !string.IsNullOrEmpty(ClientMode.Options.UpdateSource)
                    ? ClientMode.Options.UpdateSource
                    : LaunchOptions.DefaultUpdateSource);

            HotUpdateRuntime.Configure(source);

            m_ItemCatalog = HotUpdateRuntime.Resolve(m_ItemCatalog, HotUpdateAddresses.ItemCatalog);
            m_PresentationCatalog = HotUpdateRuntime.Resolve(m_PresentationCatalog, HotUpdateAddresses.PresentationCatalog);
        }
    }
}
