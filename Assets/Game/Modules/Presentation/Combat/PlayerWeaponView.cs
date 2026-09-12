using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 角色手持武器的灰盒模型。
    /// </summary>
    /// <remarks>
    /// <para>用一个细长的方块代表枪，跟随瞄准方向转动。它的作用不是好看，
    /// 而是让玩家**看得出来自己现在拿的是什么、朝哪边**——
    /// 俯视角下角色本身是个胶囊，没有武器模型的话，换了枪也毫无感觉。</para>
    /// <para>枪的长度由物品在背包里占几格推算（3 格的步枪比 2 格的手枪长），
    /// 因此不需要在战斗参数里再加一个纯表现用的字段。</para>
    /// <para>真正的武器模型在 M7 替换美术时接入，届时本组件只需要换掉创建网格的那一行。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponView : MonoBehaviour
    {
        /// <summary>枪身相对角色的前向偏移（米）。避免枪与身体重叠。</summary>
        private const float ForwardOffset = 0.12f;

        /// <summary>枪身的横截面尺寸（米）。</summary>
        private const float CrossSection = 0.09f;

        /// <summary>每格物品尺寸换算出的枪身长度（米）。</summary>
        private const float LengthPerGridCell = 0.34f;

        /// <summary>枪身相对角色脚底的高度（米）。</summary>
        private const float HeldHeight = 1.0f;

        private static readonly Color WeaponColor = new Color(0.18f, 0.18f, 0.20f);

        private Transform m_Root;
        private Renderer m_Renderer;

        /// <summary>当前是否装备了武器。角色动画用它决定是否播放持枪姿态。</summary>
        public bool IsEquipped { get; private set; }

        /// <summary>把视图挂到角色身上。</summary>
        /// <param name="owner">角色根节点。</param>
        /// <remarks>
        /// 武器不作为角色的子节点：它每帧都要按世界坐标摆到瞄准方向上，
        /// 挂在角色下面反而要先抵消父节点的变换。让它是场景里的独立对象最简单。
        /// </remarks>
        public void Build(Transform owner)
        {
            var host = GameObject.CreatePrimitive(PrimitiveType.Cube);
            host.name = "WeaponModel";

            // 灰盒模型不需要参与物理：它只是给玩家看的。
            var collider = host.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            m_Root = host.transform;
            m_Root.SetParent(owner, worldPositionStays: false);
            m_Renderer = host.GetComponent<Renderer>();
            SetColor(WeaponColor);
            host.SetActive(false);
        }

        /// <summary>
        /// 更新武器模型的位置与朝向。
        /// </summary>
        /// <param name="playerPosition">角色根节点（脚底）的世界坐标。</param>
        /// <param name="aimDegrees">瞄准角度（度）。</param>
        /// <param name="equipped">当前是否装备了武器。</param>
        /// <param name="lengthInGridCells">武器在背包里占的格数，用于推算枪身长度。</param>
        public void UpdateView(Vector3 playerPosition, float aimDegrees, bool equipped, int lengthInGridCells)
        {
            IsEquipped = equipped;

            if (m_Root == null)
            {
                return;
            }

            if (!equipped)
            {
                if (m_Root.gameObject.activeSelf)
                {
                    m_Root.gameObject.SetActive(false);
                }

                return;
            }

            if (!m_Root.gameObject.activeSelf)
            {
                m_Root.gameObject.SetActive(true);
            }

            var planar = Vector2F.FromDegrees(aimDegrees);
            var forward = new Vector3(planar.X, 0f, planar.Y);
            var length = Mathf.Max(1, lengthInGridCells) * LengthPerGridCell;

            // 立方体的本地 +Z 是它的长度方向，LookRotation 让 +Z 对准瞄准方向。
            m_Root.position = playerPosition
                              + (Vector3.up * HeldHeight)
                              + (forward * (ForwardOffset + (length * 0.5f)));
            m_Root.rotation = Quaternion.LookRotation(forward, Vector3.up);
            m_Root.localScale = new Vector3(CrossSection, CrossSection, length);
        }

        /// <summary>设置枪身颜色。用属性块避免生成材质实例。</summary>
        private void SetColor(Color color)
        {
            if (m_Renderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            m_Renderer.SetPropertyBlock(block);
        }
    }
}
