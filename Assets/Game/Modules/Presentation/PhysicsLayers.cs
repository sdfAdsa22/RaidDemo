using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 物理层登记与查询遮罩的唯一来源：单位层（玩家与 AI）以及哪些查询要排除它。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单位要单独一层（U-50 的根因）：</b>单位的碰撞胶囊承担两个互相冲突的职责——
    /// 子弹射线必须能命中它（否则打不中敌人），移动扫掠却不能把它当成墙（否则贴身即被挡住）。
    /// 二者在同一个层里无法同时满足：修复前敌人一贴近，玩家就被对方的胶囊顶死，
    /// 两个模型卡在一起、谁也动不了。</para>
    ///
    /// <para><b>分工：</b>玩家与 AI 的宿主对象统一标记为 <c>Units</c> 层；
    /// 战斗射线保持全层命中，移动碰撞与地面探测改用排除单位层的遮罩。</para>
    ///
    /// <para><b>降级策略：</b>若工程里没有登记该层（旧分支、误删、或第三方环境），
    /// 遮罩退回「全层」、单位留在原层——行为回到修复前。
    /// 宁可回到旧问题，也不让「移动直接穿墙」这种更严重的故障出现。</para>
    /// </remarks>
    public static class PhysicsLayers
    {
        /// <summary>单位层名称，必须与 <c>ProjectSettings/TagManager.asset</c> 里登记的一致。</summary>
        public const string UnitsLayerName = "Units";

        /// <summary>缓存哨兵：取到该值表示"尚未解析过层索引"。</summary>
        private const int Unresolved = int.MinValue;

        private static int s_UnitsLayer = Unresolved;
        private static bool s_HasWarnedMissingLayer;

        /// <summary>单位层索引；工程里未登记该层时返回 -1。</summary>
        public static int UnitsLayer
        {
            get
            {
                if (s_UnitsLayer == Unresolved)
                {
                    s_UnitsLayer = LayerMask.NameToLayer(UnitsLayerName);
                }

                return s_UnitsLayer;
            }
        }

        /// <summary>移动碰撞的阻挡遮罩：排除单位层，地形 / 建筑 / 道具等全部保留。</summary>
        public static int MovementBlockingMask
        {
            get { return UnitsLayer >= 0 ? ~(1 << UnitsLayer) : ~0; }
        }

        /// <summary>地面探测遮罩：同样排除单位层——站在别人身上不算站在地面上。</summary>
        public static int GroundProbeMask
        {
            get { return MovementBlockingMask; }
        }

        /// <summary>把对象标记为单位层；层未登记时保持原层并只告警一次。</summary>
        /// <param name="target">单位宿主对象（玩家或 AI 的根节点）。</param>
        public static void ApplyUnitLayer(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            var layer = UnitsLayer;
            if (layer < 0)
            {
                WarnMissingLayerOnce();
                return;
            }

            target.layer = layer;
        }

        /// <summary>层未登记时的告警。只打一次，避免每个单位刷一条。</summary>
        private static void WarnMissingLayerOnce()
        {
            if (s_HasWarnedMissingLayer)
            {
                return;
            }

            s_HasWarnedMissingLayer = true;
            Debug.LogWarning(
                "工程里没有登记 \"" + UnitsLayerName + "\" 层（ProjectSettings/TagManager.asset）。" +
                "单位将留在原层：子弹能命中，但移动会被其他单位挡住（U-50 的旧行为）。");
        }
    }
}
