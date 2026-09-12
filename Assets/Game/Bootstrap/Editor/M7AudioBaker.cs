using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 音效加工器：把外部音效库里的「整段录音」剪成「一发一份」的短音频。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须加工，不能直接用原文件：</b>The Free Firearm Sound Library 的每条录音
    /// 都是 8~29 秒的完整素材（枪响加长尾混响），单条 5~16 MB、立体声 96 kHz 24 位。
    /// 直接拿去当「每开一枪播一次」会有三个后果：每次开枪要等半秒才听到声音（录音开头是环境底噪）、
    /// 声音拖到下一发还在响、以及几十 MB 的音频常驻内存。</para>
    /// <para><b>加工做四件事：</b>其一，按 1 毫秒窗口扫描 RMS 自动定位起爆点，
    /// 从它前面 5 毫秒开始剪，保证枪声与画面同步；其二，只保留设计时长（枪声 0.9 秒、
    /// 撞击 0.5 秒）并在末尾 30 毫秒线性淡出，避免硬切产生爆音；其三，混成单声道，
    /// 因为 Unity 的 3D 空间音效只对单声道剪辑做声像与距离衰减；其四，降到 48 kHz，
    /// 把文件与内存占用直接减半。</para>
    /// <para>结果写成 48 kHz / 16 位单声道 WAV，落盘到 <c>Content/Audio/Sfx/</c> 并随仓库提交：
    /// 音效库本体留在暂存区不入库，仓库里只保留真正用到的那几秒。
    /// 具体的编解码在 <see cref="M7AudioWavCodec"/>。</para>
    /// </remarks>
    public static class M7AudioBaker
    {
        /// <summary>起爆点判定阈值：窗口 RMS 超过整段峰值的这个比例即认为「响了」。</summary>
        private const float TransientThresholdRatio = 0.08f;

        /// <summary>起爆点之前额外保留的静音时长（秒），保证起振不被削掉。</summary>
        private const float PreRollSeconds = 0.005f;

        /// <summary>末尾淡出时长（秒）。</summary>
        private const float FadeOutSeconds = 0.03f;

        /// <summary>开头淡入时长（秒）：极短，只为消除直流跳变。</summary>
        private const float FadeInSeconds = 0.002f;

        /// <summary>
        /// 一段加工配方。
        /// </summary>
        /// <remarks>
        /// <see cref="TargetPeak"/> 是归一化目标：把每条素材的峰值统一到这个电平，
        /// 再由音效目录里的音量参数做混音。若不做归一化，同一把枪的「近距」与「中距」两条录音
        /// 会相差十几个 dB，玩家听起来像两把不同的枪。
        /// </remarks>
        public readonly struct AudioRecipe
        {
            /// <summary>创建配方。</summary>
            /// <param name="sourcePath">源素材的工程相对路径（WAV 或 OGG）。</param>
            /// <param name="targetPath">加工后的工程相对路径。</param>
            /// <param name="seconds">保留时长（秒），从起爆点开始算。</param>
            /// <param name="targetPeak">归一化目标峰值（0~1）。</param>
            public AudioRecipe(string sourcePath, string targetPath, float seconds, float targetPeak)
            {
                SourcePath = sourcePath;
                TargetPath = targetPath;
                Seconds = seconds;
                TargetPeak = targetPeak;
            }

            /// <summary>源素材路径（工程相对）。</summary>
            public string SourcePath { get; }

            /// <summary>加工结果路径（工程相对）。</summary>
            public string TargetPath { get; }

            /// <summary>保留时长（秒）。</summary>
            public float Seconds { get; }

            /// <summary>归一化目标峰值。</summary>
            public float TargetPeak { get; }
        }

        /// <summary>
        /// 按配方加工一条音效。
        /// </summary>
        /// <param name="recipe">加工配方。</param>
        /// <param name="detail">成功时返回一行可读的处理摘要；失败时返回失败原因。</param>
        /// <returns>成功返回 true。</returns>
        public static bool Bake(AudioRecipe recipe, out string detail)
        {
            if (!File.Exists(recipe.SourcePath))
            {
                detail = $"{recipe.TargetPath}（跳过：缺少源文件 {recipe.SourcePath}）";
                return false;
            }

            if (!M7AudioWavCodec.TryReadSource(recipe.SourcePath, out var source, out var readError))
            {
                detail = $"{recipe.TargetPath}（读取失败：{readError}）";
                return false;
            }

            var transient = FindTransientIndex(source);
            var mono = BuildMonoSegment(source, transient, recipe.Seconds, recipe.TargetPeak);
            if (mono == null || mono.Length == 0)
            {
                detail = $"{recipe.TargetPath}（裁剪结果为空）";
                return false;
            }

            var directory = Path.GetDirectoryName(recipe.TargetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            M7AudioWavCodec.WriteMono16(recipe.TargetPath, mono, source.SampleRate);
            AssetDatabase.ImportAsset(recipe.TargetPath, ImportAssetOptions.ForceUpdate);
            ApplySfxImportSettings(recipe.TargetPath);

            detail = $"{recipe.TargetPath}（{mono.Length / (float)source.SampleRate:F2} 秒 / " +
                     $"起爆点 {transient / (float)source.SampleRate:F2} 秒）";
            return true;
        }

        /// <summary>
        /// 把加工结果设为「短音效」的导入参数。
        /// </summary>
        /// <remarks>
        /// 必须显式设置而不是吃默认值：Unity 对长音频默认走压缩流式加载，
        /// 枪声这类几十毫秒就要响的短音效一旦被流式解码，会出现「第一枪没声音、第二枪才响」
        /// ——解码器还在缓冲。PCM 加预加载的代价只是几百 KB 内存，换来的是零延迟触发。
        /// </remarks>
        private static void ApplySfxImportSettings(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is AudioImporter importer))
            {
                return;
            }

            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 找到起爆点：按 1 毫秒窗口扫描各声道平均 RMS，返回第一个超过阈值的采样位置。
        /// </summary>
        /// <remarks>
        /// 扫描窗口而不是逐采样点比较，是因为单点比较会被直流漂移与底噪触发；
        /// 1 毫秒是「枪声起振」与「底噪尖刺」之间的经验分界。
        /// </remarks>
        private static int FindTransientIndex(M7AudioWavCodec.WavData data)
        {
            var window = Mathf.Max(1, data.SampleRate / 1000);
            var limit = Mathf.Max(window, data.FrameCount - window);
            var peak = 0f;
            for (var start = 0; start < limit; start += window)
            {
                var value = WindowRms(data, start, window);
                if (value > peak)
                {
                    peak = value;
                }
            }

            var threshold = peak * TransientThresholdRatio;
            for (var start = 0; start < limit; start += window)
            {
                if (WindowRms(data, start, window) < threshold)
                {
                    continue;
                }

                return Mathf.Max(0, start - (int)(data.SampleRate * PreRollSeconds));
            }

            return 0;
        }

        /// <summary>取一个窗口内的平均 RMS。</summary>
        private static float WindowRms(M7AudioWavCodec.WavData data, int start, int length)
        {
            var end = Mathf.Min(data.FrameCount, start + length);
            if (end <= start)
            {
                return 0f;
            }

            var left = 0f;
            var right = 0f;
            for (var i = start; i < end; i++)
            {
                left += data.Left[i] * data.Left[i];
                right += data.Right[i] * data.Right[i];
            }

            var count = end - start;
            return Mathf.Sqrt(((left + right) * 0.5f) / count);
        }

        /// <summary>
        /// 从起爆点剪出指定时长、混为单声道、归一化并加淡入淡出。
        /// </summary>
        private static float[] BuildMonoSegment(M7AudioWavCodec.WavData data, int start, float seconds, float targetPeak)
        {
            var length = Mathf.Min((int)(data.SampleRate * seconds), data.FrameCount - start);
            if (length <= 1)
            {
                return null;
            }

            var mono = new float[length];
            var peak = 0f;
            for (var i = 0; i < length; i++)
            {
                var value = (data.Left[start + i] + data.Right[start + i]) * 0.5f;
                mono[i] = value;
                var magnitude = Mathf.Abs(value);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            // 归一化：所有素材统一到同一峰值，混音只由音效目录的音量参数决定。
            var gain = peak > 1e-4f ? targetPeak / peak : 1f;
            var fadeIn = Mathf.Max(1, (int)(data.SampleRate * FadeInSeconds));
            var fadeOut = Mathf.Max(1, (int)(data.SampleRate * FadeOutSeconds));
            for (var i = 0; i < length; i++)
            {
                var value = mono[i] * gain;
                if (i < fadeIn)
                {
                    value *= i / (float)fadeIn;
                }

                var tail = length - 1 - i;
                if (tail < fadeOut)
                {
                    value *= tail / (float)fadeOut;
                }

                mono[i] = Mathf.Clamp(value, -1f, 1f);
            }

            return mono;
        }
    }
}
