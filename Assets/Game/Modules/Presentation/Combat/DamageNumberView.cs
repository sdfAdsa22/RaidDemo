using RaidDemo.Combat;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 打在靶子上的伤害数字：命中处冒出一个数字，向上飘并淡出。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么靶子也要进战斗层</b>：只有进了战斗层，命中判定、伤害计算与伤害事件
    /// 才全部走现成链路。否则就得为「打靶」单独写一套弹道，那正是重复实现的开始。</para>
    ///
    /// <para>数字用 TextMesh 而不是 UI 画布：它只是世界空间里的一个小物件，
    /// 不需要排版与点击。伤害数字只含数字，因此不依赖中文字体。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DamageNumberView : MonoBehaviour
    {
        /// <summary>数字存活时长（秒）。</summary>
        private const float LifeSeconds = 0.9f;

        /// <summary>向上飘的速度（米/秒）。</summary>
        private const float RiseSpeed = 1.4f;

        private ShootingTarget m_Target;
        private int m_CombatantId;
        private System.IDisposable m_FiredSubscription;
        private System.IDisposable m_DamageSubscription;
        private Vector3 m_LastHitPoint;

        /// <summary>绑定事件。</summary>
        public void Initialize(ShootingTarget target, int combatantId, EventBus eventBus)
        {
            m_Target = target;
            m_CombatantId = combatantId;

            // 先记下弹道终点，再用它给伤害数字定位：
            // 伤害事件本身不带位置，而「打在哪里」正是玩家想看到的信息。
            m_FiredSubscription = eventBus?.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            m_DamageSubscription = eventBus?.Subscribe<DamageAppliedEvent>(OnDamageApplied);
        }

        private void OnDestroy()
        {
            m_FiredSubscription?.Dispose();
            m_DamageSubscription?.Dispose();
        }

        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (evt.HitTargetId == m_CombatantId)
            {
                m_LastHitPoint = evt.EndPoint;
            }
        }

        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (evt.TargetId != m_CombatantId || m_Target == null)
            {
                return;
            }

            var local = transform.InverseTransformPoint(m_LastHitPoint);
            var isBullseye = m_Target.RegisterHit(evt.Damage, local);
            Spawn(evt.Damage, isBullseye);
        }

        /// <summary>生成一个会向上飘的数字。</summary>
        private void Spawn(float damage, bool isBullseye)
        {
            var host = new GameObject("DamageNumber");
            host.transform.position = m_LastHitPoint.sqrMagnitude > 0f
                ? m_LastHitPoint + (Vector3.up * 0.25f)
                : transform.position + (Vector3.up * 1.2f);

            var text = host.AddComponent<TextMesh>();
            text.text = Mathf.CeilToInt(damage).ToString();
            text.fontSize = 64;
            text.characterSize = 0.06f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = isBullseye
                ? new Color(1f, 0.82f, 0.25f)
                : new Color(0.95f, 0.95f, 0.95f);

            // 朝着相机：斜俯视下数字必须正对镜头才读得出来。
            if (Camera.main != null)
            {
                host.transform.rotation = Camera.main.transform.rotation;
            }

            host.AddComponent<DamageNumberFade>().Initialize(LifeSeconds, RiseSpeed);
        }
    }

    /// <summary>伤害数字的漂浮与淡出。</summary>
    public sealed class DamageNumberFade : MonoBehaviour
    {
        private float m_Remaining;
        private float m_RiseSpeed;
        private TextMesh m_Text;
        private Color m_Color;

        /// <summary>初始化。</summary>
        public void Initialize(float lifeSeconds, float riseSpeed)
        {
            m_Remaining = lifeSeconds;
            m_RiseSpeed = riseSpeed;
            m_Text = GetComponent<TextMesh>();
            m_Color = m_Text != null ? m_Text.color : Color.white;
        }

        private void Update()
        {
            m_Remaining -= Time.deltaTime;
            transform.position += Vector3.up * (m_RiseSpeed * Time.deltaTime);

            if (m_Remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (m_Text != null)
            {
                var color = m_Color;
                color.a = Mathf.Clamp01(m_Remaining * 1.6f);
                m_Text.color = color;
            }
        }
    }
}
