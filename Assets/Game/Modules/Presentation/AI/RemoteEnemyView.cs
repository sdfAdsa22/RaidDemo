using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 远端敌人的表现：位置与朝向来自服务器快照，动画与状态颜色按与本地敌人相同的规则写入。
    /// </summary>
    /// <remarks>
    /// <para><b>它与 <see cref="EnemyAgentView"/> 的分工：</b>后者绑着一个真实的 <see cref="AiAgent"/>
    /// （逻辑在自己手里，视图只是读它），前者只认识"服务器告诉我的位置、朝向与状态"。
    /// 两者的表现规则必须一致——否则同一名敌人在单机与联机里走路的样子、颜色都不一样，
    /// 这种差异玩家一眼就能看出来。</para>
    ///
    /// <para><b>为什么地面高度用物理探测而不是导航网格：</b>联机客户端不再烘焙导航网格
    /// （那是 AI 的事，而 AI 已经在服务器上），因此这里与玩家、与服务器实体一样用
    /// <see cref="GroundProbe"/> 探地面。地图有几层地面是场景信息，不该由网络层携带。</para>
    ///
    /// <para><b>速度靠位置差分：</b>快照里不发速度（服务器也不需要为了动画多发一个字段），
    /// 表现层按相邻两帧的位置差算出"看起来在走还是跑"，
    /// 再用与本地敌人同一套规则换算动画播放倍率，避免走路剪辑打滑。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RemoteEnemyView : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ShootId = Animator.StringToHash("Shoot");
        private static readonly int DieId = Animator.StringToHash("Die");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int LocomotionRateId =
            Animator.StringToHash(LocomotionAnimationBinding.RateParameterName);

        /// <summary>速度平滑系数。与本地敌人一致，避免差分噪声让动画一顿一顿。</summary>
        private const float SpeedSmoothing = 0.35f;

        private Animator m_Animator;
        private LocomotionAnimationBinding m_AnimationBinding;
        private CombatTargetView m_TargetView;
        private System.IDisposable m_FireSubscription;
        private System.IDisposable m_DamageSubscription;

        private Vector3 m_LastPosition;
        private bool m_HasPositionSample;
        private float m_SmoothedSpeed;
        private float m_LastGroundHeight;
        private bool m_Destroyed;
        private AiStateId m_LastState = AiStateId.Patrol;

        /// <summary>该视图对应的敌人编号（<c>1000+</c>）。</summary>
        public int EnemyId { get; private set; }

        /// <summary>最近一次写入的移动动画播放倍率（调试用）。</summary>
        public float CurrentPlaybackRate { get; private set; } = 1f;

        /// <summary>是否已经进入"阵亡"表现。</summary>
        public bool IsDestroyed
        {
            get { return m_Destroyed; }
        }

        /// <summary>
        /// 创建一个远端敌人视图。
        /// </summary>
        /// <param name="characterPrefab">敌人外观预制体；为 null 时退回灰盒胶囊。</param>
        /// <param name="enemyId">敌人编号。</param>
        /// <param name="initialPosition">初始世界位置。</param>
        public static RemoteEnemyView Create(GameObject characterPrefab, int enemyId, Vector3 initialPosition)
        {
            var host = new GameObject($"RemoteEnemy_{enemyId}");
            host.transform.position = initialPosition;

            var view = host.AddComponent<RemoteEnemyView>();
            view.EnemyId = enemyId;
            view.BuildVisual(characterPrefab);

            // 编号用**敌人编号**：客户端收到的伤害 / 摧毁事件里带的就是它
            //（服务器在下行前已经把战斗单位编号翻译成了实体编号）。
            view.m_TargetView = host.AddComponent<CombatTargetView>();
            view.m_TargetView.Initialize(enemyId, colorFeedback: true);
            view.m_TargetView.SetNormalColor(EnemyAgentView.ResolveColor(AiStateId.Patrol));
            return view;
        }

        /// <summary>
        /// 订阅客户端本地事件：开火动画与受击反馈来自服务器转发的战斗事件。
        /// </summary>
        /// <remarks>
        /// 位置与状态走快照（可靠、连续），而"开了一枪""挨了一下"是一次性动作，
        /// 适合走事件——两者分工与本地敌人完全一致。
        /// </remarks>
        /// <param name="eventBus">客户端会话的事件总线。</param>
        public void Bind(EventBus eventBus)
        {
            if (eventBus == null)
            {
                return;
            }

            m_FireSubscription = eventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            m_DamageSubscription = eventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);
        }

        private void OnDestroy()
        {
            m_FireSubscription?.Dispose();
            m_DamageSubscription?.Dispose();
        }

        /// <summary>
        /// 应用一帧的（插值后）状态。
        /// </summary>
        /// <param name="plane">平面位置。</param>
        /// <param name="facingDegrees">朝向角度（度）。</param>
        /// <param name="state">AI 状态（决定灰盒颜色）。</param>
        /// <param name="isAlive">是否存活。</param>
        /// <param name="deltaTime">帧间隔（秒）。</param>
        public void Apply(Vector2F plane, float facingDegrees, AiStateId state, bool isAlive, float deltaTime)
        {
            var ground = GroundProbe.SampleGroundHeight(plane, transform.position.y, m_LastGroundHeight, transform);
            m_LastGroundHeight = ground;

            transform.position = new Vector3(plane.X, ground, plane.Y);

            // 坐标系映射与本地敌人、玩家完全一致：Unity 旋转角 = 90 - 模拟角度。
            transform.rotation = Quaternion.Euler(0f, 90f - facingDegrees, 0f);

            if (!isAlive)
            {
                ApplyDestroyedVisual();
                return;
            }

            if (state != m_LastState)
            {
                m_LastState = state;
                m_TargetView?.SetNormalColor(EnemyAgentView.ResolveColor(state));
            }

            SyncAnimation(deltaTime);
        }

        /// <summary>切到"已被击毁"的外观：变灰、播放死亡动作、停住动画。</summary>
        public void ApplyDestroyedVisual()
        {
            if (m_Destroyed)
            {
                return;
            }

            m_Destroyed = true;
            m_TargetView?.MarkDestroyed();
            m_Animator?.SetTrigger(DieId);
        }

        /// <summary>把位置差分得到的速度写进 Animator，驱动 Idle / Walk / Run 三态。</summary>
        private void SyncAnimation(float deltaTime)
        {
            if (m_Animator == null)
            {
                return;
            }

            var position = transform.position;
            if (!m_HasPositionSample)
            {
                m_HasPositionSample = true;
                m_LastPosition = position;
                return;
            }

            var delta = position - m_LastPosition;
            m_LastPosition = position;

            var speed = deltaTime > 0.0001f ? delta.magnitude / deltaTime : 0f;
            m_SmoothedSpeed = Mathf.Lerp(m_SmoothedSpeed, speed, SpeedSmoothing);
            m_Animator.SetFloat(SpeedId, m_SmoothedSpeed);

            // 播放倍率与本地敌人同一套换算：设计速度阈值来自角色构建器写进绑定的值，
            // 表现层不复制常量。
            var designSpeed = ResolveDesignSpeed(m_SmoothedSpeed);
            CurrentPlaybackRate = LocomotionAnimationRate.Calculate(m_SmoothedSpeed, designSpeed);
            m_Animator.SetFloat(LocomotionRateId, CurrentPlaybackRate);
        }

        /// <summary>按当前速度选择该用哪一档设计速度；绑定缺失时返回 0（倍率退回 1）。</summary>
        private float ResolveDesignSpeed(float speed)
        {
            if (m_AnimationBinding == null)
            {
                return 0f;
            }

            var useRun = m_AnimationBinding.RunSwitchSpeed > 0f
                         && speed > m_AnimationBinding.RunSwitchSpeed;
            return useRun ? m_AnimationBinding.RunDesignSpeed : m_AnimationBinding.WalkDesignSpeed;
        }

        /// <summary>只有本单位开枪才播放射击动作。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_Animator == null || evt.ShooterId != EnemyId || evt.PelletIndex != 0)
            {
                return;
            }

            m_Animator.SetTrigger(ShootId);
        }

        /// <summary>受击反馈：闪一下；被击杀时切到阵亡外观。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (evt.TargetId != EnemyId)
            {
                return;
            }

            if (evt.WasKilled)
            {
                ApplyDestroyedVisual();
                return;
            }

            m_Animator?.SetTrigger(HitId);
            m_TargetView?.FlashHit();
        }

        /// <summary>实例化角色外观并缓存动画组件；预制体缺失时退回胶囊。</summary>
        private void BuildVisual(GameObject characterPrefab)
        {
            if (characterPrefab == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "RemoteEnemyPlaceholder";
                capsule.transform.SetParent(transform, false);
                capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                Destroy(capsule.GetComponent<Collider>());
                return;
            }

            var instance = Instantiate(characterPrefab, transform, false);
            instance.name = characterPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            m_Animator = instance.GetComponentInChildren<Animator>(true);
            m_AnimationBinding = m_Animator != null
                ? m_Animator.GetComponent<LocomotionAnimationBinding>()
                : null;
        }
    }
}
