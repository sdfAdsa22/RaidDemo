using System;
using RaidDemo.Kernel;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 玩家脚步：订阅移动事件，按走过的距离触发脚步音。
    /// </summary>
    /// <remarks>
    /// <para>订阅移动事件而不是每帧读 Transform：移动事件本来就带着速度，
    /// 而速度正是脚步节拍需要的唯一输入；从 Transform 反推速度还要自己存上一帧的位置，
    /// 在帧率波动时会算出一串抖动值。</para>
    /// <para>只做本地玩家：AI 的脚步（如果有）属于另一套需求，
    /// 而且 2~4 人联机时每个客户端只该听到自己脚下这一份。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FootstepAudioDirector : MonoBehaviour
    {
        /// <summary>脚步音量。刻意低于枪声：脚步是持续音，抢戏会让整局很吵。</summary>
        private const float StepVolume = 0.5f;

        /// <summary>脚步的最远可听距离（米）。与「奔跑 8 米 / 超载 12 米」的听觉圈同量级。</summary>
        private const float StepMaxDistance = 16f;

        /// <summary>地面探测：从角色上方这么高处开始向下打。</summary>
        private const float ProbeUpOffset = 0.8f;

        /// <summary>地面探测总长（米）。覆盖平台边缘与坡道。</summary>
        private const float ProbeDistance = 2.4f;

        private readonly RaycastHit[] m_ProbeHits = new RaycastHit[6];

        private AudioService m_Audio;
        private Transform m_Player;
        private int m_PlayerId;
        private FootstepCadence m_Cadence;
        private IDisposable m_Subscription;

        /// <summary>
        /// 绑定事件总线与玩家。
        /// </summary>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="audio">音效服务。</param>
        /// <param name="player">玩家根节点（脚底）。</param>
        /// <param name="playerId">本地玩家编号，用于过滤事件。</param>
        public void Bind(EventBus eventBus, AudioService audio, Transform player, int playerId)
        {
            m_Audio = audio;
            m_Player = player;
            m_PlayerId = playerId;
            m_Cadence.Reset();

            if (eventBus == null)
            {
                return;
            }

            m_Subscription?.Dispose();
            m_Subscription = eventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
        }

        /// <summary>移动事件到达：推进节拍，该响就响一声。</summary>
        private void OnMovementChanged(PlayerMovementChanged evt)
        {
            if (evt.PlayerId != m_PlayerId || m_Audio == null || m_Audio.Catalog == null)
            {
                return;
            }

            // 用事件里的时间戳差值当作步长。移动事件每个模拟步都会发布，
            // 因此这个差值就是模拟步长，与逻辑层看到的完全一致。
            var deltaTime = ResolveDeltaTime(evt.Timestamp);
            if (!m_Cadence.Advance(evt.Speed, deltaTime, evt.IsSprinting))
            {
                return;
            }

            var surface = ProbeSurface();
            var catalog = m_Audio.Catalog;
            var volume = Mathf.Lerp(StepVolume, StepVolume * 1.45f, Mathf.InverseLerp(2f, 6f, evt.Speed));
            var clip = surface == FootstepSurface.Grass
                ? catalog.PickFootstepGrass(m_Cadence.StepIndex)
                : catalog.PickFootstepHard(m_Cadence.StepIndex);

            // 自己的脚步用等响度播放：方位就是"我这里"，但必须听得清——
            // 脚步是不被背刺的最后一道听觉提示。
            m_Audio.PlayAt(clip, ResolveFeetPosition(), volume, StepMaxDistance, 0.08f, flat: true);
        }

        private float m_LastTimestamp;
        private bool m_HasTimestamp;

        /// <summary>
        /// 求两次移动事件之间的时间差。
        /// </summary>
        /// <remarks>首次收到事件时没有"上一次"，返回 0 让节拍器这一步不推进——
        /// 否则进图瞬间会因为时间戳从 0 跳到一个很大值而立刻响一步。</remarks>
        private float ResolveDeltaTime(double timestamp)
        {
            var current = (float)timestamp;
            if (!m_HasTimestamp)
            {
                m_HasTimestamp = true;
                m_LastTimestamp = current;
                return 0f;
            }

            var delta = Mathf.Clamp(current - m_LastTimestamp, 0f, 0.2f);
            m_LastTimestamp = current;
            return delta;
        }

        /// <summary>脚步发声点：脚底稍微抬高一点，避免与地面网格重合产生定位偏差。</summary>
        private Vector3 ResolveFeetPosition()
        {
            return m_Player != null
                ? m_Player.position + (Vector3.up * 0.05f)
                : transform.position;
        }

        /// <summary>
        /// 探测脚下的路面类别。
        /// </summary>
        /// <remarks>
        /// 用 RaycastNonAlloc 并从命中列表里挑第一个"不属于玩家自己"的碰撞体：
        /// 角色自身也有胶囊碰撞体，起点若落在它内部，某些物理配置下会先命中自己，
        /// 于是所有脚步声都被判成"硬地"。
        /// </remarks>
        private FootstepSurface ProbeSurface()
        {
            if (m_Player == null)
            {
                return FootstepSurface.Grass;
            }

            var origin = m_Player.position + (Vector3.up * ProbeUpOffset);
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, m_ProbeHits, ProbeDistance);
            for (var i = 0; i < count; i++)
            {
                var hit = m_ProbeHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(m_Player))
                {
                    continue;
                }

                var renderer = hit.collider.GetComponent<Renderer>();
                return AudioPlaybackRules.ResolveSurface(
                    renderer != null && renderer.sharedMaterial != null
                        ? renderer.sharedMaterial.name
                        : null);
            }

            return FootstepSurface.Grass;
        }
    }
}
