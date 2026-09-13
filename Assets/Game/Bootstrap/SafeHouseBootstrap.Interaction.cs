using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的设施交互：找出最近的设施、显示提示、按 E 分派行为。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因：主文件在 M8 批次 1 加进图鉴分支后超过了 400 行上限。
    /// "设施交互"与主文件里的"输入、相机、流程状态"本来也是两件事。</para>
    ///
    /// <para>行为分派集中在这里的 <see cref="Activate"/>：每种设施只允许有一个入口，
    /// 新增设施时改这一处即可。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>找出最近的可交互设施，并按 E 触发。</summary>
        private void UpdateInteraction(bool uiOpen)
        {
            if (uiOpen || m_PlayerMotor == null)
            {
                m_Nearby = null;
                m_Ui?.SetPrompt(null);
                return;
            }

            var simulated = m_PlayerMotor.SimulatedPosition;
            var position = new Vector2F(simulated.x, simulated.y);
            m_Nearby = FindNearest(position);
            m_Ui?.SetPrompt(m_Nearby != null ? $"按 E 与「{m_Nearby.DisplayName}」交互" : null);

            if (m_Nearby == null || m_InputCollector == null || !m_InputCollector.ReadInteractIntent())
            {
                return;
            }

            Activate(m_Nearby);
        }

        /// <summary>按设施类型分派行为。</summary>
        private void Activate(SafeHouseInteractable target)
        {
            switch (target.Type)
            {
                case SafeHouseInteractable.Kind.Stash:
                    // 打开背包界面，右侧面板显示仓库——与主菜单里的出击准备是同一块界面。
                    m_InventoryScreen?.SetStashContainer(m_StashContainerId);
                    m_InventoryScreen?.OpenStash(m_StashContainerId);
                    break;

                case SafeHouseInteractable.Kind.Exit:
                    m_Ui?.ShowMap();
                    break;

                case SafeHouseInteractable.Kind.Merchant:
                    m_InventoryScreen?.Close();
                    if (m_InventoryScreen != null)
                    {
                        m_InventoryScreen.InputEnabled = false;
                    }

                    m_MerchantScreen?.Open();
                    break;

                case SafeHouseInteractable.Kind.Wardrobe:
                    OpenCharacterSelect();
                    break;

                case SafeHouseInteractable.Kind.CodexBoard:
                    OpenCodex();
                    break;

                default:
                    m_Ui?.ShowHint("任务板：批次 4 开放");
                    break;
            }
        }

        /// <summary>找出生点周围最近、且在交互距离内的设施。</summary>
        private SafeHouseInteractable FindNearest(Vector2F position)
        {
            var all = UnityEngine.Object.FindObjectsByType<SafeHouseInteractable>(
                FindObjectsSortMode.None);
            SafeHouseInteractable nearest = null;
            var best = float.MaxValue;

            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                var dx = candidate.transform.position.x - position.X;
                var dz = candidate.transform.position.z - position.Y;
                var squared = (dx * dx) + (dz * dz);
                if (squared > candidate.RangeMeters * candidate.RangeMeters || squared >= best)
                {
                    continue;
                }

                best = squared;
                nearest = candidate;
            }

            return nearest;
        }
    }
}
