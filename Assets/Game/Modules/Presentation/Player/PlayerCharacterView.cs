using System;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 玩家角色的动画驱动：把模拟层状态翻译成 Animator 参数。
    /// </summary>
    /// <remarks>
    /// <para><b>职责边界：</b>本类型只读事件与权威状态，不参与任何决策，也不写回模拟层。
    /// 位置与朝向仍由 <see cref="PlayerMotor"/> 负责——两者挂在同一个 Player 节点上，
    /// 一个管"在哪、朝哪"，一个管"看起来在干什么"。</para>
    ///
    /// <para><b>为什么用事件而不是每帧读输入：</b>输入只代表"玩家想做什么"，
    /// 而动画要表现"实际发生了什么"（例如力竭时按着冲刺键也跑不起来）。
    /// 因此速度取自 <see cref="PlayerMovementChanged"/>，开火取自 <see cref="WeaponFiredEvent"/>，
    /// 死亡取自 <see cref="DamageAppliedEvent"/>——全部是模拟层的结论。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterView : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");
        private static readonly int ArmedId = Animator.StringToHash("Armed");
        private static readonly int ShootId = Animator.StringToHash("Shoot");
        private static readonly int DieId = Animator.StringToHash("Die");

        private Animator m_Animator;
        private CombatTargetView m_TargetView;
        private PlayerWeaponView m_WeaponView;
        private EventBus m_EventBus;
        private IDisposable m_MovementSubscription;
        private IDisposable m_FireSubscription;
        private IDisposable m_DamageSubscription;
        private bool m_IsDead;

        private void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            if (!ServiceLocatorHolder.TryGet(out m_EventBus) || m_EventBus == null)
            {
                return;
            }

            m_MovementSubscription = m_EventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
            m_FireSubscription = m_EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            m_DamageSubscription = m_EventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);
        }

        private void OnDestroy()
        {
            m_MovementSubscription?.Dispose();
            m_FireSubscription?.Dispose();
            m_DamageSubscription?.Dispose();
        }

        private void Update()
        {
            if (m_Animator == null || m_IsDead)
            {
                return;
            }

            // 持枪姿态：武器视图由启动流程在运行时创建，因此延迟查找一次并缓存。
            if (m_WeaponView == null)
            {
                m_WeaponView = GetComponent<PlayerWeaponView>();
                if (m_WeaponView == null)
                {
                    m_WeaponView = FindFirstObjectByType<PlayerWeaponView>();
                }
            }

            m_Animator.SetBool(ArmedId, m_WeaponView != null && m_WeaponView.IsEquipped);
        }

        /// <summary>速度驱动：0 附近待机，超过阈值走，超过冲刺阈值跑。</summary>
        private void OnMovementChanged(PlayerMovementChanged evt)
        {
            if (m_Animator == null || m_IsDead)
            {
                return;
            }

            m_Animator.SetFloat(SpeedId, evt.Speed);

            // "是否在奔跑"直接用模拟层的结论，而不是再拿速度和一个阈值比一次：
            // 阈值会随负重变化（超载时冲刺门槛会降低），在表现层复制一份必然对不上，
            // 表现为"系统认为你在跑（掉体力、噪音按奔跑算），画面上还在走"。
            m_Animator.SetBool(SprintingId, evt.IsSprinting);
        }

        /// <summary>只有玩家自己的枪声才触发开火动画（敌人的射击由各自的视图处理）。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (!ShouldPlayShootAnimation(m_Animator != null, m_IsDead, IsLocalPlayer(evt.ShooterId)))
            {
                return;
            }

            m_Animator.SetTrigger(ShootId);
        }

        /// <summary>
        /// 判断一次开火是否应该触发开火动画。
        /// </summary>
        /// <param name="hasAnimator">角色身上是否找到了动画控制器。</param>
        /// <param name="isDead">角色是否已阵亡。</param>
        /// <param name="isLocalPlayer">开火者是不是本地玩家。</param>
        /// <returns>应该播放开火动画时返回 true。</returns>
        /// <remarks>
        /// <para><b>抽成静态方法是为了能测。</b>这里原本写的是
        /// <c>m_Animator == null || m_IsDead &amp;&amp; !IsLocalPlayer(...)</c>——
        /// C# 里 <c>&amp;&amp;</c> 的优先级高于 <c>||</c>，于是"本地玩家已阵亡"时整个条件为假、
        /// 不会提前返回，倒地动画被自己的枪声打断，看起来像"尸体会站起来开枪"。</para>
        /// <para>这条缺陷不报错、不崩溃，只有进游戏盯着看才会发现；
        /// 把它变成一行可断言的布尔表达式，是唯一能长期防住它的办法。</para>
        /// </remarks>
        public static bool ShouldPlayShootAnimation(bool hasAnimator, bool isDead, bool isLocalPlayer)
        {
            return hasAnimator && !isDead && isLocalPlayer;
        }

        /// <summary>玩家被击杀时切到倒地动画，并停止后续的状态驱动。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (m_Animator == null || m_IsDead || !evt.WasKilled || !IsLocalPlayer(evt.TargetId))
            {
                return;
            }

            m_IsDead = true;
            m_Animator.SetTrigger(DieId);
        }

        /// <summary>
        /// 判断某个标识是不是本地玩家。
        /// </summary>
        /// <remarks>
        /// 标识来自 <see cref="CombatTargetView.TargetId"/>，而该组件由启动流程在运行时添加，
        /// 因此这里必须容错：还没拿到标识时返回 false，等标识就绪后动画自然恢复。
        /// </remarks>
        private bool IsLocalPlayer(int combatantId)
        {
            if (m_TargetView == null)
            {
                m_TargetView = GetComponent<CombatTargetView>();
            }

            return m_TargetView != null && m_TargetView.TargetId == combatantId;
        }
    }
}
