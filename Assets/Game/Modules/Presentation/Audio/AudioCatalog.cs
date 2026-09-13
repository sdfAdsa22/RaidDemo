using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 音效目录：把「游戏里会响的每一件事」映射到具体的音频剪辑。
    /// </summary>
    /// <remarks>
    /// <para>做成资产而不是在代码里写死路径，理由与物品目录一致：声音属于表现层配置，
    /// 换一条枪声不该重新编译代码；同时资产是一份可被 Inspector 直接检查的清单，
    /// 漏接哪个槽位一眼就能看到（代码里漏掉一个字段通常什么都看不出来）。</para>
    /// <para>所有数组型槽位都允许为空或有多个元素：调用方通过 <c>Pick</c> 系列方法按索引取，
    /// 取不到就返回 null，播放层对 null 静默跳过。**缺素材不应该让游戏报错**——
    /// 别人克隆仓库时少拿了一个音效包，应该只是没声音，而不是玩不了。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "AudioCatalog",
        menuName = "RaidDemo/音效目录",
        order = 21)]
    public sealed class AudioCatalog : ScriptableObject
    {
        [Header("武器")]
        [SerializeField] private AudioClip[] m_RifleShots;
        [SerializeField] private AudioClip[] m_PistolShots;
        [SerializeField] private AudioClip[] m_SmgShots;
        [SerializeField] private AudioClip[] m_ShotgunShots;
        [SerializeField] private AudioClip m_DryFire;
        [SerializeField] private AudioClip m_MagazineOut;
        [SerializeField] private AudioClip m_MagazineIn;
        [SerializeField] private AudioClip m_BoltClose;
        [SerializeField] private AudioClip m_WeaponSwitch;

        [Header("命中")]
        [SerializeField] private AudioClip[] m_ImpactFlesh;
        [SerializeField] private AudioClip[] m_ImpactHard;

        [Header("脚步")]
        [SerializeField] private AudioClip[] m_FootstepGrass;
        [SerializeField] private AudioClip[] m_FootstepHard;

        [Header("搜刮与战局")]
        [SerializeField] private AudioClip m_LootOpen;
        [SerializeField] private AudioClip[] m_LootRummage;
        [SerializeField] private AudioClip m_LootPickup;
        [SerializeField] private AudioClip m_ExtractionTick;
        [SerializeField] private AudioClip m_RaidSuccess;
        [SerializeField] private AudioClip m_RaidFail;

        [Header("界面")]
        [SerializeField] private AudioClip m_UiClick;
        [SerializeField] private AudioClip m_UiPanelOpen;
        [SerializeField] private AudioClip m_UiPanelClose;
        [SerializeField] private AudioClip m_UiTabSwitch;
        [SerializeField] private AudioClip m_UiConfirm;
        [SerializeField] private AudioClip m_UiCancel;
        [SerializeField] private AudioClip m_UiLocked;
        [SerializeField] private AudioClip m_UiBuy;

        /// <summary>步枪枪声（多个变体轮换播放）。</summary>
        public AudioClip[] RifleShots => m_RifleShots;

        /// <summary>手枪枪声（多个变体轮换播放）。</summary>
        public AudioClip[] PistolShots => m_PistolShots;

        /// <summary>冲锋枪枪声（多个变体轮换播放）。</summary>
        public AudioClip[] SmgShots => m_SmgShots;

        /// <summary>霰弹枪枪声（多个变体轮换播放）。</summary>
        public AudioClip[] ShotgunShots => m_ShotgunShots;

        /// <summary>空仓扣扳机的干响。</summary>
        public AudioClip DryFire => m_DryFire;

        /// <summary>弹匣脱出。</summary>
        public AudioClip MagazineOut => m_MagazineOut;

        /// <summary>弹匣推入。</summary>
        public AudioClip MagazineIn => m_MagazineIn;

        /// <summary>拉栓 / 上膛。</summary>
        public AudioClip BoltClose => m_BoltClose;

        /// <summary>切换武器。</summary>
        public AudioClip WeaponSwitch => m_WeaponSwitch;

        /// <summary>命中活体（打在单位身上）。</summary>
        public AudioClip[] ImpactFlesh => m_ImpactFlesh;

        /// <summary>命中硬物（打在墙体、集装箱、地面上）。</summary>
        public AudioClip[] ImpactHard => m_ImpactHard;

        /// <summary>草地脚步。</summary>
        public AudioClip[] FootstepGrass => m_FootstepGrass;

        /// <summary>硬质地面（水泥、金属、木栈板）脚步。</summary>
        public AudioClip[] FootstepHard => m_FootstepHard;

        /// <summary>容器开启。</summary>
        public AudioClip LootOpen => m_LootOpen;

        /// <summary>搜刮读条时的翻找声（按进度重复播放）。</summary>
        public AudioClip[] LootRummage => m_LootRummage;

        /// <summary>拾取物品。</summary>
        public AudioClip LootPickup => m_LootPickup;

        /// <summary>撤离读秒的计次提示。</summary>
        public AudioClip ExtractionTick => m_ExtractionTick;

        /// <summary>撤离成功。</summary>
        public AudioClip RaidSuccess => m_RaidSuccess;

        /// <summary>阵亡或超时。</summary>
        public AudioClip RaidFail => m_RaidFail;

        /// <summary>普通按钮点击。</summary>
        public AudioClip UiClick => m_UiClick;

        /// <summary>面板打开。</summary>
        public AudioClip UiPanelOpen => m_UiPanelOpen;

        /// <summary>面板关闭。</summary>
        public AudioClip UiPanelClose => m_UiPanelClose;

        /// <summary>页签切换。</summary>
        public AudioClip UiTabSwitch => m_UiTabSwitch;

        /// <summary>确认 / 出击 / 购买成功。</summary>
        public AudioClip UiConfirm => m_UiConfirm;

        /// <summary>取消 / 返回。</summary>
        public AudioClip UiCancel => m_UiCancel;

        /// <summary>未开放或操作被拒绝。</summary>
        public AudioClip UiLocked => m_UiLocked;

        /// <summary>购买成功。</summary>
        public AudioClip UiBuy => m_UiBuy;

        /// <summary>
        /// 是否存在最小可用的一组音效（枪声 + 命中）。
        /// </summary>
        /// <remarks>启动层用它打印一条明确的警告。缺几个脚步音不该报警，
        /// 但如果连枪声都没有，玩家会以为射击功能坏了。</remarks>
        public bool HasCombatSounds =>
            HasAny(m_RifleShots) || HasAny(m_PistolShots)
            || HasAny(m_SmgShots) || HasAny(m_ShotgunShots);

        /// <summary>按索引取一个步枪枪声（循环取值，取不到返回 null）。</summary>
        public AudioClip PickRifleShot(int index)
        {
            return Pick(m_RifleShots, index);
        }

        /// <summary>按索引取一个手枪枪声（循环取值，取不到返回 null）。</summary>
        public AudioClip PickPistolShot(int index)
        {
            return Pick(m_PistolShots, index);
        }

        /// <summary>按索引取一个冲锋枪枪声（循环取值，取不到返回 null）。</summary>
        public AudioClip PickSmgShot(int index)
        {
            return Pick(m_SmgShots, index);
        }

        /// <summary>按索引取一个霰弹枪枪声（循环取值，取不到返回 null）。</summary>
        public AudioClip PickShotgunShot(int index)
        {
            return Pick(m_ShotgunShots, index);
        }

        /// <summary>按索引取一个活体命中音。</summary>
        public AudioClip PickImpactFlesh(int index)
        {
            return Pick(m_ImpactFlesh, index);
        }

        /// <summary>按索引取一个硬物命中音。</summary>
        public AudioClip PickImpactHard(int index)
        {
            return Pick(m_ImpactHard, index);
        }

        /// <summary>按索引取一个草地脚步。</summary>
        public AudioClip PickFootstepGrass(int index)
        {
            return Pick(m_FootstepGrass, index);
        }

        /// <summary>按索引取一个硬地脚步。</summary>
        public AudioClip PickFootstepHard(int index)
        {
            return Pick(m_FootstepHard, index);
        }

        /// <summary>按索引取一个翻找声。</summary>
        public AudioClip PickLootRummage(int index)
        {
            return Pick(m_LootRummage, index);
        }

        /// <summary>数组为空时返回 null，否则按索引取模。</summary>
        /// <remarks>取模而不是越界返回 null：调用方只需要「循环播放第一个变体」，
        /// 不必自己记住每个数组的长度。</remarks>
        private static AudioClip Pick(AudioClip[] clips, int index)
        {
            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            var safeIndex = index % clips.Length;
            if (safeIndex < 0)
            {
                safeIndex += clips.Length;
            }

            return clips[safeIndex];
        }

        /// <summary>数组里是否至少有一个非空剪辑。</summary>
        private static bool HasAny(AudioClip[] clips)
        {
            if (clips == null)
            {
                return false;
            }

            foreach (var clip in clips)
            {
                if (clip != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
