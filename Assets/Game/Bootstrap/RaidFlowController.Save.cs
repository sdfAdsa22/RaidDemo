using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的存档部分：读档、自动存档与"强退视同阵亡"。
    /// </summary>
    /// <remarks>
    /// <para><b>存档只存在安全屋与战局边界。</b>战局中途的物品变化不写盘，
    /// 因为那会让"这一局还没结束"的状态混进局外存档，也会给强退刷装备留下空间。</para>
    ///
    /// <para>进入战局时先写一份 <c>raidInProgress = true</c> 的存档；
    /// 正常撤离或阵亡后清除标记。若下次启动读到标记仍在，就按阵亡处理。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        private SaveFileStore m_SaveStore;
        private MetaSaveData m_PendingSave;
        private MetaProgress m_HookedProgress;
        private bool m_RaidInProgress;
        private string m_StartupNotice;

        /// <summary>是否存在可读取的存档文件。</summary>
        public bool HasSave
        {
            get { return m_SaveStore != null && m_SaveStore.Exists; }
        }

        /// <summary>启动时需要告诉玩家的一次性提示（例如强退战局的惩罚）。</summary>
        public string StartupNotice
        {
            get { return m_StartupNotice; }
        }

        /// <summary>
        /// 把读到的存档还原进当前进度。
        /// </summary>
        /// <param name="catalog">场景里的物品目录。</param>
        /// <remarks>
        /// 安全屋与战局场景都会调用它，但只有第一次会真正生效：
        /// 还原之后待处理存档会被清空，后续调用是空操作。
        /// </remarks>
        public void ApplyPendingSave(IItemDefinitionLookup catalog)
        {
            if (m_PendingSave == null)
            {
                // 新游戏没有存档可还原，但物品目录仍然要注入：
                // 任务奖励与上交检查都依赖它。
                Progress.AttachCatalog(catalog);
                HookProgress(Progress);
                return;
            }

            var data = m_PendingSave;
            m_PendingSave = null;
            var restored = MetaSaveMapper.Restore(data, catalog, out var problems);
            if (restored == null)
            {
                for (var i = 0; i < problems.Count; i++)
                {
                    Debug.LogError("[RaidDemo] 读档失败：" + problems[i]);
                }

                Progress.AttachCatalog(catalog);
                HookProgress(Progress);
                return;
            }

            Progress = restored;
            HookProgress(Progress);

            for (var i = 0; i < problems.Count; i++)
            {
                Debug.LogWarning("[RaidDemo] 读档时跳过了内容：" + problems[i]);
            }

            if (data.raidInProgress)
            {
                // 战局中途退出视同阵亡：随身携带物丢失，仓库不受影响。
                Progress.ClearLoadout();
                m_RaidInProgress = false;
                m_StartupNotice = "上一次战局没有正常结束：随身携带的装备与物资已丢失。";
                SaveNow();
            }
        }

        /// <summary>立即写入一份存档。</summary>
        public void SaveNow()
        {
            if (m_SaveStore == null || Progress == null)
            {
                return;
            }

            var data = MetaSaveMapper.Capture(Progress, m_RaidInProgress);
            if (!m_SaveStore.Save(data, out var error))
            {
                Debug.LogWarning("[RaidDemo] 自动存档失败：" + error);
            }
        }

        /// <summary>战局正常结算：清掉"进行中"标记并保存。</summary>
        public void MarkRaidFinished()
        {
            m_RaidInProgress = false;
            SaveNow();
        }

        /// <summary>开始新游戏：清空存档并重新进入安全屋。</summary>
        public void StartNewGame()
        {
            m_SaveStore?.Delete(out _);
            Progress = new MetaProgress();
            HookProgress(Progress);
            m_RaidInProgress = false;
            m_StartupNotice = null;
            SaveNow();

            State = FlowState.SafeHouse;
            HideScreens();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SafeHouseSceneName);
        }

        private void OnApplicationQuit()
        {
            // 战局中途退出时不覆盖开始战局时写入的 "raidInProgress = true"，
            // 否则强退会变成免费保险。
            if (!m_RaidInProgress)
            {
                SaveNow();
            }
        }

        /// <summary>把当前进度挂上自动存档。</summary>
        private void HookProgress(MetaProgress progress)
        {
            if (m_HookedProgress == progress)
            {
                return;
            }

            if (m_HookedProgress != null)
            {
                m_HookedProgress.Changed -= OnProgressChanged;
            }

            m_HookedProgress = progress;
            if (m_HookedProgress != null)
            {
                m_HookedProgress.Changed += OnProgressChanged;
            }
        }

        /// <summary>
        /// 局外数据变化时自动存档。
        /// </summary>
        /// <remarks>
        /// 只在安全屋状态下保存：战局里捡东西、击杀都会触发进度变化，
        /// 但那些内容本来就不该写进局外存档。
        /// </remarks>
        private void OnProgressChanged()
        {
            if (State == FlowState.SafeHouse)
            {
                SaveNow();
            }
        }
    }
}
