using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 角色手持武器的模型：跟随瞄准方向转动，并对外提供枪口位置。
    /// </summary>
    /// <remarks>
    /// <para><b>模型优先、灰盒兜底：</b>装配层传入步枪与手枪两个预制体，都拿不到时退回一个细长方块。
    /// 兜底不是"开发期偷懒"，而是分发要求——别人克隆仓库时若少了某个素材包，
    /// 仍然要能看出"我拿着枪、朝哪边"，而不是手里空空如也。</para>
    /// <para><b>枪口位置交给本组件提供：</b>弹道起点、枪口火焰、AI 视线都从枪口出发。
    /// 在灰盒阶段枪口是"角色位置抬高 1.2 米"，换成真实模型之后必须改成枪管末端，
    /// 否则子弹会从角色胸口凭空出现，枪口火焰也会在枪身中段炸开。</para>
    /// <para>武器不作为角色的子节点参与动画骨骼：俯视角下角色动画是整套的，
    /// 武器的朝向由瞄准方向决定，与身体朝向解耦，因此每帧按世界坐标摆放最简单。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponView : MonoBehaviour
    {
        /// <summary>
        /// 枪身相对角色的前向偏移（米）。
        /// </summary>
        /// <remarks>0.2 米大致是角色双手自然前伸的位置。再小会让枪身埋进躯干，
        /// 在 62 度俯角下只剩枪口露在外面。</remarks>
        private const float ForwardOffset = 0.2f;

        /// <summary>灰盒枪身的横截面尺寸（米）。</summary>
        private const float CrossSection = 0.09f;

        /// <summary>灰盒枪身每格换算出的长度（米）。</summary>
        private const float LengthPerGridCell = 0.34f;

        /// <summary>持枪高度（米）：角色胸口略下方，与手臂自然下垂的位置相当。</summary>
        private const float HeldHeight = 1.0f;

        /// <summary>灰盒枪身颜色。</summary>
        private static readonly Color WeaponColor = new Color(0.18f, 0.18f, 0.20f);

        private Transform m_Mount;
        private GameObject m_FallbackModel;
        private GameObject m_RifleModel;
        private GameObject m_PistolModel;
        private Transform m_RifleMuzzle;
        private Transform m_PistolMuzzle;
        private Vector3 m_LastForward = Vector3.forward;
        private float m_FallbackLength = 0.68f;

        /// <summary>当前是否装备了武器。角色动画用它决定是否播放持枪姿态。</summary>
        public bool IsEquipped { get; private set; }

        /// <summary>当前武器的表现类别（长枪 / 短枪）。</summary>
        public WeaponPresentationKind Kind { get; private set; } = WeaponPresentationKind.Rifle;

        /// <summary>是否用上了真实模型（false 表示正在用灰盒立方体兜底）。</summary>
        public bool UsesRealModel => m_RifleModel != null || m_PistolModel != null;

        /// <summary>
        /// 枪口的世界坐标。
        /// </summary>
        /// <remarks>没有模型或模型缺少枪口标记时，按"挂点沿朝向前推一个枪长"推算，
        /// 保证任何情况下都能给出一个合理位置。</remarks>
        public Vector3 MuzzleWorldPosition
        {
            get
            {
                var muzzle = ActiveMuzzle();
                if (muzzle != null)
                {
                    return muzzle.position;
                }

                return m_Mount != null
                    ? m_Mount.position + (m_LastForward * m_FallbackLength)
                    : transform.position + (Vector3.up * HeldHeight);
            }
        }

        /// <summary>
        /// 把武器模型挂到角色身上。
        /// </summary>
        /// <param name="owner">角色根节点。</param>
        /// <param name="riflePrefab">步枪预制体，可为 null。</param>
        /// <param name="pistolPrefab">手枪预制体，可为 null。</param>
        public void Build(Transform owner, GameObject riflePrefab = null, GameObject pistolPrefab = null)
        {
            var mountHost = new GameObject("WeaponMount");
            m_Mount = mountHost.transform;
            m_Mount.SetParent(owner, worldPositionStays: false);

            m_RifleModel = InstantiateModel(riflePrefab, "RifleModel", out m_RifleMuzzle);
            m_PistolModel = InstantiateModel(pistolPrefab, "PistolModel", out m_PistolMuzzle);

            // 两个模型都没拿到时才建灰盒；拿到模型就不该再多一个方块穿在枪里。
            if (!UsesRealModel)
            {
                BuildFallbackModel();
            }

            SetEquippedVisual(false);
        }

        /// <summary>
        /// 更新武器模型的位置与朝向。
        /// </summary>
        /// <param name="playerPosition">角色根节点（脚底）的世界坐标。</param>
        /// <param name="aimDegrees">瞄准角度（度）。</param>
        /// <param name="equipped">当前是否装备了武器。</param>
        /// <param name="lengthInGridCells">武器在背包里占的格数，用于判定长枪/短枪与灰盒长度。</param>
        public void UpdateView(Vector3 playerPosition, float aimDegrees, bool equipped, int lengthInGridCells)
        {
            IsEquipped = equipped;
            Kind = AudioPlaybackRules.ResolveWeaponKind(lengthInGridCells);
            m_FallbackLength = Mathf.Max(1, lengthInGridCells) * LengthPerGridCell;

            if (m_Mount == null)
            {
                return;
            }

            SetEquippedVisual(equipped);
            if (!equipped)
            {
                return;
            }

            var planar = Vector2F.FromDegrees(aimDegrees);
            var forward = new Vector3(planar.X, 0f, planar.Y);
            m_LastForward = forward;

            // 真实模型的枢轴在握把处，灰盒方块的枢轴在几何中心，因此两者的前向偏移不同：
            // 方块要多推半个身长，枪口才落在与真实模型一致的位置。
            var offset = UsesRealModel ? ForwardOffset : ForwardOffset + (m_FallbackLength * 0.5f);
            m_Mount.position = playerPosition + (Vector3.up * HeldHeight) + (forward * offset);
            m_Mount.rotation = Quaternion.LookRotation(forward, Vector3.up);

            if (m_FallbackModel != null)
            {
                m_FallbackModel.transform.localScale = new Vector3(CrossSection, CrossSection, m_FallbackLength);
            }

            SelectModel();
        }

        /// <summary>按当前类别显示对应模型。</summary>
        private void SelectModel()
        {
            var useRifle = Kind == WeaponPresentationKind.Rifle;
            if (m_RifleModel != null)
            {
                m_RifleModel.SetActive(useRifle);
            }

            if (m_PistolModel != null)
            {
                m_PistolModel.SetActive(!useRifle);
            }
        }

        /// <summary>整组显示 / 隐藏。</summary>
        private void SetEquippedVisual(bool equipped)
        {
            if (m_Mount != null && m_Mount.gameObject.activeSelf != equipped)
            {
                m_Mount.gameObject.SetActive(equipped);
            }
        }

        /// <summary>取当前类别的枪口标记。</summary>
        private Transform ActiveMuzzle()
        {
            return Kind == WeaponPresentationKind.Rifle ? m_RifleMuzzle : m_PistolMuzzle;
        }

        /// <summary>实例化一个武器模型，并寻找它下面的枪口标记。</summary>
        private GameObject InstantiateModel(GameObject prefab, string name, out Transform muzzle)
        {
            muzzle = null;
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, m_Mount);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // 枪口标记由武器预制体构建器生成，名字固定为 Muzzle。
            foreach (var candidate in instance.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == "Muzzle")
                {
                    muzzle = candidate;
                    break;
                }
            }

            instance.SetActive(false);
            return instance;
        }

        /// <summary>创建灰盒兜底模型。</summary>
        private void BuildFallbackModel()
        {
            m_FallbackModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_FallbackModel.name = "GreyboxWeapon";

            // 灰盒模型不参与物理：它只是给玩家看的。
            var collider = m_FallbackModel.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            m_FallbackModel.transform.SetParent(m_Mount, worldPositionStays: false);
            m_FallbackModel.transform.localPosition = Vector3.zero;
            SetColor(m_FallbackModel.GetComponent<Renderer>(), WeaponColor);
        }

        /// <summary>设置颜色。用属性块避免生成材质实例。</summary>
        private static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
