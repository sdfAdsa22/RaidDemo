using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 物品的静态定义资产。
    /// </summary>
    /// <remarks>
    /// <para>它是 <see cref="IItemDefinition"/> 的引擎侧实现。规则层只认接口，
    /// 因此背包的全部规则都能在 EditMode 测试里用假定义跑完，无需创建任何资源文件。</para>
    /// <para>本类只描述"所有 AK-74 共有的东西"。某一把 AK 的状态（数量、是否旋转、
    /// 将来的装弹数与耐久）在 <see cref="ItemInstance"/> 上，两者严格分离。</para>
    /// <para>字段全部用 SerializeField 私有字段加只读属性暴露。物品定义是只读数据，
    /// 运行时不允许改写，因为改了会影响所有已存在的实例。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Item_",
        menuName = "RaidDemo/物品定义",
        order = 10)]
    public sealed class ItemDefinition : ScriptableObject, IItemDefinition
    {
        /// <summary>稳定标识。详见 <see cref="IItemDefinition.Id"/> 的说明。</summary>
        [SerializeField] private string m_Id;

        /// <summary>显示名称。</summary>
        [SerializeField] private string m_DisplayName;

        /// <summary>分类。</summary>
        [SerializeField] private ItemCategory m_Category = ItemCategory.Loot;

        /// <summary>稀有度档位。</summary>
        [SerializeField] private RarityTier m_Rarity = RarityTier.Common;

        /// <summary>未旋转时占用的宽度（列数）。</summary>
        [SerializeField] private int m_Width = 1;

        /// <summary>未旋转时占用的高度（行数）。</summary>
        [SerializeField] private int m_Height = 1;

        /// <summary>单个物品的重量（千克）。</summary>
        [SerializeField] private float m_WeightKg = 0.1f;

        /// <summary>单个物品的基础价值（游戏币）。</summary>
        [SerializeField] private int m_BaseValue = 100;

        /// <summary>单格最大堆叠数量。</summary>
        [SerializeField] private int m_MaxStack = 1;

        /// <summary>是否允许旋转放置。</summary>
        [SerializeField] private bool m_CanRotate = true;

        /// <summary>是否是一个容器。</summary>
        [SerializeField] private bool m_IsContainer;

        /// <summary>作为容器时的内部宽度。</summary>
        [SerializeField] private int m_ContainerWidth = 4;

        /// <summary>作为容器时的内部高度。</summary>
        [SerializeField] private int m_ContainerHeight = 4;

        /// <summary>可选的特殊行为。M2 阶段没有任何实现。</summary>
        [SerializeField] private ItemBehavior m_Behavior;

        /// <inheritdoc />
        public string Id
        {
            get { return m_Id; }
        }

        /// <inheritdoc />
        public string DisplayName
        {
            get { return string.IsNullOrEmpty(m_DisplayName) ? m_Id : m_DisplayName; }
        }

        /// <inheritdoc />
        public ItemCategory Category
        {
            get { return m_Category; }
        }

        /// <inheritdoc />
        public RarityTier Rarity
        {
            get { return m_Rarity; }
        }

        /// <inheritdoc />
        public GridSize GridSize
        {
            get { return new GridSize(m_Width, m_Height); }
        }

        /// <inheritdoc />
        public float WeightKg
        {
            get { return m_WeightKg; }
        }

        /// <inheritdoc />
        public int BaseValue
        {
            get { return m_BaseValue; }
        }

        /// <inheritdoc />
        public int MaxStack
        {
            get { return m_MaxStack; }
        }

        /// <inheritdoc />
        public bool CanRotate
        {
            get { return m_CanRotate; }
        }

        /// <inheritdoc />
        public bool IsContainer
        {
            get { return m_IsContainer; }
        }

        /// <inheritdoc />
        public GridSize ContainerGridSize
        {
            get { return new GridSize(m_ContainerWidth, m_ContainerHeight); }
        }

        /// <summary>可选的特殊行为，可能为 null。</summary>
        public ItemBehavior Behavior
        {
            get { return m_Behavior; }
        }

        /// <inheritdoc />
        public IWeaponStats WeaponStats
        {
            get { return m_Behavior as IWeaponStats; }
        }

        /// <inheritdoc />
        public IAmmoStats AmmoStats
        {
            get { return m_Behavior as IAmmoStats; }
        }

        /// <inheritdoc />
        public IArmorStats ArmorStats
        {
            get { return m_Behavior as IArmorStats; }
        }

        /// <summary>
        /// 校验本资产的字段是否自洽。
        /// </summary>
        /// <returns>自洽返回 null，否则返回中文说明。</returns>
        /// <remarks>
        /// 校验逻辑放在资产自己身上，让目录校验与编辑器检查共用同一份规则，
        /// 而不是在检查脚本里再抄一遍。
        /// </remarks>
        public string ValidateSelf()
        {
            if (string.IsNullOrWhiteSpace(m_Id))
            {
                return "物品 ID 不能为空，格式应为 category.name.variant。";
            }

            if (m_Width <= 0 || m_Height <= 0)
            {
                return $"物品占格尺寸必须为正，当前为 {m_Width}x{m_Height}。";
            }

            if (m_WeightKg < 0f)
            {
                return "物品重量不能为负。";
            }

            if (m_BaseValue < 0)
            {
                return "物品价值不能为负。";
            }

            if (m_MaxStack < 1)
            {
                return "堆叠上限必须至少为 1。";
            }

            if (m_IsContainer)
            {
                if (m_ContainerWidth <= 0 || m_ContainerHeight <= 0)
                {
                    return $"容器物品的内部尺寸必须为正，当前为 {m_ContainerWidth}x{m_ContainerHeight}。";
                }

                if (m_MaxStack != 1)
                {
                    return "容器物品不可堆叠，堆叠上限必须为 1，否则无法确定内部的物品属于哪一份。";
                }
            }

            // 价值区间只约束值钱小物件。弹药与消耗品按每次用量定价，不适用整件价值档位。
            if (m_Category == ItemCategory.Loot)
            {
                var min = RarityValueBands.GetMin(m_Rarity);
                var max = RarityValueBands.GetMax(m_Rarity);
                if (m_BaseValue < min || m_BaseValue > max)
                {
                    return $"值钱小物件的价值应落在 {min} 到 {max} 之间（{m_Rarity} 档），当前为 {m_BaseValue}。";
                }
            }

            if (m_Behavior != null)
            {
                return m_Behavior.Validate(this);
            }

            return null;
        }

        /// <summary>
        /// 在编辑器内修改字段时即时提示问题。
        /// </summary>
        /// <remarks>
        /// 只警告不阻止。设计过程中经常出现先填一半的中间状态，
        /// 强行阻断会让调数值变得非常难受。真正的把关在提交前的目录校验。
        /// </remarks>
        private void OnValidate()
        {
            var problem = ValidateSelf();
            if (problem != null)
            {
                Debug.LogWarning($"[物品定义] {name}：{problem}", this);
            }
        }
    }
}
