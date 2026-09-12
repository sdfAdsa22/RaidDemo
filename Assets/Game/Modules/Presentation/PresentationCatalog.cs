using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 表现层资产总目录：一个场景只需要引用它，就能拿到音效、武器模型与战斗特效。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不再往启动组件上挂七个字段：</b>启动组件已经有很多序列化引用了，
    /// 每加一类美术资产就要在战局与安全屋两处各接一次线，漏一个的症状是"安全屋有枪声、
    /// 进图没有"这类只在一边出现的问题。收成一个目录资产之后，接线只有一处。</para>
    /// <para>所有字段都允许为空：缺素材时对应表现自动退回灰盒或静默跳过，
    /// 游戏本身仍然可玩。这条规则贯穿整个 M7——素材是加分项，不是运行前提。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "PresentationCatalog",
        menuName = "RaidDemo/表现层资产目录",
        order = 22)]
    public sealed class PresentationCatalog : ScriptableObject
    {
        [Header("音效")]
        [SerializeField] private AudioCatalog m_Audio;

        [Header("武器模型")]
        [SerializeField] private GameObject m_RifleWeaponPrefab;
        [SerializeField] private GameObject m_PistolWeaponPrefab;

        [Header("战斗特效")]
        [SerializeField] private GameObject m_MuzzleFlashPrefab;
        [SerializeField] private GameObject m_ImpactSparkPrefab;
        [SerializeField] private GameObject m_ImpactDustPrefab;
        [SerializeField] private GameObject m_ImpactFleshPrefab;

        /// <summary>音效目录。</summary>
        public AudioCatalog Audio => m_Audio;

        /// <summary>步枪模型预制体。</summary>
        public GameObject RifleWeaponPrefab => m_RifleWeaponPrefab;

        /// <summary>手枪模型预制体。</summary>
        public GameObject PistolWeaponPrefab => m_PistolWeaponPrefab;

        /// <summary>枪口火焰预制体。</summary>
        public GameObject MuzzleFlashPrefab => m_MuzzleFlashPrefab;

        /// <summary>命中火花预制体。</summary>
        public GameObject ImpactSparkPrefab => m_ImpactSparkPrefab;

        /// <summary>命中尘土预制体。</summary>
        public GameObject ImpactDustPrefab => m_ImpactDustPrefab;

        /// <summary>命中活体预制体。</summary>
        public GameObject ImpactFleshPrefab => m_ImpactFleshPrefab;
    }
}
