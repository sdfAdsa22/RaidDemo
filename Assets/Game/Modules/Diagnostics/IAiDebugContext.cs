using RaidDemo.AI;
using RaidDemo.Combat;
using UnityEngine;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 调试工具读取世界状态的入口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用接口而不是让调试层直接去找场景对象：</b>调试工具由启动层创建，
    /// 而启动层又引用调试程序集——如果调试层反过来引用启动层，就形成了程序集循环。
    /// 让启动层实现这个接口，依赖方向就变成单向：启动层 → 调试层。</para>
    ///
    /// <para><b>为什么全部是只读属性：</b>可视化绝不能改变玩法状态。把契约限定成"只能读"，
    /// 就从结构上排除了"打开调试面板后 AI 行为变了"这类最难排查的缺陷。</para>
    ///
    /// <para>实现者：<c>SceneBootstrap</c>（见 <c>SceneBootstrap.Diagnostics.cs</c>）。</para>
    /// </remarks>
    public interface IAiDebugContext
    {
        /// <summary>AI 调度器：单位列表、参数、时钟。</summary>
        AiDirector Director { get; }

        /// <summary>射线能力，供调试层独立复核"看得见 / 被挡住"。</summary>
        IHitProbe Probe { get; }

        /// <summary>战斗单位注册表，用于读取玩家生命。</summary>
        CombatWorld World { get; }

        /// <summary>当前用于渲染的相机。可能为 null（无相机时只显示面板，不显示世界标签）。</summary>
        Camera ViewCamera { get; }

        /// <summary>AI 当前的目标（即玩家）快照。</summary>
        AiTargetInfo Target { get; }

        /// <summary>玩家在战斗层中的单位标识。</summary>
        int PlayerCombatantId { get; }

        /// <summary>玩家当前的噪音档位。</summary>
        NoiseTier PlayerNoiseTier { get; }
    }
}
