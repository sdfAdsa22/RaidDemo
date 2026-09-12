using System;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// AI 单位的灰盒表现：跟随逻辑位置、显示朝向、用颜色表达当前状态。
    /// </summary>
    /// <remarks>
    /// <para><b>它只做搬运与呈现，不做任何决策：</b>位置、朝向、状态全部来自
    /// <see cref="AiAgent"/>。这条边界让 AI 逻辑可以在没有场景的情况下测试，
    /// 也让将来换成正式模型时只需要改这一个文件。</para>
    ///
    /// <para><b>灰盒阶段用颜色表达状态是刻意的：</b>调试 AI 时最需要回答的问题是
    /// "它现在到底在干什么"。没有这层可视化，就只能靠读日志猜；
    /// 有了颜色之后，站在掩体后看一眼就能判断它是在巡逻还是已经发现你了。
    /// 颜色对照表写在 <see cref="ResolveColor"/> 上，M7 替换美术后这套颜色会移除。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EnemyAgentView : MonoBehaviour
    {
        /// <summary>角色高度（米）。与玩家一致，便于对照视线与弹道。</summary>
        private const float BodyHeight = 1.8f;

        /// <summary>身体半径（米）。</summary>
        private const float BodyRadius = 0.4f;

        /// <summary>
        /// 落地采样的搜索半径（米）。必须 ≥ 地图最大高差 + 余量，
        /// 且必须与 <see cref="NavMeshGroundHeightProvider.SampleHeight"/> 用的半径一致
        /// （当前地形为下沉盆地，最大高差 6 米，故取 12 米）。
        /// </summary>
        private const float GroundSampleRadiusMeters = 12f;

        private static readonly Color PatrolColor = new Color(0.35f, 0.72f, 0.42f);
        private static readonly Color InvestigateColor = new Color(0.92f, 0.76f, 0.25f);
        private static readonly Color AlertColor = new Color(1f, 0.58f, 0.16f);
        private static readonly Color EngageColor = new Color(0.88f, 0.32f, 0.26f);
        private static readonly Color RetreatColor = new Color(0.34f, 0.56f, 0.92f);

        private AiAgent m_Agent;
        private CombatTargetView m_TargetView;
        private Transform m_FacingIndicator;
        private GameObject m_CharacterPrefab;
        private Animator m_Animator;
        private Vector3 m_LastPosition;
        private float m_SmoothedSpeed;
        private IDisposable m_StateSubscription;
        private IDisposable m_DamageSubscription;
        private IDisposable m_FireSubscription;
        private AiStateId m_LastState = AiStateId.Patrol;
        private bool m_Destroyed;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ShootId = Animator.StringToHash("Shoot");
        private static readonly int DieId = Animator.StringToHash("Die");
        private static readonly int HitId = Animator.StringToHash("Hit");

        /// <summary>绑定的 AI 单位。供装配与调试读取。</summary>
        public AiAgent Agent
        {
            get { return m_Agent; }
        }

        /// <summary>
        /// 绑定一个 AI 单位并构建灰盒外观。
        /// </summary>
        /// <param name="agent">逻辑层的 AI 单位。</param>
        /// <param name="eventBus">事件总线，用于接收状态变化与伤害事件。</param>
        public void Initialize(AiAgent agent, EventBus eventBus)
        {
            Initialize(agent, eventBus, null);
        }

        /// <summary>绑定 AI 单位与角色预制体；预制体为空时退化为灰盒胶囊外观。</summary>
        public void Initialize(AiAgent agent, EventBus eventBus, GameObject characterPrefab)
        {
            m_Agent = agent;
            m_CharacterPrefab = characterPrefab;
            BuildVisual();

            m_TargetView = gameObject.AddComponent<CombatTargetView>();
            m_TargetView.Initialize(agent.CombatantId, colorFeedback: true);
            m_TargetView.SetNormalColor(ResolveColor(m_LastState));

            if (eventBus == null)
            {
                return;
            }

            m_StateSubscription = eventBus.Subscribe<AiStateChangedEvent>(OnStateChanged);
            m_DamageSubscription = eventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);
            m_FireSubscription = eventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);

            SyncTransform();
        }

        private void OnDestroy()
        {
            m_StateSubscription?.Dispose();
            m_DamageSubscription?.Dispose();
            m_FireSubscription?.Dispose();
        }

        private void Update()
        {
            SyncLifeState();
            SyncTransform();
            SyncAnimation();
        }

        /// <summary>把移动速度写进 Animator，驱动 Idle / Walk / Run 三态。</summary>
        /// <remarks>速度由位置差分得到：AI 逻辑层不暴露速度，而表现层只需要"看起来在走还是跑"。</remarks>
        private void SyncAnimation()
        {
            if (m_Animator == null || m_Agent == null)
            {
                return;
            }

            var delta = transform.position - m_LastPosition;
            m_LastPosition = transform.position;
            var speed = Time.deltaTime > 0.0001f ? delta.magnitude / Time.deltaTime : 0f;
            m_SmoothedSpeed = Mathf.Lerp(m_SmoothedSpeed, speed, 0.35f);
            m_Animator.SetFloat(SpeedId, m_SmoothedSpeed);
        }

        /// <summary>
        /// 每帧与权威状态对齐一次生死表现。
        /// </summary>
        /// <remarks>
        /// <para>死亡表现虽然由伤害事件驱动，但不能只依赖事件：伤害可能来自没有发布事件的路径
        /// （调试指令、将来的坠落伤害、服务端下发的状态同步）。漏掉一次，敌人就永远停在
        /// "看起来还活着"的状态上——而它的逻辑已经死了，玩家会朝一具尸体继续开枪。</para>
        /// <para>这与弹药界面"每帧拉取权威状态"是同一个取舍：事件负责即时，轮询负责自愈。</para>
        /// </remarks>
        private void SyncLifeState()
        {
            if (m_Agent == null || m_Destroyed || m_Agent.IsAlive)
            {
                return;
            }

            ApplyDestroyedVisual();
        }

        /// <summary>切到"已被击毁"的外观。伤害事件与每帧对齐都会走到这里。</summary>
        private void ApplyDestroyedVisual()
        {
            m_Destroyed = true;
            m_TargetView?.MarkDestroyed();
            m_Animator?.SetTrigger(DieId);

            // 朝向指示条在死亡后失去意义，藏起来避免看起来还活着。
            if (m_FacingIndicator != null)
            {
                m_FacingIndicator.gameObject.SetActive(false);
            }
        }

        /// <summary>状态变化：换色，让玩家与开发者都能一眼看出 AI 在做什么。</summary>
        private void OnStateChanged(AiStateChangedEvent evt)
        {
            if (m_Agent == null || evt.CombatantId != m_Agent.CombatantId)
            {
                return;
            }

            m_LastState = evt.Current;
            m_TargetView?.SetNormalColor(ResolveColor(evt.Current));
        }

        /// <summary>受击反馈：闪烁；被击杀时变成灰色并停下。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (m_Agent == null || evt.TargetId != m_Agent.CombatantId || m_TargetView == null)
            {
                return;
            }

            if (evt.WasKilled)
            {
                ApplyDestroyedVisual();
                return;
            }

            m_Animator?.SetTrigger(HitId);
            m_TargetView.FlashHit();
        }

        /// <summary>只有本单位开枪才播放射击动作。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_Animator == null || m_Agent == null || evt.ShooterId != m_Agent.CombatantId)
            {
                return;
            }

            m_Animator.SetTrigger(ShootId);
        }

        /// <summary>把逻辑层的平面位置与朝向搬到 Transform 上。</summary>
        private void SyncTransform()
        {
            if (m_Agent == null)
            {
                return;
            }

            var position = m_Agent.Position;
            transform.position = new Vector3(position.X, ResolveGroundHeight(position), position.Y);

            // 坐标系映射与玩家完全一致：Unity 旋转角 = 90 - 模拟角度。
            // 两处用的是同一条换算，因此敌人的朝向指示与弹道方向天然对齐。
            transform.rotation = Quaternion.Euler(0f, 90f - m_Agent.FacingDegrees, 0f);
        }

        /// <summary>
        /// 采样脚下的导航网格高度，让敌人能站在装卸平台上而不是陷进台体里。
        /// </summary>
        /// <param name="position">逻辑层给出的平面位置。</param>
        /// <returns>脚底应处的世界高度（米）。采样失败时退化为 0，即灰盒地面。</returns>
        /// <remarks>
        /// <para>逻辑层的位置是二维的（它必须能在无头服务端运行），高度属于场景信息，
        /// 因此由表现层补上。这正是「逻辑层只管平面、表现层负责落地」这条分工的落点。</para>
        ///
        /// <para>采样点要抬到单位当前位置的上方再往下找：若直接拿 y 等于 0 的点去采样，
        /// 站在平台上时该点位于台体内部，采样要么失败、要么把结果拉回地面，
        /// 表现为「敌人半个身子埋进台面」，而这在俯视角下很难与「敌人被击倒」区分。
        /// 抬高采样是为了让探测点脱离台体内部。</para>
        ///
        /// <para><b>搜索半径必须给足：</b>单位刚出生时脚底还没落地，<c>transform.position.y</c>
        /// 可能离脚下地面很远（下沉盆地的谷底与出生点相差数米）。半径不够时采样失败、
        /// 回退成 0，敌人会一直浮在 y=0 的空中，而现象与"地形没做对"几乎无法区分。</para>
        /// </remarks>
        private float ResolveGroundHeight(Vector2F position)
        {
            var probe = new Vector3(position.X, transform.position.y + 2f, position.Y);
            if (NavMesh.SamplePosition(probe, out var hit, GroundSampleRadiusMeters, NavMesh.AllAreas))
            {
                return hit.position.y;
            }

            return 0f;
        }

        /// <summary>状态到灰盒颜色的对照表。</summary>
        /// <param name="state">AI 状态。</param>
        /// <remarks>
        /// 公开成静态方法而不是留一个私有表：开发者模式也要按同一套颜色画视野锥，
        /// 两处各写一份迟早会出现"胶囊是红的、扇形还是绿的"这种不一致，
        /// 而颜色不一致会直接误导调试判断。
        /// </remarks>
        public static Color ResolveColor(AiStateId state)
        {
            switch (state)
            {
                case AiStateId.Investigate:
                    return InvestigateColor;
                case AiStateId.Alert:
                    return AlertColor;
                case AiStateId.Engage:
                    return EngageColor;
                case AiStateId.Retreat:
                    return RetreatColor;
                default:
                    return PatrolColor;
            }
        }

        /// <summary>
        /// 构建灰盒外观：胶囊碰撞体 + 身体 + 头部 + 朝向指示。
        /// </summary>
        /// <remarks>
        /// 碰撞体必须挂在根节点上：射线检测返回的是碰撞体，<see cref="CombatTargetView"/>
        /// 靠 <c>GetComponentInParent</c> 找到它，从而把命中翻译成单位标识。
        /// 身体与头部的几何体自身不带碰撞体，避免出现"打中头部却打不到"这类分层问题——
        /// 整个敌人只有一个碰撞体，判定因此永远一致。
        /// </remarks>
        private void BuildVisual()
        {
            var collider = gameObject.AddComponent<CapsuleCollider>();
            collider.height = BodyHeight;
            collider.radius = BodyRadius;
            collider.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            // M7 批次 1：优先使用正式角色模型（自带 Animator 与状态动画）。
            // 碰撞体仍然只有一个、仍在根节点上——射线判定与单位标识的翻译不变。
            if (m_CharacterPrefab != null)
            {
                var visual = Instantiate(m_CharacterPrefab, transform);
                visual.name = "Character";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                m_Animator = visual.GetComponentInChildren<Animator>();
                m_LastPosition = transform.position;
                return;
            }

            var bodyHeight = BodyHeight - (BodyRadius * 2f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(transform, worldPositionStays: false);
            body.transform.localPosition = new Vector3(0f, BodyRadius + (bodyHeight * 0.5f), 0f);
            body.transform.localScale = new Vector3(BodyRadius * 2f, bodyHeight * 0.5f, BodyRadius * 2f);
            DestroyCollider(body);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(transform, worldPositionStays: false);
            head.transform.localPosition = new Vector3(0f, BodyHeight - BodyRadius, 0f);
            head.transform.localScale = Vector3.one * (BodyRadius * 2f);
            DestroyCollider(head);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicator.name = "FacingIndicator";
            indicator.transform.SetParent(transform, worldPositionStays: false);
            indicator.transform.localPosition = new Vector3(0f, 0.3f, BodyRadius + 0.35f);
            indicator.transform.localScale = new Vector3(0.22f, 0.16f, 0.6f);
            DestroyCollider(indicator);
            m_FacingIndicator = indicator.transform;
        }

        /// <summary>移除图元自带的碰撞体，保证一个敌人只有一个碰撞体。</summary>
        private static void DestroyCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }
    }
}
