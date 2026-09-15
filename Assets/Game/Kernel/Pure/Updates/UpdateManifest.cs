using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel.Updates
{
    /// <summary>
    /// 更新清单（<c>manifest.json</c>，协议 v1）的数据模型。
    /// </summary>
    /// <remarks>
    /// <para><b>它是三层更新的唯一契约</b>：本体层（启动器消费）、资源层与代码层（游戏内消费）
    /// 共用同一个文件。任何一侧改了字段含义，都必须同时改协议版本号
    /// （<see cref="SchemaVersion"/>），否则老客户端会拿着新清单做出错误判断。</para>
    ///
    /// <para><b>为什么模型放在纯逻辑层：</b>启动器（<c>Tools/Launcher</c>）是独立的 .NET 程序、
    /// 不能引用 Unity 程序集，因此它自己带一份等价模型；而游戏内（批次 4/5 的热更流程）读的是这一份。
    /// 两边共享的只是**字段名与语义**，不是代码——所以字段名一旦发布就不能再改，
    /// 只能通过提升 <see cref="SchemaVersion"/> 来演进。</para>
    ///
    /// <para><b>为什么字段是 public 且没有属性：</b>Unity 的 <c>JsonUtility</c> 只序列化
    /// public 字段（或带 <c>[SerializeField]</c> 的私有字段），不支持属性。
    /// 这里刻意保持"数据类"形态，不掺行为，方便与 <c>JsonUtility</c> 及后续可能的手写解析器共存。</para>
    ///
    /// <para><b>空层 = 无更新，判据是 <c>version</c> 为空字符串</b>：三层字段在序列化后**总是存在**——
    /// Unity 的 <c>JsonUtility</c> 会把值为 <c>null</c> 的类字段写成"字段齐全但内容为空"的对象
    /// （实测：<c>"content": { "version": "", "catalog": {...}, "files": [] }</c>），
    /// 而不是写成 <c>null</c>。因此消费方一律用
    /// <see cref="HasBodyLayer"/> / <see cref="HasContentLayer"/> / <see cref="HasCodeLayer"/>
    /// 判断某层是否存在，不要依赖 <c>null</c> 判断。</para>
    /// </remarks>
    [Serializable]
    public sealed class UpdateManifest
    {
        /// <summary>协议版本。当前为 1；消费方遇到不认识的值必须拒绝更新并提示升级。</summary>
        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>清单生成时间（ISO 8601，带时区偏移），仅用于展示与排障，不参与比对。</summary>
        public string generatedAt = string.Empty;

        /// <summary>本体层：exe / UnityPlayer.dll / 数据目录等，由启动器在游戏进程外替换。</summary>
        public ManifestLayer body;

        /// <summary>资源层：Addressables 的 catalog 与 bundle；无更新时为 <c>null</c>。</summary>
        public ManifestContentLayer content;

        /// <summary>代码层：热更程序集与 AOT 补充元数据；无更新时为 <c>null</c>。</summary>
        public ManifestCodeLayer code;

        /// <summary>当前协议版本常量。发布后的清单里这个值是唯一被信任的格式标识。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>本体层是否存在（<c>version</c> 非空即为存在）。</summary>
        public bool HasBodyLayer
        {
            get { return body != null && !string.IsNullOrEmpty(body.version); }
        }

        /// <summary>资源层是否存在。首个版本（批次 1）里这一层为空。</summary>
        public bool HasContentLayer
        {
            get { return content != null && !string.IsNullOrEmpty(content.version); }
        }

        /// <summary>代码层是否存在。批次 5 之前这一层为空。</summary>
        public bool HasCodeLayer
        {
            get { return code != null && !string.IsNullOrEmpty(code.version); }
        }

        /// <summary>
        /// 生成 ISO 8601 时间戳（本地时区，带偏移）。
        /// </summary>
        /// <remarks>
        /// 刻意不用 <c>DateTime.UtcNow</c>：排障时"几点几分"要与开发者的本地时间对得上，
        /// 偏移量会让这一点保持无歧义。
        /// </remarks>
        public static string CreateTimestamp()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        }
    }

    /// <summary>
    /// 单层（本体 / 资源 / 代码）的清单块。
    /// </summary>
    /// <remarks>
    /// 三层结构一致，唯独资源层多一个 <c>catalog</c> 入口文件（见 <see cref="ManifestContentLayer"/>）。
    /// </remarks>
    [Serializable]
    public sealed class ManifestLayer
    {
        /// <summary>该层版本号（本体为语义化版本，例 <c>0.10.0</c>）。</summary>
        public string version = string.Empty;

        /// <summary>该层全部文件（相对路径 + 大小 + SHA-256）。</summary>
        public List<ManifestFileEntry> files = new List<ManifestFileEntry>();
    }

    /// <summary>
    /// 资源层清单块：比普通层多一个 catalog 入口。
    /// </summary>
    /// <remarks>
    /// Addressables 的加载入口是 catalog（目录文件），bundle 是它引用的数据。
    /// 把 catalog 单独列出来，是为了让消费方"先取 catalog、再按需取 bundle"，
    /// 也避免把入口文件与普通数据混在一个列表里靠命名约定区分。
    /// </remarks>
    [Serializable]
    public sealed class ManifestContentLayer
    {
        /// <summary>资源层版本号（例 <c>0.10.0.3</c>）。</summary>
        public string version = string.Empty;

        /// <summary>Addressables catalog 入口文件；缺失时视为无资源更新。</summary>
        public ManifestFileEntry catalog;

        /// <summary>全部 bundle 文件。</summary>
        public List<ManifestFileEntry> files = new List<ManifestFileEntry>();
    }

    /// <summary>
    /// 代码层清单块：热更程序集与 AOT 补充元数据分开列。
    /// </summary>
    /// <remarks>
    /// <para>两者用途不同，不能混：程序集是"要被执行的代码"，补充元数据是"给解释器补齐 AOT
    /// 泛型信息的数据"。少了元数据会在运行时报"泛型方法找不到"，而且报错位置离原因很远，
    /// 所以这里分列两份清单，便于加载前逐项校验。</para>
    /// </remarks>
    [Serializable]
    public sealed class ManifestCodeLayer
    {
        /// <summary>代码层版本号（例 <c>0.10.0.7</c>）。</summary>
        public string version = string.Empty;

        /// <summary>热更程序集（<c>RaidDemo.*.dll</c>）。</summary>
        public List<ManifestFileEntry> assemblies = new List<ManifestFileEntry>();

        /// <summary>AOT 补充元数据 DLL。</summary>
        public List<ManifestFileEntry> metadata = new List<ManifestFileEntry>();
    }

    /// <summary>
    /// 清单里的一个文件条目。
    /// </summary>
    [Serializable]
    public sealed class ManifestFileEntry
    {
        /// <summary>
        /// 相对路径，一律使用正斜杠（例 <c>RaidDemo_Data/Managed/RaidDemo.Combat.dll</c>）。
        /// </summary>
        /// <remarks>
        /// 反斜杠只作为分隔符在 Windows 上有意义，写进清单会让"同一份文件在两端算出不同的键"，
        /// 因此协议层统一正斜杠，两端在拼接本地路径时各自转换。
        /// </remarks>
        public string path = string.Empty;

        /// <summary>文件字节数。用于下载前预估与快速失败（真正的完整性判据是哈希）。</summary>
        public long size;

        /// <summary>小写十六进制的 SHA-256。</summary>
        public string sha256 = string.Empty;

        /// <summary>
        /// 把路径规范化为清单使用的格式：反斜杠转正斜杠、去掉开头的 <c>./</c>。
        /// </summary>
        /// <param name="path">任意来源的相对路径。</param>
        /// <returns>规范化后的相对路径。</returns>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var normalized = path.Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized.TrimStart('/');
        }
    }
}
