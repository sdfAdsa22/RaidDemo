using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的收集图鉴接线：创建界面、处理「图鉴展示板」的开关，并维护墙上展示牌的进度文字。
    /// </summary>
    /// <remarks>
    /// <para>图鉴只在安全屋开放（负责人 2026-09-13 决定）：它是"盘点家底"的界面，
    /// 放进战局会变成干扰交火的第四块面板。</para>
    ///
    /// <para>墙上的进度牌与界面共用 <see cref="CodexScreenController.CountDiscovered"/> 的统计，
    /// 因此不必翻开界面就能看到"还差几件"。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        private CodexScreenController m_CodexScreen;

        /// <summary>
        /// 图鉴展示板上的进度显示组件。由场景生成器写入（见 <c>SafeHouseSceneBuilder</c>）。
        /// </summary>
        [SerializeField] private CodexBoardView m_CodexBoard;

        /// <summary>创建图鉴界面并刷新一次墙上的进度牌。由主文件在背包装配之后调用。</summary>
        private void InitializeCodex()
        {
            var host = new GameObject("CodexScreen");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_CodexScreen = host.AddComponent<CodexScreenController>();
            m_CodexScreen.Initialize(
                m_ItemCatalog,
                m_Progress != null ? m_Progress.Codex : null,
                OnCodexClosed);

            if (m_Progress != null)
            {
                m_Progress.Changed += RefreshCodexBoardText;
            }

            RefreshCodexBoardText();
        }

        /// <summary>打开图鉴；由「图鉴展示板」调用。</summary>
        private void OpenCodex()
        {
            if (m_CodexScreen == null)
            {
                return;
            }

            // 与商人界面同一条规则：打开前关掉背包界面并停掉它的输入，
            // 避免两个全屏面板叠在一起、同一次点击被两边各处理一次。
            m_InventoryScreen?.Close();
            if (m_InventoryScreen != null)
            {
                m_InventoryScreen.InputEnabled = false;
            }

            m_Ui?.SetAuxiliaryPanelOpen(true);
            m_CodexScreen.Open();
        }

        /// <summary>图鉴关闭后的收尾：恢复背包输入与安全屋 HUD 状态。</summary>
        private void OnCodexClosed()
        {
            m_Ui?.SetAuxiliaryPanelOpen(false);
            if (m_InventoryScreen != null)
            {
                m_InventoryScreen.InputEnabled = true;
            }
        }

        /// <summary>把最新收集进度写进墙上展示牌。局外任何变化都会触发一次。</summary>
        private void RefreshCodexBoardText()
        {
            m_CodexBoard?.Refresh(
                m_ItemCatalog,
                m_Progress != null ? m_Progress.Codex : null);
        }
    }
}
