using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的角色选择接线：启动时应用存档角色，衣柜打开界面后立即换装。
    /// </summary>
    /// <remarks>
    /// 逻辑放在 partial 文件里，避免把接近 400 行的主文件继续撑大；
    /// 角色目录来自表现层目录资产，场景启动时注入到流程控制器。
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>打开角色选择界面；由更衣镜调用。</summary>
        public void OpenCharacterSelect()
        {
            var flow = m_Flow != null ? m_Flow : RaidFlowController.Ensure();
            m_Ui?.SetAuxiliaryPanelOpen(true);
            flow.ShowCharacterSelect(_ => ApplySelectedCharacter(), OnCharacterSelectClosed);
        }

        /// <summary>应用当前存档里选择的角色到安全屋玩家。</summary>
        public void ApplySelectedCharacter()
        {
            if (m_PlayerMotor == null || m_PresentationCatalog == null)
            {
                return;
            }

            var flow = m_Flow != null ? m_Flow : RaidFlowController.Ensure();
            var id = flow.Progress != null ? flow.Progress.SelectedCharacterId : null;
            var prefab = m_PresentationCatalog.FindCharacterPrefab(id);
            if (prefab == null)
            {
                return;
            }

            var view = m_PlayerMotor.GetComponent<PlayerCharacterView>();
            view?.SetCharacter(prefab);
        }

        private void Start()
        {
            // Start 在所有 Awake 之后执行：此时存档、表现层目录与流程控制器都已就绪。
            var flow = m_Flow != null ? m_Flow : RaidFlowController.Ensure();
            flow.SetCharacterCatalog(m_PresentationCatalog);
            ApplySelectedCharacter();
        }

        private void OnCharacterSelectClosed()
        {
            m_Ui?.SetAuxiliaryPanelOpen(false);
        }
    }
}
