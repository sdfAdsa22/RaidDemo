using System;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 存档根对象。
    /// </summary>
    /// <remarks>
    /// <para><b>只存局外进度。</b>战局中途状态（AI、掉落箱、子弹、场景对象）
    /// 一律不写盘，因为每开一局都会重新生成它们。若把中途状态也存下来，
    /// 存档格式会被几十个只在一个场景里有意义的字段绑死，后续每次改战局都要做迁移。</para>
    ///
    /// <para>SchemaVersion 用于将来迁移。任何破坏兼容性的字段改动都必须递增它，
    /// 并在迁移函数里补齐转换，而不是让旧存档直接崩掉。</para>
    /// </remarks>
    [Serializable]
    public sealed class MetaSaveData
    {
        /// <summary>当前存档结构版本。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>存档结构版本。</summary>
        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>金币余额。</summary>
        public int money;

        /// <summary>写入时间（UTC，ISO 8601）。只用于显示与排查，不参与逻辑。</summary>
        public string savedAtUtc;

        /// <summary>
        /// 战局进行中标记。
        /// </summary>
        /// <remarks>
        /// 进入战局时写 true，正常回到安全屋时写 false。
        /// 下次启动读到 true 就视同阵亡：随身携带物清空。
        /// 这是防止「快退保装备」的唯一机制。
        /// </remarks>
        public bool raidInProgress;

        /// <summary>当前追踪的任务 ID。</summary>
        public string trackedQuestId;

        /// <summary>
        /// 当前选择的玩家角色 ID。
        /// </summary>
        /// <remarks>
        /// 旧存档没有这个字段时为空字符串，由 <c>MetaProgress</c> 回退到默认角色；
        /// 因此这是向后兼容的增量字段，不递增 SchemaVersion。
        /// </remarks>
        public string selectedCharacterId;

        /// <summary>主背包网格宽度。背包决定容量，还原时必须先恢复尺寸。</summary>
        public int backpackWidth;

        /// <summary>主背包网格高度。</summary>
        public int backpackHeight;

        /// <summary>仓库物品。</summary>
        public ItemStackSave[] stash;

        /// <summary>随身背包物品。</summary>
        public ItemStackSave[] backpack;

        /// <summary>弹药挂物品。</summary>
        public ItemStackSave[] ammoPouch;

        /// <summary>装备槽物品。</summary>
        public EquipmentItemSave[] equipment;

        /// <summary>任务状态。</summary>
        public QuestSaveRecord[] quests;

        /// <summary>
        /// 已点亮的收集图鉴条目（物品稳定 ID 列表）。
        /// </summary>
        /// <remarks>
        /// 向后兼容的增量字段：旧存档没有它时为 null，按「一条都没点亮」处理，
        /// 首次进安全屋时会被"扫描持有物"补上当前仓库里的物品；
        /// 因此这里不递增 SchemaVersion，与 selectedCharacterId 的处理方式一致。
        /// </remarks>
        public string[] discoveredItemIds;
    }

    /// <summary>容器里一堆物品的存档记录。</summary>
    [Serializable]
    public sealed class ItemStackSave
    {
        /// <summary>物品稳定 ID。存档永不写显示名与资产引用。</summary>
        public string itemId;

        /// <summary>数量。</summary>
        public int count;

        /// <summary>左上角格子横坐标。</summary>
        public int x;

        /// <summary>左上角格子纵坐标。</summary>
        public int y;

        /// <summary>是否旋转 90 度。</summary>
        public bool rotated;
    }

    /// <summary>装备槽里一件物品的存档记录。</summary>
    [Serializable]
    public sealed class EquipmentItemSave
    {
        /// <summary>装备槽序号，对应 <c>EquipmentSlot</c> 的整数值。</summary>
        public int slot;

        /// <summary>物品稳定 ID。</summary>
        public string itemId;

        /// <summary>数量。装备类物品通常恒为 1。</summary>
        public int count;
    }

    /// <summary>一个任务的存档记录。</summary>
    [Serializable]
    public sealed class QuestSaveRecord
    {
        /// <summary>任务稳定 ID。</summary>
        public string questId;

        /// <summary>任务状态，对应 <c>QuestState</c> 的整数值。</summary>
        public int state;

        /// <summary>当前进度。</summary>
        public int progress;
    }
}
