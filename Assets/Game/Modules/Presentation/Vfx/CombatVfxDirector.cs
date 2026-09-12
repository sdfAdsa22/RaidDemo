using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 战斗特效：订阅开火事件，在枪口喷火、在命中点溅火花或尘土。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么特效由事件驱动而不是写在武器里：</b>AI 与玩家走的是同一条开火链路、
    /// 发的是同一个事件。特效挂在事件上，敌人的枪口一样会喷火，不必为 AI 再写一遍；
    /// 反过来若把特效写在玩家的武器组件里，敌人开枪就永远只有声音没有画面。</para>
    /// <para><b>池化的理由与弹道相同：</b>全自动武器每秒十余发，每次开火都要一个枪口火焰
    /// 加一个命中特效，按需创建销毁会产生持续的内存分配。</para>
    /// <para>命中点用事件里带的 <see cref="WeaponFiredEvent.EndPoint"/>：它是射线真正打到的地方，
    /// 打墙就在墙上、打人就在人身上，不需要这里再补一次射线。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CombatVfxDirector : MonoBehaviour
    {
        /// <summary>单类特效的池容量。</summary>
        private const int PoolCapacity = 24;

        /// <summary>单类特效的预热数量。</summary>
        private const int PoolPrewarm = 6;

        /// <summary>火花喷溅的随机偏航角范围（度）。</summary>
        private const float SparkYawRange = 180f;

        [SerializeField] private GameObject m_MuzzleFlashPrefab;
        [SerializeField] private GameObject m_ImpactSparkPrefab;
        [SerializeField] private GameObject m_ImpactDustPrefab;
        [SerializeField] private GameObject m_ImpactFleshPrefab;

        private VfxPool m_MuzzleFlash;
        private VfxPool m_ImpactSpark;
        private VfxPool m_ImpactDust;
        private VfxPool m_ImpactFlesh;
        private IDisposable m_Subscription;
        private System.Random m_Random;

        /// <summary>累计播放的特效数量，供调试与性能观测使用。</summary>
        public int PlayedCount { get; private set; }

        /// <summary>
        /// 装配特效预制体。
        /// </summary>
        /// <param name="muzzleFlash">枪口火焰预制体，可为 null。</param>
        /// <param name="impactSpark">命中火花预制体，可为 null。</param>
        /// <param name="impactDust">命中尘土预制体，可为 null。</param>
        /// <param name="impactFlesh">命中活体预制体，可为 null。</param>
        /// <remarks>允许传 null：缺少某个特效时只少了那种表现，不影响射击与伤害，
        /// 这样"素材没拿到"不会表现为"游戏坏了"。</remarks>
        public void Initialize(
            GameObject muzzleFlash,
            GameObject impactSpark,
            GameObject impactDust,
            GameObject impactFlesh)
        {
            m_Random ??= new System.Random(20260912);
            // 允许重复装配（场景重载、切换存档）：先释放旧池，避免预制体实例越积越多。
            m_MuzzleFlash?.Dispose();
            m_ImpactSpark?.Dispose();
            m_ImpactDust?.Dispose();
            m_ImpactFlesh?.Dispose();
            m_MuzzleFlash = CreatePool(muzzleFlash);
            m_ImpactSpark = CreatePool(impactSpark);
            m_ImpactDust = CreatePool(impactDust);
            m_ImpactFlesh = CreatePool(impactFlesh);
        }

        /// <summary>绑定事件总线。</summary>
        /// <param name="eventBus">事件总线。</param>
        public void Bind(EventBus eventBus)
        {
            if (eventBus == null)
            {
                return;
            }

            m_Subscription?.Dispose();
            m_Subscription = eventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        private void Update()
        {
            m_MuzzleFlash?.Advance();
            m_ImpactSpark?.Advance();
            m_ImpactDust?.Advance();
            m_ImpactFlesh?.Advance();
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
            m_MuzzleFlash?.Dispose();
            m_ImpactSpark?.Dispose();
            m_ImpactDust?.Dispose();
            m_ImpactFlesh?.Dispose();
        }

        /// <summary>一次开火：枪口喷火，命中则再补一个命中特效。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            // 只取水平方向当作朝向：弹道终点画在地面高度上，直接拿三维差值会让枪口火焰朝下倾斜，
            // 在斜俯视下看起来像"枪口在往地上喷"。
            var direction = evt.EndPoint - evt.Origin;
            direction.y = 0f;
            var forward = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            m_MuzzleFlash?.Play(evt.Origin, rotation);
            PlayedCount++;

            if (!evt.DidHit)
            {
                return;
            }

            // 打在单位身上是"闷响 + 血雾"，打在环境上是"火星 + 尘土"。
            // 判定依据是事件里的命中目标编号：0 表示没有击中任何可受击单位。
            var isUnit = evt.HitTargetId != 0;
            var yaw = Quaternion.Euler(0f, NextYaw(), 0f);
            if (isUnit)
            {
                m_ImpactFlesh?.Play(evt.EndPoint, yaw);
            }
            else
            {
                m_ImpactSpark?.Play(evt.EndPoint, yaw);
                m_ImpactDust?.Play(evt.EndPoint, yaw);
            }

            PlayedCount++;
        }

        private float NextYaw()
        {
            m_Random ??= new System.Random(20260912);
            return ((float)m_Random.NextDouble() * 2f - 1f) * SparkYawRange;
        }

        private VfxPool CreatePool(GameObject prefab)
        {
            return prefab == null ? null : new VfxPool(prefab, transform, PoolCapacity, PoolPrewarm);
        }

        /// <summary>
        /// 单类特效的对象池：负责实例的借出、计时回收与隐藏。
        /// </summary>
        /// <remarks>
        /// 回收时机由"粒子总寿命"推算，而不是写一个固定秒数：
        /// 策划把烟雾寿命从 0.4 秒调到 1.2 秒之后，固定秒数会让烟雾在播到一半时被掐断。
        /// </remarks>
        private sealed class VfxPool
        {
            private readonly ObjectPool<ParticleSystem> m_Pool;
            private readonly List<ParticleSystem> m_Active = new List<ParticleSystem>();
            private readonly List<float> m_ReleaseTimes = new List<float>();

            public VfxPool(GameObject prefab, Transform parent, int capacity, int prewarm)
            {
                m_Pool = new ObjectPool<ParticleSystem>(
                    () => CreateInstance(prefab, parent),
                    maxSize: capacity,
                    prewarmCount: prewarm,
                    onTake: system => system.gameObject.SetActive(true),
                    onRelease: system => system.gameObject.SetActive(false));
            }

            /// <summary>在指定位置播放一次。</summary>
            public void Play(Vector3 position, Quaternion rotation)
            {
                var system = m_Pool.Get();
                system.transform.SetPositionAndRotation(position, rotation);
                system.Clear(true);
                system.Play(true);

                m_Active.Add(system);
                m_ReleaseTimes.Add(Time.time + ResolveTotalLifetime(system));
            }

            /// <summary>回收播放完的实例。</summary>
            public void Advance()
            {
                for (var i = m_Active.Count - 1; i >= 0; i--)
                {
                    if (Time.time < m_ReleaseTimes[i])
                    {
                        continue;
                    }

                    var system = m_Active[i];
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    m_Pool.Release(system);
                    m_Active.RemoveAt(i);
                    m_ReleaseTimes.RemoveAt(i);
                }
            }

            public void Dispose()
            {
                m_Active.Clear();
                m_ReleaseTimes.Clear();
                m_Pool?.Dispose();
            }

            /// <summary>最长的"发射时长 + 粒子寿命"，含全部子粒子系统。</summary>
            private static float ResolveTotalLifetime(ParticleSystem root)
            {
                var longest = 0f;
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = system.main;
                    var span = main.duration + main.startLifetime.constantMax;
                    if (span > longest)
                    {
                        longest = span;
                    }
                }

                // 兜底：没有粒子系统的空预制体也不该被立刻回收。
                return Mathf.Max(longest, 0.2f);
            }

            private static ParticleSystem CreateInstance(GameObject prefab, Transform parent)
            {
                // 显式写 UnityEngine.Object：本文件同时 using System，直接写 Instantiate 会与 System.Object 混淆。
                var instance = UnityEngine.Object.Instantiate(prefab, parent);
                instance.name = prefab.name;
                var system = instance.GetComponent<ParticleSystem>();
                if (system == null)
                {
                    system = instance.GetComponentInChildren<ParticleSystem>();
                }

                instance.SetActive(false);
                return system;
            }
        }
    }
}
