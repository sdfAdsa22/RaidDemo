using System.Collections.Generic;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 音效播放服务：全场景唯一的声音出口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不让每个组件自己挂 AudioSource：</b>那样做迟早会出现三个问题——
    /// 每个组件各写一套音量与随机音高，同一场战斗里手枪比步枪还响；并发数失控，
    /// 二十个敌人同时开火时创建二十个 AudioSource；以及"到底谁在响"无从排查。
    /// 集中成一个服务之后，混音、并发上限、同音重复节流都只有一份实现。</para>
    ///
    /// <para><b>为什么用固定数量的声部轮流播放而不是播放完销毁：</b>全自动武器每秒十余发，
    /// 命中反馈还会叠加，按需创建销毁会持续产生垃圾回收压力，表现为射击时周期性卡顿。
    /// 这与弹道渲染使用对象池是同一个理由（见 <see cref="TracerRenderer"/>）。</para>
    ///
    /// <para>本服务只负责"把一条剪辑按指定音量、音高、空间位置播出去"，
    /// 具体什么时候该响由各监听事件的导演组件决定。这样加一条新音效不必改播放器。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        /// <summary>3D 声部数量。</summary>
        /// <remarks>同时可能发声的有：玩家枪声、数个敌人的枪声、命中、脚步、搜刮。
        /// 16 个声部覆盖正常交火，超出时复用最早的那个——听感上是"少了一层细节"，
        /// 而不是"报错"或"卡顿"。</remarks>
        private const int SpatialVoiceCount = 16;

        /// <summary>2D 声部数量（界面与结算提示，始终不衰减）。</summary>
        private const int FlatVoiceCount = 4;

        /// <summary>空间音效不再随距离衰减的距离下限（米）。</summary>
        private const float MinDistance = 3.5f;

        /// <summary>空间音效的默认最远可听距离（米）。</summary>
        private const float DefaultMaxDistance = 45f;

        /// <summary>默认音高抖动幅度：0.05 表示 ±5%。</summary>
        private const float DefaultPitchJitter = 0.05f;

        /// <summary>随机种子固定，保证同一串操作听起来完全一致（便于对照调试与录像）。</summary>
        private const int RandomSeed = 20260912;

        private readonly Dictionary<AudioClip, float> m_LastPlayedTimes = new Dictionary<AudioClip, float>();
        private readonly List<AudioClip> m_ThrottleKeys = new List<AudioClip>();
        private AudioSource[] m_SpatialVoices;
        private AudioSource[] m_FlatVoices;
        private System.Random m_Random;
        private int m_NextSpatial;
        private int m_NextFlat;
        private float m_MasterVolume = 0.85f;

        /// <summary>当前使用的音效目录（可能为 null，表示没有音效素材）。</summary>
        public AudioCatalog Catalog { get; private set; }

        /// <summary>主音量（0~1）。</summary>
        public float MasterVolume => m_MasterVolume;

        /// <summary>
        /// 初始化：绑定音效目录并创建声部。
        /// </summary>
        /// <param name="catalog">音效目录，可为 null。</param>
        /// <param name="masterVolume">主音量。</param>
        /// <remarks>可重复调用（场景重载、切换存档）：已存在的声部会被复用，不会越积越多。</remarks>
        public void Initialize(AudioCatalog catalog, float masterVolume = 0.85f)
        {
            Catalog = catalog;
            m_MasterVolume = Mathf.Clamp01(masterVolume);
            m_Random ??= new System.Random(RandomSeed);
            EnsureVoices();

            if (catalog == null)
            {
                LogWarning("音效目录为空：本局不会有任何音效。");
            }
            else if (!catalog.HasCombatSounds)
            {
                LogWarning("音效目录里没有任何枪声剪辑：射击将没有声音。" +
                           "请执行菜单 RaidDemo/M7/重建音效资产与音效目录。");
            }
        }

        /// <summary>
        /// 通过服务定位器输出一条警告。
        /// </summary>
        /// <remarks>
        /// 不直接 <c>Debug.LogWarning</c>：工程规范要求所有日志走统一的 LogService，
        /// 否则正式构建里会混进开发期噪声。服务定位器尚未就绪时静默丢弃——
        /// 表现层不该因为"日志系统还没起来"而抛异常。
        /// </remarks>
        private static void LogWarning(string message)
        {
            if (ServiceLocatorHolder.TryGet(out LogService log))
            {
                log.Warning(message);
            }
        }

        /// <summary>
        /// 在指定世界位置播放一条音效（有距离衰减与左右声像）。
        /// </summary>
        /// <param name="clip">音频剪辑，为 null 时直接忽略。</param>
        /// <param name="position">世界坐标。</param>
        /// <param name="volume">音量（0~1）。</param>
        /// <param name="maxDistance">最远可听距离（米）。</param>
        /// <param name="pitchJitter">音高抖动幅度（0~0.3）。</param>
        /// <returns>成功播放返回 true；被节流或缺少素材返回 false。</returns>
        public bool PlayAt(
            AudioClip clip,
            Vector3 position,
            float volume = 1f,
            float maxDistance = DefaultMaxDistance,
            float pitchJitter = DefaultPitchJitter)
        {
            if (!CanPlay(clip, out var source))
            {
                return false;
            }

            source.transform.position = position;
            ConfigureSpatial(source, maxDistance);
            Play(source, clip, volume, pitchJitter);
            return true;
        }

        /// <summary>
        /// 播放一条不随距离衰减的音效（界面与结算提示）。
        /// </summary>
        /// <param name="clip">音频剪辑，为 null 时直接忽略。</param>
        /// <param name="volume">音量（0~1）。</param>
        /// <param name="pitchJitter">音高抖动幅度。</param>
        /// <returns>成功播放返回 true。</returns>
        public bool PlayFlat(AudioClip clip, float volume = 1f, float pitchJitter = 0f)
        {
            if (clip == null || m_FlatVoices == null || m_FlatVoices.Length == 0)
            {
                return false;
            }

            var source = m_FlatVoices[m_NextFlat];
            m_NextFlat = (m_NextFlat + 1) % m_FlatVoices.Length;
            Play(source, clip, volume, pitchJitter);
            return true;
        }

        /// <summary>
        /// 按顺序取下一个声音变体所需的下标（供调用方轮换多条同类音效）。
        /// </summary>
        /// <returns>0 到 <paramref name="count"/>-1 之间的随机下标。</returns>
        /// <remarks>用随机而不是严格轮换：轮换在连射时会形成"三条一循环"的规律感，
        /// 听起来像节拍器。</remarks>
        public int NextVariantIndex(int count)
        {
            if (count <= 1)
            {
                return 0;
            }

            m_Random ??= new System.Random(RandomSeed);
            return m_Random.Next(count);
        }

        /// <summary>立即停止全部声部（离开战局、返回主菜单时调用）。</summary>
        public void StopAll()
        {
            StopAll(m_SpatialVoices);
            StopAll(m_FlatVoices);
            m_LastPlayedTimes.Clear();
            m_ThrottleKeys.Clear();
        }

        private static void StopAll(AudioSource[] sources)
        {
            if (sources == null)
            {
                return;
            }

            foreach (var source in sources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }
        }

        /// <summary>通用播放前置检查：素材存在、声部就绪、未被节流。</summary>
        private bool CanPlay(AudioClip clip, out AudioSource source)
        {
            source = null;
            if (clip == null || m_SpatialVoices == null || m_SpatialVoices.Length == 0)
            {
                return false;
            }

            var now = Time.unscaledTime;
            if (m_LastPlayedTimes.TryGetValue(clip, out var last) &&
                AudioPlaybackRules.ShouldThrottle(last, now, AudioPlaybackRules.DefaultMinRetriggerInterval))
            {
                return false;
            }

            m_LastPlayedTimes[clip] = now;
            source = m_SpatialVoices[m_NextSpatial];
            m_NextSpatial = (m_NextSpatial + 1) % m_SpatialVoices.Length;

            // 节流表按剪辑累积，长时间运行会留下大量已不再播放的条目；
            // 超过阈值时整表重建，代价是一次几百项的写入，远小于持续增长的内存。
            if (m_LastPlayedTimes.Count > 256)
            {
                PruneThrottleTable(now);
            }

            return true;
        }

        /// <summary>清理超过一秒没有播放过的节流记录。</summary>
        private void PruneThrottleTable(float now)
        {
            m_ThrottleKeys.Clear();
            foreach (var pair in m_LastPlayedTimes)
            {
                if (now - pair.Value > 1f)
                {
                    m_ThrottleKeys.Add(pair.Key);
                }
            }

            foreach (var key in m_ThrottleKeys)
            {
                m_LastPlayedTimes.Remove(key);
            }
        }

        /// <summary>设置空间参数。同一个声部被不同距离的音效复用时必须重设。</summary>
        private static void ConfigureSpatial(AudioSource source, float maxDistance)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinDistance;
            source.maxDistance = Mathf.Max(MinDistance + 1f, maxDistance);
        }

        /// <summary>真正播出去：写入音量与随机音高。</summary>
        private void Play(AudioSource source, AudioClip clip, float volume, float pitchJitter)
        {
            var jitter = Mathf.Clamp(pitchJitter, 0f, 0.3f);
            source.clip = clip;
            source.pitch = jitter <= 0f
                ? 1f
                : 1f + (((float)m_Random.NextDouble() * 2f) - 1f) * jitter;
            source.volume = Mathf.Clamp01(volume) * m_MasterVolume;
            source.Play();
        }

        /// <summary>创建声部（幂等）。</summary>
        private void EnsureVoices()
        {
            if (m_SpatialVoices == null)
            {
                m_SpatialVoices = CreateVoices(SpatialVoiceCount, spatial: true);
            }

            if (m_FlatVoices == null)
            {
                m_FlatVoices = CreateVoices(FlatVoiceCount, spatial: false);
            }
        }

        private AudioSource[] CreateVoices(int count, bool spatial)
        {
            var voices = new AudioSource[count];
            for (var i = 0; i < count; i++)
            {
                var host = new GameObject(spatial ? $"Voice3D_{i:00}" : $"Voice2D_{i:00}");
                host.transform.SetParent(transform, worldPositionStays: false);
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = spatial ? 1f : 0f;
                source.bypassReverbZones = true;
                source.priority = spatial ? 128 : 64;
                voices[i] = source;
            }

            return voices;
        }
    }
}
