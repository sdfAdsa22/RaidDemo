using RaidDemo.Raid;
using RaidDemo.UI;
using RaidDemo.Meta;
using RaidDemo.Data;
using RaidDemo.Kernel;
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
    public sealed partial class RaidFlowController : MonoBehaviour
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

            /// <summary>安全屋：玩家在局外空间里走动、整理装备、选择地图。</summary>
            SafeHouse = 3,
        }

        private static RaidFlowController s_Instance;

        /// <summary>安全屋场景名。启动与结算后都回到这里。</summary>
        private const string SafeHouseSceneName = "SafeHouse";

        /// <summary>战局场景名。</summary>
        private const string RaidSceneName = "GreyboxRaid";

        private MainMenuScreen m_MenuScreen;
        private RaidResultScreen m_ResultScreen;
        /// <summary>
        /// 局外进度（仓库）。
        /// </summary>
        /// <remarks>
        /// 放在流程控制器上而不是场景里：它标记了 DontDestroyOnLoad，
        /// 而每开一局都会重新加载场景——放在场景里的东西活不过一局。
        /// </remarks>
        public MetaProgress Progress { get; private set; } = new MetaProgress();

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

            // 读档只读原始 DTO，不在这里还原物品：还原需要物品目录资产，
            // 而那个资产属于场景，要等装配层把目录交进来之后才能做。
            m_SaveStore = new SaveFileStore();
            if (m_SaveStore.TryLoad<MetaSaveData>(out var data, out var loadError))
            {
                m_PendingSave = data;
            }
            else if (m_SaveStore.Exists)
            {
                Debug.LogWarning($"[RaidDemo] 存档读取失败：{loadError}");
            }

            m_MenuScreen = gameObject.AddComponent<MainMenuScreen>();
            m_MenuScreen.Initialize(EnterSafeHouse, StartNewGame);

            m_ResultScreen = gameObject.AddComponent<RaidResultScreen>();
            m_ResultScreen.Initialize(GoToSafeHouse);
            HookProgress(Progress);
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
            m_MenuScreen.SetHasSave(HasSave);
            m_MenuScreen.SetNotice(StartupNotice);
            m_MenuScreen.SetVisible(true);
            UnlockCursor();
        }

        /// <summary>开始一局：重载场景，让新场景以「战局进行中」的状态启动。</summary>
        public void StartRaid()
        {
            // 先写入"战局进行中"再切场景：这样中途强退时，下一次启动能判定为阵亡。
            m_RaidInProgress = true;
            SaveNow();
            State = FlowState.InRaid;
            HideScreens();
            Time.timeScale = 1f;
            SceneManager.LoadScene(RaidSceneName);
        }

        /// <summary>
        /// 从主菜单进入安全屋。
        /// </summary>
        /// <remarks>
        /// 不换场景：启动场景就是安全屋，主菜单只是盖在它上面的一层。
        /// 因此「开始」= 把菜单收起来，玩家立刻站在安全屋里。
        /// </remarks>
        public void EnterSafeHouse()
        {
            m_RaidInProgress = false;
            SaveNow();
            State = FlowState.SafeHouse;
            HideScreens();
        }

        /// <summary>
        /// 回到安全屋。
        /// </summary>
        /// <remarks>
        /// 结算之后的去处是安全屋，而不是主菜单：那里才是玩家整理战利品、
        /// 决定下一局带什么的地方。主菜单只在启动时出现一次。
        /// </remarks>
        public void GoToSafeHouse()
        {
            m_RaidInProgress = false;
            SaveNow();
            State = FlowState.SafeHouse;
            HideScreens();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SafeHouseSceneName);
        }

        /// <summary>返回主菜单：重载场景，让新场景以主菜单状态启动。</summary>
        public void ReturnToMenu()
        {
            m_RaidInProgress = false;
            SaveNow();
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
        public void ShowResult(RaidResult result, string questSummary = null)
        {
            State = FlowState.Result;
            m_MenuScreen.SetVisible(false);
            m_ResultScreen.Show(result, questSummary);
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
        /// 单独显示或隐藏主菜单面板。
        /// </summary>
        /// <param name="visible">是否显示。</param>
        /// <remarks>
        /// 用于「出击准备」：玩家在主菜单里按下 Tab 打开仓库整理装备时，
        /// 菜单必须让开，否则它的遮罩会盖在背包面板上（菜单层级 300 &gt; 背包 200）。
        /// 关掉背包后菜单要回来，否则玩家会以为退回主菜单了。
        /// </remarks>
        public void SetMenuVisible(bool visible)
        {
            m_MenuScreen.SetVisible(visible);
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
