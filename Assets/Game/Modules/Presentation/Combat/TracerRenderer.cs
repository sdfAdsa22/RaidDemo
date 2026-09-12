using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 弹道表现：订阅开火事件，画出枪口到命中点的一条线。
    /// </summary>
    /// <remarks>
    /// <para>用对象池复用线段渲染器，而不是每开一枪创建再销毁：
    /// 全自动武器每秒会打出十发以上，每次创建 GameObject 都会产生垃圾回收压力，
    /// 表现为射击时的周期性卡顿。</para>
    /// <para>线段在极短时间内淡出，是因为射线弹道在真实时间里没有飞行过程，
    /// 如果不做残留，玩家在 60 帧下几乎看不到自己开了枪。</para>
    /// </remarks>
    public sealed class TracerRenderer : MonoBehaviour
    {
        /// <summary>弹道留存的时长（秒）。</summary>
        private const float TracerLifetime = 0.05f;

        /// <summary>池中保留的闲置弹道上限。</summary>
        /// <remarks>
        /// 全自动武器每秒打出十发以上，同时存在的弹道通常不超过二十几条；
        /// 留到 32 足够覆盖高峰，又不会让闲置对象长期占着内存。
        /// </remarks>
        private const int PoolCapacity = 32;

        /// <summary>预热数量。弹道是开火瞬间就要用的东西，首次使用的分配尖峰会直接影响手感。</summary>
        private const int PoolPrewarm = 8;

        /// <summary>弹道线条的宽度（米）。</summary>
        private const float TracerWidth = 0.03f;

        /// <summary>弹道离地高度（米）。略高于地面，避免与地板重叠产生闪烁。</summary>
        private const float GroundOffset = 0.05f;

        /// <summary>弹道颜色。</summary>
        private static readonly Color TracerColor = new Color(1f, 0.9f, 0.5f, 0.9f);

        /// <summary>一条正在显示的弹道。</summary>
        private sealed class TracerInstance
        {
            public LineRenderer Line;
            public float Remaining;
        }

        private readonly List<TracerInstance> m_Active = new List<TracerInstance>();
        private ObjectPool<LineRenderer> m_Pool;

        private IDisposable m_Subscription;

        /// <summary>累计创建的弹道数量，供调试与性能观测使用。</summary>
        public int TotalCreated
        {
            get { return m_Pool?.TotalCreated ?? 0; }
        }

        private void Awake()
        {
            // 用 Kernel 的通用对象池，而不是在本类里手写一个：
            // 弹道不是唯一需要复用的东西（M5 的掉落物、M7 的特效都要），
            // 复用的策略只应该有一份实现。
            m_Pool = new ObjectPool<LineRenderer>(
                CreateLine,
                maxSize: PoolCapacity,
                prewarmCount: PoolPrewarm,
                onTake: line => line.gameObject.SetActive(true),
                onRelease: line => line.gameObject.SetActive(false));
        }

        /// <summary>
        /// 绑定事件总线。由启动层在事件总线就绪后调用。
        /// </summary>
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

        private void OnDestroy()
        {
            m_Subscription?.Dispose();

            // 在 OnDestroy 而不是 OnDisable 里释放池：Awake 只会执行一次，
            // 若在 OnDisable 里把池置空，组件被禁用再启用之后就再也没有池可用了。
            m_Pool?.Dispose();
            m_Pool = null;
        }

        private void Update()
        {
            for (var i = m_Active.Count - 1; i >= 0; i--)
            {
                var tracer = m_Active[i];
                tracer.Remaining -= Time.deltaTime;
                if (tracer.Remaining > 0f)
                {
                    continue;
                }

                m_Pool.Release(tracer.Line);
                m_Active.RemoveAt(i);
            }
        }

        /// <summary>一次开火就画一条弹道。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            var line = Rent();

            // 弹道画在地面上，而不是枪口高度。
            // 原因：准星落在地面，而弹道从枪口水平打出，两者在斜俯视下不在同一条视线上。
            // 把命中点投影到地面之后，这条线必然穿过准星——
            // 玩家看到的才是"子弹从我这打到准星指的地方"。
            line.SetPosition(0, ProjectToGround(evt.Origin));
            line.SetPosition(1, ProjectToGround(evt.EndPoint));
            m_Active.Add(new TracerInstance { Line = line, Remaining = TracerLifetime });
        }

        /// <summary>把世界坐标压到地面高度，稍微抬高一点避免与地板重叠闪烁。</summary>
        private static Vector3 ProjectToGround(Vector3 point)
        {
            // 保留弹道点的真实高度，只加一个小偏移避免与地面/平台面 z-fighting。
            //
            // 历史：这里曾经把 Y 强制压成 GroundOffset（"贴地画线"），在平地上看不出问题；
            // 但装卸平台高 1.25 米，站在平台上的敌人开枪时弹道就被画到平台下面去了——
            // 表现为"敌人开火了但看不见弹道"，实际上子弹是打中的。
            return new Vector3(point.x, point.y + GroundOffset, point.z);
        }

        /// <summary>取一条可用的线段渲染器，池空时创建新的。</summary>
        private LineRenderer Rent()
        {
            // 同时存在的弹道超过池容量时，复用最早的那一条。
            // 极端情况下（例如射速被调得极高）这会让个别弹道提前消失，
            // 但比无限增长导致的内存尖峰更容易接受。
            if (m_Active.Count >= PoolCapacity)
            {
                var oldest = m_Active[0];
                m_Active.RemoveAt(0);
                return oldest.Line;
            }

            return m_Pool.Get();
        }

        /// <summary>创建一条新的弹道线段。</summary>
        private LineRenderer CreateLine()
        {
            var host = new GameObject("Tracer");
            host.transform.SetParent(transform, worldPositionStays: false);
            var line = host.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = TracerWidth;
            line.endWidth = TracerWidth;
            line.useWorldSpace = true;
            line.startColor = TracerColor;
            line.endColor = TracerColor;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            host.SetActive(false);
            return line;
        }
    }
}
