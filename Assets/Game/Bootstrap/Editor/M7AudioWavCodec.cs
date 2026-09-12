using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 音效加工用到的音频编解码：读 WAV / 读 Unity 已导入的剪辑 / 降采样 / 写 16 位单声道 WAV。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="M7AudioBaker"/> 分开放，是因为两者关心的事情不同：
    /// 本类只处理"字节与采样"，不知道枪声该剪多长、该归一化到多少；
    /// 加工器只处理"怎么剪"，不关心 WAV 块结构。这样各自都在 400 行以内，也各自能被单独替换。</para>
    /// </remarks>
    internal static class M7AudioWavCodec
    {
        /// <summary>
        /// 加工后的采样率。
        /// </summary>
        /// <remarks>
        /// 枪声库的源文件是 96 kHz，比游戏需要的采样率高一倍。降到 48 kHz 把每条剪辑的
        /// 内存与文件体积直接减半，听感上没有区别：48 kHz 的奈奎斯特上限是 24 kHz，已超出人耳范围。
        /// </remarks>
        public const int TargetSampleRate = 48000;

        /// <summary>解码后的音频数据（统一成 -1~1 的浮点双声道）。</summary>
        internal sealed class WavData
        {
            public float[] Left;
            public float[] Right;
            public int SampleRate;
            public int FrameCount;
        }

        /// <summary>
        /// 读取源素材：WAV 走本类的解码器（保留 24 位精度），其余交给 Unity 解码。
        /// </summary>
        /// <remarks>
        /// Kenney 的打击/界面音效是 OGG，枪声库是 24 位 WAV，两条路都要通。
        /// 分岔放在这里而不是让配方各自声明格式：加素材的人只要写路径，
        /// 由扩展名决定怎么解码，少一个可以填错的字段。
        /// </remarks>
        public static bool TryReadSource(string path, out WavData data, out string error)
        {
            var read = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? TryReadWav(path, out data, out error)
                : TryReadViaUnity(path, out data, out error);
            if (read && data.SampleRate > TargetSampleRate)
            {
                data = Downsample(data, TargetSampleRate);
            }

            return read;
        }

        /// <summary>
        /// 降采样到目标采样率。
        /// </summary>
        /// <remarks>
        /// 用滑动平均（盒式滤波）而不是直接隔点抽取：直接抽取会把 24 kHz 以上的能量折叠回可听频段，
        /// 表现为枪声里多出一层"沙沙"的金属噪声。滑动平均在降采样的同时顺手做了抗混叠。
        /// 只在降采样时调用，升采样没有意义（丢掉的频率不会回来）。
        /// </remarks>
        private static WavData Downsample(WavData source, int targetRate)
        {
            var ratio = source.SampleRate / (double)targetRate;
            var frames = Mathf.Max(1, (int)(source.FrameCount / ratio));
            var window = Mathf.Max(1, Mathf.CeilToInt((float)ratio));
            var result = new WavData
            {
                SampleRate = targetRate,
                FrameCount = frames,
                Left = new float[frames],
                Right = new float[frames]
            };

            for (var i = 0; i < frames; i++)
            {
                var start = Mathf.Min((int)(i * ratio), source.FrameCount - 1);
                var end = Mathf.Min(start + window, source.FrameCount);
                var left = 0f;
                var right = 0f;
                for (var j = start; j < end; j++)
                {
                    left += source.Left[j];
                    right += source.Right[j];
                }

                var count = Mathf.Max(1, end - start);
                result.Left[i] = left / count;
                result.Right[i] = right / count;
            }

            return result;
        }

        /// <summary>
        /// 借助 Unity 的导入结果解码 OGG 等压缩音频。
        /// </summary>
        /// <remarks>
        /// <para>必须先确保素材是「解压到内存」：<see cref="AudioClip.GetData"/> 对流式加载的剪辑无效，
        /// 会直接抛异常。因此这里先改导入设置再读——源素材位于暂存区、不随仓库提交，
        /// 改它的导入设置没有副作用。</para>
        /// <para>另一个坑是读取必须在主线程：本工具由编辑器菜单触发，天然满足。</para>
        /// </remarks>
        private static bool TryReadViaUnity(string path, out WavData data, out string error)
        {
            data = null;
            error = null;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                error = "Unity 未能导入该素材（可能还在导入中，稍后重试）";
                return false;
            }

            if (AssetImporter.GetAtPath(path) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }

            if (clip == null)
            {
                error = "重新导入后仍取不到音频剪辑";
                return false;
            }

            var channels = Mathf.Max(1, clip.channels);
            var frames = clip.samples;
            var interleaved = new float[frames * channels];
            if (!clip.GetData(interleaved, 0))
            {
                error = "剪辑数据不可读（可能是流式加载）";
                return false;
            }

            var result = new WavData
            {
                SampleRate = clip.frequency,
                FrameCount = frames,
                Left = new float[frames],
                Right = new float[frames]
            };

            for (var i = 0; i < frames; i++)
            {
                result.Left[i] = interleaved[i * channels];
                result.Right[i] = channels > 1 ? interleaved[(i * channels) + 1] : result.Left[i];
            }

            data = result;
            return true;
        }

        /// <summary>
        /// 读取 WAV。支持 16 / 24 / 32 位整数与 32 位浮点，单声道与立体声。
        /// </summary>
        /// <remarks>
        /// 不直接读 Unity 的 <see cref="AudioClip"/>：源素材是 24 位 PCM，
        /// Unity 导入时会先降到 16 位，再读回来等于白白丢一次精度；
        /// 而且未导入完成的素材在编辑器里还拿不到。
        /// </remarks>
        private static bool TryReadWav(string path, out WavData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length < 44)
                {
                    error = "文件过短";
                    return false;
                }

                if (!TryReadHeader(bytes, out var header, out error))
                {
                    return false;
                }

                var frames = header.DataLength / header.FrameBytes;
                var result = new WavData
                {
                    SampleRate = header.SampleRate,
                    FrameCount = frames,
                    Left = new float[frames],
                    Right = new float[frames]
                };

                for (var i = 0; i < frames; i++)
                {
                    var position = header.DataOffset + (i * header.FrameBytes);
                    result.Left[i] = ReadSample(bytes, position, header.Bits, header.Format);
                    result.Right[i] = header.Channels > 1
                        ? ReadSample(bytes, position + header.BytesPerSample, header.Bits, header.Format)
                        : result.Left[i];
                }

                data = result;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>WAV 头部解析结果。</summary>
        private readonly struct Header
        {
            public Header(int format, int channels, int sampleRate, int bits, int dataOffset, int dataLength)
            {
                Format = format;
                Channels = channels;
                SampleRate = sampleRate;
                Bits = bits;
                DataOffset = dataOffset;
                DataLength = dataLength;
            }

            public int Format { get; }

            public int Channels { get; }

            public int SampleRate { get; }

            public int Bits { get; }

            public int DataOffset { get; }

            public int DataLength { get; }

            public int BytesPerSample => Bits / 8;

            public int FrameBytes => BytesPerSample * Channels;
        }

        /// <summary>遍历 RIFF 块，找到 fmt 与 data。</summary>
        private static bool TryReadHeader(byte[] bytes, out Header header, out string error)
        {
            header = default;
            error = null;

            var offset = 12;
            var format = 1;
            var channels = 1;
            var sampleRate = 44100;
            var bits = 16;
            var dataOffset = -1;
            var dataLength = 0;

            while (offset + 8 <= bytes.Length)
            {
                var chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
                var body = offset + 8;
                if (chunkId == "fmt ")
                {
                    format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = body;
                    dataLength = Mathf.Min(chunkSize, bytes.Length - body);
                    break;
                }

                offset = body + chunkSize + (chunkSize % 2);
            }

            if (dataOffset < 0)
            {
                error = "没有 data 块";
                return false;
            }

            if (bits / 8 <= 0)
            {
                error = "位深不受支持";
                return false;
            }

            header = new Header(format, channels, sampleRate, bits, dataOffset, dataLength);
            return true;
        }

        /// <summary>读取一个采样并归一化到 -1~1。</summary>
        private static float ReadSample(byte[] bytes, int position, int bits, int format)
        {
            if (format == 3 && bits == 32)
            {
                return BitConverter.ToSingle(bytes, position);
            }

            switch (bits)
            {
                case 16:
                    return BitConverter.ToInt16(bytes, position) / 32768f;
                case 24:
                    var value = bytes[position] | (bytes[position + 1] << 8) | (bytes[position + 2] << 16);
                    if ((value & 0x800000) != 0)
                    {
                        value -= 0x1000000;
                    }

                    return value / 8388608f;
                case 32:
                    return BitConverter.ToInt32(bytes, position) / 2147483648f;
                default:
                    return 0f;
            }
        }

        /// <summary>写一个 16 位单声道 WAV 文件。</summary>
        public static void WriteMono16(string path, float[] samples, int sampleRate)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            var dataBytes = samples.Length * 2;
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);

            foreach (var sample in samples)
            {
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), short.MinValue, short.MaxValue));
            }
        }
    }
}
