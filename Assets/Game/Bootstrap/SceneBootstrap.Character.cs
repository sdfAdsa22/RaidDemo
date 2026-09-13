using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局启动时的角色应用：按存档里的 selectedCharacterId 替换玩家外观。
    /// </summary>
    /// <remarks>
    /// 战局里不提供角色选择入口，但必须应用安全屋里已经选好的角色；
    /// 否则会出现“安全屋换了人、出击后又变回默认角色”的割裂。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private void Start()
        {
            var flow = RaidFlowController.Ensure();
            flow.SetCharacterCatalog(m_PresentationCatalog);
            ApplySelectedCharacter();
        }

        /// <summary>把当前存档角色应用到战局玩家。</summary>
        public void ApplySelectedCharacter()
        {
            if (m_PlayerMotor == null || m_PresentationCatalog == null)
            {
                return;
            }

            var flow = RaidFlowController.Ensure();
            var id = flow.Progress != null ? flow.Progress.SelectedCharacterId : null;
            var prefab = m_PresentationCatalog.FindCharacterPrefab(id);
            if (prefab == null)
            {
                return;
            }

            var view = m_PlayerMotor.GetComponent<PlayerCharacterView>();
            view?.SetCharacter(prefab);
        }
    }
}
