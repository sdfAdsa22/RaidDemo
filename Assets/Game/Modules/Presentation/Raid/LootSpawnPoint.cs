using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 战利品容器的场景标记：告诉启动层「这个位置要生成一个什么样子的箱子」。
    /// </summary>
    /// <remarks>
    /// <para>标记组件只承载**配置数据**，不含任何行为：它不知道容器网格、不知道掉落表，
    /// 也不知道玩家什么时候来开箱。真正的容器由启动层按 ID 从
    /// <c>RaidDemo.Raid.LootContainerCatalog</c> 取定义后创建，并登记到容器注册表。</para>
    ///
    /// <para>为什么用场景标记而不是把容器清单写死在启动层代码里：
    /// 箱子的位置属于「地图布置」，而地图是由生成器写进场景的。
    /// 若位置写死在启动层，改一个箱子的位置就要动代码，而且生成器与启动层会出现两份互相矛盾的地图数据。</para>
    ///
    /// <para>本组件同时被编辑器生成器（写入）与启动层（读取）使用，因此放在表现层：
    /// 它是场景的一部分，不属于纯逻辑模块。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LootSpawnPoint : MonoBehaviour
    {
        /// <summary>
        /// 容器定义 ID，取值见 <c>LootContainerCatalog</c>（例如 crate.common、safe.rare）。
        /// </summary>
        /// <remarks>
        /// 存字符串而不是枚举：容器种类以后会继续增加（工具箱、尸体、保险柜变体），
        /// 用字符串扩展不需要重新序列化旧场景，也不会因为枚举成员顺序变化而错位。
        /// 代价是拼错 ID 时编译期发现不了，因此启动层会对找不到的定义给出明确告警。
        /// </remarks>
        [SerializeField] private string m_ContainerDefinitionId = "crate.common";

        /// <summary>容器定义 ID。</summary>
        public string ContainerDefinitionId
        {
            get { return m_ContainerDefinitionId; }
        }
    }
}
