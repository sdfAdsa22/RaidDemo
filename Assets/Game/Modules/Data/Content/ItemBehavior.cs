using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 物品特殊行为的扩展点。
    /// </summary>
    /// <remarks>
    /// <para>给"不能靠数据描述"的物品留一个挂载位置：能打开的战利品箱、
    /// 使用后回血的消耗品、装备后改变负重上限的背包等等。</para>
    /// <para><b>M2 阶段没有任何具体实现。</b>现在就把抽象类留出来，是因为
    /// ItemDefinition 一旦发布，资产文件里就会引用它；事后补字段会让已有资产
    /// 产生缺字段的状态，需要额外的迁移脚本。</para>
    /// <para>做成 ScriptableObject 而不是接口，是为了让它能以子资产的形式
    /// 挂在物品定义上，从而被 Unity 的序列化与资源管理直接覆盖。</para>
    /// </remarks>
    public abstract class ItemBehavior : ScriptableObject
    {
        /// <summary>
        /// 返回本行为是否与物品定义自洽。
        /// </summary>
        /// <param name="owner">拥有本行为的物品定义。</param>
        /// <returns>自洽返回 null，否则返回描述问题的中文说明。</returns>
        /// <remarks>由 <see cref="ItemCatalog"/> 在编辑器内统一调用，用于尽早发现配置错误。</remarks>
        public virtual string Validate(ItemDefinition owner)
        {
            return null;
        }
    }
}
