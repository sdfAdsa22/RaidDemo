using System;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 枪声播放器：订阅开火事件，播放程序生成的枪声。
    /// </summary>
    /// <remarks>
    /// <para><b>枪声是程序生成的，不使用任何音频素材。</b>这与项目"M0~M6 只用程序化灰盒资源"
    /// 的约定一致（见 README 的素材来源一节）。做法是合成一段带指数衰减的白噪声，
    /// 再叠一个低频冲击，听起来就是一声短促的枪响。M7 替换美术时会换成正式音效。</para>
    /// <para>即便如此这一步也不能省：没有声音的射击手感是残缺的，
    /// 玩家判断枪有没有打出去有一半依靠听觉。</para>
    /// </remarks>
    public sealed class WeaponAudioPlayer : MonoBehaviour
    {
        /// <summary>枪声的变体。不同武器用不同长度与音高的枪声。</summary>
        public enum ShotVariant
        {
            /// <summary>手枪：更短、更脆。</summary>
            Light = 0,

            /// <summary>步枪：更长、更沉。</summary>
            Heavy,
        }

        /// <summary>手枪枪声时长（秒）。</summary>
        private const float LightDuration = 0.16f;

        /// <summary>步枪枪声时长（秒）。</summary>
        private const float HeavyDuration = 0.24f;

        /// <summary>合成音使用的采样率。</summary>
        private const int SampleRate = 44100;

        /// <summary>允许同时播放的枪声数量。全自动连射时避免互相打断。</summary>
        private const int VoiceCount = 6;

        private AudioSource[] m_Voices;
        private int m_NextVoice;
        private AudioClip m_LightClip;
        private AudioClip m_HeavyClip;
        private ShotVariant m_Variant = ShotVariant.Heavy;
        private IDisposable m_Subscription;
        private System.Random m_Noise = new System.Random(20260911);

        /// <summary>
        /// 绑定事件总线。
        /// </summary>
        /// <param name="eventBus">事件总线。</param>
        /// <remarks>
        /// 用多个 AudioSource 轮流播放，而不是每次开火新建对象：
        /// 全自动武器每秒十发以上，同一时刻会有多个枪声重叠，
        /// 单个 AudioSource 会把前一声打断，听起来像卡带。
        /// </remarks>
        public void Bind(EventBus eventBus)
        {
            if (eventBus == null)
            {
                return;
            }

            EnsureVoices();
            if (m_LightClip == null)
            {
                m_LightClip = CreateGunshotClip(LightDuration, 0.85f);
            }

            if (m_HeavyClip == null)
            {
                m_HeavyClip = CreateGunshotClip(HeavyDuration, 0.55f);
            }

            m_Subscription?.Dispose();
            m_Subscription = eventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        /// <summary>切换枪声变体。换枪时由启动层调用。</summary>
        /// <param name="variant">枪声变体。</param>
        public void SetVariant(ShotVariant variant)
        {
            m_Variant = variant;
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
        }

        /// <summary>一次开火播放一声。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_Voices == null || m_Voices.Length == 0)
            {
                return;
            }

            var source = m_Voices[m_NextVoice];
            m_NextVoice = (m_NextVoice + 1) % m_Voices.Length;

            source.clip = m_Variant == ShotVariant.Light ? m_LightClip : m_HeavyClip;

            // 每一声都微调音高与音量：完全一致的重复声音听上去像机器，
            // 这一点随机是让连续射击不刺耳的关键。
            source.pitch = 1f + ((float)m_Noise.NextDouble() * 0.12f) - 0.06f;
            source.volume = 0.65f + ((float)m_Noise.NextDouble() * 0.1f);
            source.Play();
        }

        /// <summary>创建一组轮流使用的播放通道。</summary>
        private void EnsureVoices()
        {
            if (m_Voices != null)
            {
                return;
            }

            m_Voices = new AudioSource[VoiceCount];
            for (var i = 0; i < VoiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                m_Voices[i] = source;
            }
        }

        /// <summary>
        /// 合成一段枪声。
        /// </summary>
        /// <param name="duration">时长（秒）。</param>
        /// <param name="decayRate">衰减速度，越大越干脆。</param>
        /// <returns>可直接播放的音频片段。</returns>
        /// <remarks>
        /// 成分是三样东西叠加：白噪声的快速衰减（枪口爆音）、
        /// 一个低频正弦的慢衰减（膛压的低沉感）、以及极短的起始冲击。
        /// 单独用噪声会像白噪音，单独用低频会像鼓声，三者叠起来才像枪。
        /// </remarks>
        private AudioClip CreateGunshotClip(float duration, float decayRate)
        {
            var sampleCount = Mathf.CeilToInt(duration * SampleRate);
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / SampleRate;
                var progress = (float)i / sampleCount;

                var noise = ((float)m_Noise.NextDouble() * 2f) - 1f;
                var noiseEnvelope = Mathf.Exp(-t * decayRate * 12f);

                var thump = Mathf.Sin(2f * Mathf.PI * 85f * t);
                var thumpEnvelope = Mathf.Exp(-t * 18f);

                // 起始冲击让枪声有清脆的开头，而不是慢慢淡入。
                var attack = progress < 0.02f ? 2.2f : 1f;

                var value = ((noise * noiseEnvelope * 0.75f) + (thump * thumpEnvelope * 0.5f)) * attack;
                samples[i] = Mathf.Clamp(value, -1f, 1f);
            }

            // 末尾做一个短淡出，避免波形被硬切造成爆音。
            var fadeCount = Mathf.Min(sampleCount, SampleRate / 100);
            for (var i = 0; i < fadeCount; i++)
            {
                var index = sampleCount - fadeCount + i;
                samples[index] *= 1f - ((float)i / fadeCount);
            }

            var clip = AudioClip.Create("Gunshot", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
