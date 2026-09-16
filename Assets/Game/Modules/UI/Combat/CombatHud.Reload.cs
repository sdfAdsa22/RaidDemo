using RaidDemo.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战斗 HUD 的换弹进度条部分。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个文件：</b>单机与联机拿到"换弹进度"的方式完全不同——
    /// 单机读本地武器运行时，联机只能靠服务器事件 + 本地计时。这两条路径放在一起讲才看得懂，
    /// 混在 HUD 的其它刷新逻辑里（弹药、护甲、生命、提示）很容易改坏其中一条。</para>
    /// </remarks>
    public sealed partial class CombatHud
    {
        /// <summary>进度条底与填充。</summary>
        private Image m_ReloadBarFill;

        /// <summary>进度条根节点。</summary>
        private RectTransform m_ReloadBarRoot;

        /// <summary>
        /// 联机换弹：是否正在按"服务器事件 + 本地计时"画进度条。
        /// </summary>
        /// <remarks>
        /// 联机里武器由服务器推进，本地 runtime 永远不会进入"换弹中"，
        /// 因此进度条必须自己计时——否则能换弹、界面却什么都不显示（负责人反馈的问题 8）。
        /// </remarks>
        private bool m_ServerReloadActive;

        /// <summary>联机换弹已过去的时间（秒）。</summary>
        private float m_ServerReloadElapsed;

        /// <summary>联机换弹的总时长（秒）：取自本地已同步的武器规格。</summary>
        private float m_ServerReloadDuration;

        /// <summary>联机换弹时长的下限（秒）：武器规格异常时也保证进度条能画出来。</summary>
        private const float MinServerReloadSeconds = 0.1f;

        /// <summary>刷新换弹进度条。</summary>
        /// <param name="runtime">本地武器运行时（单机路径用它，联机路径不用）。</param>
        private void UpdateReloadBar(WeaponRuntime runtime)
        {
            // 联机：本地不跑换弹，进度按"服务器说开始了" + 武器规格里的时长自行推进。
            if (m_ServerReloadActive)
            {
                m_ServerReloadElapsed += Time.deltaTime;
                if (m_ServerReloadElapsed < m_ServerReloadDuration)
                {
                    SetBarVisible(true);
                    UiFactory.SetBarProgress(
                        m_ReloadBarFill,
                        m_ReloadBarRoot.sizeDelta.x,
                        m_ServerReloadElapsed / m_ServerReloadDuration);
                    return;
                }

                // 兜底：完成事件丢失时也不能让进度条一直转下去。
                m_ServerReloadActive = false;
                SetBarVisible(false);
                return;
            }

            var reloading = runtime.IsReloading;
            SetBarVisible(reloading);
            if (!reloading)
            {
                return;
            }

            UiFactory.SetBarProgress(
                m_ReloadBarFill,
                m_ReloadBarRoot.sizeDelta.x,
                runtime.ReloadProgress01);
        }

        /// <summary>显示或隐藏换弹进度条。</summary>
        private void SetBarVisible(bool visible)
        {
            if (m_ReloadBarRoot != null && m_ReloadBarRoot.gameObject.activeSelf != visible)
            {
                m_ReloadBarRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 联机：服务器通知"开始 / 结束换弹"。
        /// </summary>
        /// <param name="isReloading">是否正在换弹。</param>
        /// <remarks>
        /// 换弹时长是武器规格的一部分（两端同一份内容），因此这里只需要一个"开始了"的信号，
        /// 进度条自己按本机时间推进；服务器随后发来的"结束"会把它收起来。
        /// </remarks>
        public void NotifyReloadStateFromServer(bool isReloading)
        {
            if (!isReloading)
            {
                m_ServerReloadActive = false;
                SetBarVisible(false);
                return;
            }

            var runtime = m_Controller != null ? m_Controller.Runtime : null;
            var seconds = runtime != null ? runtime.Weapon.ReloadSeconds : 0f;

            m_ServerReloadDuration = Mathf.Max(MinServerReloadSeconds, seconds);
            m_ServerReloadElapsed = 0f;
            m_ServerReloadActive = true;
            SetBarVisible(true);
        }
    }
}
