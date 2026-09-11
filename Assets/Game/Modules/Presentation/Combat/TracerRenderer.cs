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

        /// <summary>池中最多同时存在的弹道数量。</summary>
        private const int MaxTracers = 24;

        /// <summary>弹道线条的宽度（米）。</summary>
        private const float TracerWidth = 0.03f;

        /// <summary>弹道颜色。</summary>
        private static readonly Color TracerColor = new Color(1f, 0.9f, 0.5f, 0.9f);

        /// <summary>一条正在显示的弹道。</summary>
        private sealed class TracerInstance
        {
            public LineRenderer Line;
            public float Remaining;
        }

        private readonly List<TracerInstance> m_Active = new List<TracerInstance>();
        private readonly Stack<LineRenderer> m_Pool = new Stack<LineRenderer>();

        private IDisposable m_Subscription;

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

                tracer.Line.gameObject.SetActive(false);
                m_Pool.Push(tracer.Line);
                m_Active.RemoveAt(i);
            }
        }

        /// <summary>一次开火就画一条弹道。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            var line = Rent();
            line.SetPosition(0, evt.Origin);
            line.SetPosition(1, evt.EndPoint);
            line.gameObject.SetActive(true);

            m_Active.Add(new TracerInstance { Line = line, Remaining = TracerLifetime });
        }

        /// <summary>取一条可用的线段渲染器，池空时创建新的。</summary>
        private LineRenderer Rent()
        {
            if (m_Pool.Count > 0)
            {
                return m_Pool.Pop();
            }

            // 池的上限只是限制同时存在的线段数量，超过时复用最早的那一条，
            // 避免极端情况下无限增长。
            if (m_Active.Count >= MaxTracers)
            {
                var oldest = m_Active[0];
                m_Active.RemoveAt(0);
                return oldest.Line;
            }

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
