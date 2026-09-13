using System;
using System.Collections.Generic;
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
        /// <summary>
        /// 一个可选玩家角色的目录项。
        /// </summary>
        /// <remarks>
        /// id 是存档里写的稳定标识；显示名只用于界面；Prefab 是已经加工好的项目层角色预制体。
        /// </remarks>
        [Serializable]
        public sealed class PlayerCharacterEntry
        {
            [SerializeField] private string m_Id;
            [SerializeField] private string m_DisplayName;
            [SerializeField] private GameObject m_Prefab;

            public string Id => m_Id;
            public string DisplayName => m_DisplayName;
            public GameObject Prefab => m_Prefab;
        }

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

        [Header("准星")]
        [SerializeField] private Sprite m_CrosshairSprite;
        [SerializeField] private Sprite m_CrosshairReloadSprite;

        [Header("玩家角色")]
        [SerializeField] private List<PlayerCharacterEntry> m_PlayerCharacters =
            new List<PlayerCharacterEntry>();

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

        /// <summary>常规准星贴图。</summary>
        public Sprite CrosshairSprite => m_CrosshairSprite;

        /// <summary>换弹中的准星贴图（换一个造型，让"正在换弹"不靠颜色也能看出来）。</summary>
        public Sprite CrosshairReloadSprite => m_CrosshairReloadSprite;

        /// <summary>全部可选玩家角色。</summary>
        public IReadOnlyList<PlayerCharacterEntry> PlayerCharacters => m_PlayerCharacters;

        /// <summary>
        /// 按 id 查找角色预制体。
        /// </summary>
        /// <param name="id">存档里的角色 id。</param>
        /// <returns>找到的预制体；id 为空时返回第一项；找不到时返回 null。</returns>
        /// <remarks>旧存档没有 selectedCharacterId 时 id 为空，回退到第一项保证游戏仍然能开局。</remarks>
        public GameObject FindCharacterPrefab(string id)
        {
            if (m_PlayerCharacters == null || m_PlayerCharacters.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                return m_PlayerCharacters[0] != null ? m_PlayerCharacters[0].Prefab : null;
            }

            for (var i = 0; i < m_PlayerCharacters.Count; i++)
            {
                var entry = m_PlayerCharacters[i];
                if (entry != null && entry.Id == id)
                {
                    return entry.Prefab;
                }
            }

            return null;
        }
    }
}
