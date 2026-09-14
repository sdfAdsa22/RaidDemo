using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的联机场景切换部分：开局加载地图、战后回安全屋、进房落到安全屋。
    /// </summary>
    /// <remarks>
    /// <para>从 <c>RaidFlowController.Multiplayer</c> 拆出来的原因有两个：那个文件本来就在 400 行上限附近，
    /// 而且"界面状态映射"与"什么时候换哪张场景"是两件改动原因完全不同的事——
    /// 界面会随 UX 调整，场景切换只随流程变化（P4.5-b 新增的正是后者）。</para>
    ///
    /// <para><b>共同的约定：</b>场景切换只在这里发生，而且**只由服务器的通知驱动**
    /// （单机出击除外）。客户端自己决定进图会出现"有人已经在地图上、房间却还在等待"的分裂状态。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        /// <summary>
        /// 服务器通知开局：收起界面并加载地图。
        /// </summary>
        /// <param name="mapSceneName">服务器指定的地图场景名。</param>
        private void OnRaidStarting(string mapSceneName)
        {
            if (string.IsNullOrEmpty(mapSceneName))
            {
                Debug.LogError("[联机] 服务器通知开局，但没有给出地图名。");
                return;
            }

            // 先切回"战局中"的流程状态再加载场景：加载过程中会有一帧空窗，
            // 此时若流程还停在安全屋，玩家会看到安全屋的操作提示与光标行为。
            State = FlowState.InRaid;
            m_MultiplayerScreen.SetVisible(false);
            m_LobbyScreen.SetVisible(false);
            m_MultiplayerUiActive = false;
            Time.timeScale = 1f;

            if (SceneManager.GetActiveScene().name == mapSceneName)
            {
                return;
            }

            Debug.Log($"[联机] 加载战局地图：{mapSceneName}");
            SceneManager.LoadScene(mapSceneName);
        }

        /// <summary>
        /// 服务器通知"本局结束"：全员回到共享安全屋（P4.5-b 的战后回屋循环）。
        /// </summary>
        /// <param name="sceneName">要返回的场景名；空则用安全屋。</param>
        /// <remarks>
        /// <para>与开局对称：这边只负责切场景，世界的权威内容（谁在屋里、站在哪）
        /// 全部由服务器在切图时重建。</para>
        ///
        /// <para><b>结算面板要留着：</b>玩家可能正在看自己的战局结算。
        /// 因此这里只加载场景、不动流程状态；面板上的「回到安全屋」按钮随后把状态切回安全屋，
        /// 而那时场景已经是安全屋，不会触发二次加载（见 <c>RaidFlowController.GoToSafeHouse</c>）。</para>
        /// </remarks>
        private void OnRaidEnding(string sceneName)
        {
            var target = string.IsNullOrEmpty(sceneName) ? GameScenes.SafeHouse : sceneName;

            m_MultiplayerScreen.SetVisible(false);
            m_LobbyScreen.SetVisible(false);
            m_MultiplayerUiActive = false;

            if (State != FlowState.Result)
            {
                EnterSafeHouseDirectly();
            }

            if (SceneManager.GetActiveScene().name == target)
            {
                return;
            }

            Debug.Log($"[联机] 本局结束，返回共享安全屋：{target}");
            SceneManager.LoadScene(target);
        }

        /// <summary>
        /// 进入房间之后的场景与界面收尾：收起房间界面、确保玩家真的站在安全屋里。
        /// </summary>
        /// <remarks>
        /// <para>场景加载是兜底而不是主路径：从主菜单点「联机」时玩家本来就在安全屋场景里，
        /// 只有"从别处进房"（例如在战局里退回大厅后重新建房、或编辑器直接 Play 了地图场景）
        /// 才需要真的把安全屋加载出来。</para>
        ///
        /// <para>结算面板正在显示时不改流程状态：那说明这位玩家刚打完一局，
        /// 面板要留给他自己关掉。</para>
        /// </remarks>
        private void EnterSafeHouseFromRoom()
        {
            m_MultiplayerScreen.SetVisible(false);
            m_LobbyScreen.SetVisible(false);
            m_MultiplayerUiActive = false;

            if (State != FlowState.Result && State != FlowState.InRaid)
            {
                EnterSafeHouseDirectly();
            }

            if (SceneManager.GetActiveScene().name != GameScenes.SafeHouse && State != FlowState.Result)
            {
                Debug.Log("[联机] 进入房间，加载共享安全屋场景。");
                SceneManager.LoadScene(GameScenes.SafeHouse);
            }
        }
    }
}
