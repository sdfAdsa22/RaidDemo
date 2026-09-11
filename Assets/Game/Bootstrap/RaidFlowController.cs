using RaidDemo.Raid;
using RaidDemo.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局流程：主菜单 ↔ 战局 ↔ 结算。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用「重新加载场景」而不是原地重置状态：</b>
    /// 一局结束后，容器内容、AI 状态、掉落随机数、事件订阅、命令序号全都要回到初始值。
    /// 原地重置意味着要写出「每个系统的复位方法」，而其中任何一个漏掉，
    /// 症状都是「第二局和第一局不一样」——这类问题极难排查，且每次新增系统都要再检查一遍。
    /// 重新加载场景把这批问题一次性消掉：新场景就是新世界。</para>
    ///
    /// <para><b>为什么本类要跨场景存活：</b>它是「下一局要做什么」的唯一记忆。
    /// 场景重载会销毁场景里的一切，因此流程状态、主菜单与结算界面都必须挂在
    /// 这个标记了 DontDestroyOnLoad 的对象上。</para>
    ///
    /// <para>它属于装配层：只有装配层允许同时认识界面、战局结果与场景加载。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RaidFlowController : MonoBehaviour
    {
        /// <summary>当前流程所处的位置。</summary>
        public enum FlowState
        {
            /// <summary>主菜单：战局逻辑不推进，只显示菜单。</summary>
            MainMenu = 0,

            /// <summary>战局进行中。</summary>
            InRaid = 1,

            /// <summary>已结算：战局世界冻结，显示结算面板。</summary>
            Result = 2,
        }

        private static RaidFlowController s_Instance;

        private MainMenuScreen m_MenuScreen;
        private RaidResultScreen m_ResultScreen;

        /// <summary>当前流程状态。</summary>
        public FlowState State { get; private set; } = FlowState.MainMenu;

        /// <summary>
        /// 取得（必要时创建）唯一的流程控制器。
        /// </summary>
        /// <remarks>
        /// 由场景启动对象在初始化最开始调用。若已存在则直接返回，
        /// 因此每个新加载的场景都会接管同一份流程状态，而不是各自新建一个。
        /// </remarks>
        public static RaidFlowController Ensure()
        {
            if (s_Instance != null)
            {
                return s_Instance;
            }

            var host = new GameObject("RaidFlow");
            s_Instance = host.AddComponent<RaidFlowController>();
            DontDestroyOnLoad(host);
            return s_Instance;
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            m_MenuScreen = gameObject.AddComponent<MainMenuScreen>();
            m_MenuScreen.Initialize(StartRaid);

            m_ResultScreen = gameObject.AddComponent<RaidResultScreen>();
            m_ResultScreen.Initialize(StartRaid, ReturnToMenu);
        }

        /// <summary>
        /// 进入主菜单（当前场景就地显示菜单）。
        /// </summary>
        /// <remarks>
        /// 与 StartRaid 不同，本方法不重新加载场景：
        /// 场景刚加载完时战局本来就没有开始，再重载一次只会多一次无意义的黑屏。
        /// </remarks>
        public void ShowMainMenu()
        {
            State = FlowState.MainMenu;
            Time.timeScale = 0f;
            m_ResultScreen.SetVisible(false);
            m_MenuScreen.SetVisible(true);
            UnlockCursor();
        }

        /// <summary>开始一局：重载场景，让新场景以「战局进行中」的状态启动。</summary>
        public void StartRaid()
        {
            State = FlowState.InRaid;
            HideScreens();
            ReloadScene();
        }

        /// <summary>返回主菜单：重载场景，让新场景以主菜单状态启动。</summary>
        public void ReturnToMenu()
        {
            State = FlowState.MainMenu;
            ReloadScene();
        }

        /// <summary>显示结算界面并冻结战局世界。</summary>
        /// <param name="result">战局结算数据。</param>
        /// <remarks>
        /// 冻结世界用 <c>Time.timeScale</c> 而不是逐个停掉系统：
        /// 结算画面背后应该是玩家阵亡或撤离那一刻的静止画面，
        /// 让敌人继续跑动会让「战局已经结束」这件事显得含糊。
        /// </remarks>
        public void ShowResult(RaidResult result)
        {
            State = FlowState.Result;
            m_MenuScreen.SetVisible(false);
            m_ResultScreen.Show(result);
            Time.timeScale = 0f;
            UnlockCursor();
        }

        /// <summary>隐藏全部流程界面（进入战局时调用）。</summary>
        public void HideScreens()
        {
            m_MenuScreen.SetVisible(false);
            m_ResultScreen.SetVisible(false);
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 重新加载当前场景。
        /// </summary>
        /// <remarks>
        /// <para>优先用 buildIndex 而不是场景名：只有在构建列表里登记过的场景才能按名字加载，
        /// 而按索引加载对「在编辑器里直接打开场景并播放」同样有效。</para>
        ///
        /// <para>加载前把时间恢复为 1：结算界面把 timeScale 设为 0 之后若忘记恢复，
        /// 下一局会以「时间静止」启动，表现为角色不动、敌人不动，而输入看起来完全正常。</para>
        /// </remarks>
        private void ReloadScene()
        {
            Time.timeScale = 1f;

            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(scene.name))
            {
                SceneManager.LoadScene(scene.name);
                return;
            }

            Debug.LogError("[RaidDemo] 无法重新加载场景：当前场景既没有构建索引也没有名称。");
        }

        /// <summary>解锁并显示鼠标：菜单与结算都需要用鼠标点按钮。</summary>
        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
