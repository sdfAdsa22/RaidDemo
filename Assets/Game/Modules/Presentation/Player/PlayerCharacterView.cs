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
        }

        /// <summary>只有玩家自己的枪声才触发开火动画（敌人的射击由各自的视图处理）。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_Animator == null || m_IsDead && !IsLocalPlayer(evt.ShooterId))
            {
                return;
            }

            if (IsLocalPlayer(evt.ShooterId))
            {
                m_Animator.SetTrigger(ShootId);
            }
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
