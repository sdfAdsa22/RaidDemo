using RaidDemo.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的暂停菜单部分：暂停、恢复、返回主界面与返回桌面。
    /// </summary>
    /// <remarks>
    /// <para>暂停必须同时冻结时间、释放鼠标并显示菜单；恢复则相反。
    /// 把这三件事放在流程控制器里，界面只负责按钮回调，
    /// 避免"点了继续但 timeScale 没恢复"这类难查的问题。</para>
    ///
    /// <para><b>战局中返回主界面 = 放弃本局。</b>若直接切状态而不处理随身物品，
    /// 玩家就能用 Esc 把本局装备安全带回去，绕过"死亡丢装备"的规则，
    /// 与强退视同阵亡的防刷设计冲突。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        private PauseMenuScreen m_PauseScreen;

        /// <summary>当前是否处于暂停状态。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>创建暂停菜单。由 Awake 调用一次。</summary>
        private void InitializePauseMenu()
        {
            m_PauseScreen = gameObject.AddComponent<PauseMenuScreen>();
            m_PauseScreen.Initialize(ReturnToMainMenuFromPause, QuitApplication);
        }

        /// <summary>
        /// 打开暂停菜单。
        /// </summary>
        /// <param name="warnAbandon">是否显示"返回主界面会丢失随身物品"的警告。</param>
        public void ShowPauseMenu(bool warnAbandon)
        {
            if (IsPaused
                || (State != FlowState.InRaid && State != FlowState.SafeHouse))
            {
                return;
            }

            IsPaused = true;
            Time.timeScale = 0f;
            m_PauseScreen?.Show(warnAbandon);
            UnlockCursor();
        }

        /// <summary>关闭暂停菜单并恢复时间。</summary>
        public void ResumeFromPause()
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            m_PauseScreen?.Hide();
            Time.timeScale = 1f;
        }

        /// <summary>隐藏暂停菜单。用于流程切换（进战局 / 结算 / 回主菜单）。</summary>
        public void HidePauseMenu()
        {
            IsPaused = false;
            m_PauseScreen?.Hide();
        }

        /// <summary>
        /// 从暂停菜单返回主界面。
        /// </summary>
        /// <remarks>
        /// 战局中调用时按"放弃本局"处理：清空随身携带物后再返回菜单；
        /// 安全屋中调用不受影响。返回后统一在安全屋场景显示主菜单。
        /// </remarks>
        private void ReturnToMainMenuFromPause()
        {
            if (State == FlowState.InRaid)
            {
                Progress.ClearLoadout();
            }

            m_RaidInProgress = false;
            SaveNow();
            HidePauseMenu();
            State = FlowState.MainMenu;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SafeHouseSceneName);
        }

        /// <summary>返回桌面。</summary>
        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
