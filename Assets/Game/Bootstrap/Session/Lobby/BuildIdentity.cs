using System;
using System.Text;
using RaidDemo.HotUpdate;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 版本标识 <c>buildId</c> 的组装与联机握手判定（M10 第 13.1 节）。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题：</b>联机两端如果跑的不是同一版本体，协议字段、规则数值、
    /// 甚至场景内容都会对不上。症状通常不是"连不上"，而是更难查的那一类——
    /// 进房成功、一进图就错乱，或者点了按钮像没反应。与其让玩家在战局里撞上这种怪事，
    /// 不如在进房这一步就拒掉，并把两个版本号明说出来。</para>
    ///
    /// <para><b>三段各是什么：</b></para>
    /// <list type="bullet">
    /// <item><c>bodyVersion</c>：本体版本（<see cref="Application.version"/>，构建时由
    /// <c>PlayerSettings.bundleVersion</c> 写入）。它是握手的<b>唯一判据</b>；</item>
    /// <item><c>contentHash8</c>：资源层版本（<see cref="HotUpdateRuntime.ContentVersion"/> 的最后一段，
    /// 形如 <c>0.10.0.c6f6221e7</c> → <c>c6f6221e7</c>）。它进标识是为了排查问题时能看清
    /// "这个人装的是哪一版资源"，<b>不参与</b>握手判定——资源差异由 Addressables 的寻址吸收，
    /// 拿它当拒绝条件会让"资源热更过的客户端"连不上没有资源层的服务器（服务器是版本锚点，
    /// 它自己没有内容层）；</item>
    /// <item><c>codeHash8</c>：代码段，当前恒为空（代码更新走启动器本体更新，
    /// 见 <c>Docs/03_模块设计/11_热更新与分发.md</c> 第 12 节）。保留该段是为了将来启用
    /// 进程内代码热更时不必改协议。</item>
    /// </list>
    ///
    /// <para><b>缺失即放行：</b>没有上报 <c>buildId</c> 的客户端按"版本未知"处理并放行。
    /// 这一条是刻意设计的：改动之前构建的老客户端根本没有这个字段，
    /// 若按"不一致"处理，玩家升级服务器之后就会全体进不去。
    /// 握手防的是"版本不匹配造成的状态错乱"，不是恶意客户端——它不是安全边界。</para>
    ///
    /// <para><b>为什么不缓存 <see cref="Current"/>：</b>资源热更可能在本进程运行期间生效
    /// （进安全屋时检查更新并重写寻址），内容段因此会变。这个属性一次会话里只被读几次
    /// （发请求、做握手），现场拼装比"维护一份可能过期的缓存"更省心。</para>
    /// </remarks>
    public static class BuildIdentity
    {
        /// <summary>段与段之间的分隔符。</summary>
        /// <remarks>用加号是因为它不会出现在语义化版本号里，解析时"第一个加号之前"就是本体版本。</remarks>
        public const char SegmentSeparator = '+';

        /// <summary>
        /// <c>buildId</c> 的长度上限。
        /// </summary>
        /// <remarks>
        /// 取值来自协议字段：请求消息里它是 <c>FixedString64Bytes</c>，
        /// 这种定长字符串要留出 2 字节长度前缀与 1 字节结束符，实际可用 <c>64 - 3 = 61</c> 字节。
        /// 拼装时按这个上限截断，避免"写进协议时被静默截掉半截"这种难以定位的现象。
        /// </remarks>
        public const int MaxLength = 61;

        /// <summary>
        /// 本体版本段的长度上限。
        /// </summary>
        /// <remarks>
        /// 比摘要段宽松：本体版本来自构建配置，可能带预发布后缀（<c>0.10.1-beta.3</c>），
        /// 而摘要段是定长哈希。上限存在的意义是"不让人把整段日志塞进标识里"。
        /// </remarks>
        private const int MaxBodySegmentLength = 32;

        /// <summary>单个"摘要段"的长度上限（内容段与代码段用）。</summary>
        private const int MaxSegmentLength = 16;

        /// <summary>
        /// 当前进程的版本标识。
        /// </summary>
        /// <remarks>
        /// <para>优先用启动参数 <c>-buildid</c> 给的值（启动器在拉起游戏时会算好并传入，
        /// 演练时也用它来模拟"旧版本客户端"）；没有传参时现场拼装：
        /// 本体版本取 <see cref="Application.version"/>，资源段取当前生效的内容版本。</para>
        ///
        /// <para>服务器进程同样走这里：服务器是版本锚点，它通常不带更新源，
        /// 于是资源段为空，拼出来就是"仅本体"的标识。</para>
        /// </remarks>
        public static string Current
        {
            get
            {
                var options = LaunchOptions.Current;
                var overridden = options != null ? options.BuildIdOverride : null;
                if (!string.IsNullOrEmpty(overridden))
                {
                    return overridden;
                }

                return Compose(Application.version, HotUpdateRuntime.ContentVersion, string.Empty);
            }
        }

        /// <summary>
        /// 拼装一条 <c>buildId</c>。
        /// </summary>
        /// <param name="bodyVersion">本体版本（如 <c>0.10.1</c>）。</param>
        /// <param name="contentVersion">资源层版本（如 <c>0.10.0.c6f6221e7</c>）；空表示没有内容层。</param>
        /// <param name="codeHash">代码段；当前恒为空。</param>
        /// <returns><c>本体+资源+代码</c> 形态的标识；空段自动省略。</returns>
        /// <remarks>
        /// 段内的字符会被过滤成"字母 / 数字 / <c>.</c> / <c>-</c> / <c>_</c>"：
        /// 分隔符本身混进段里会让解析产生歧义（"第一个加号之前是本体版本"就不再成立），
        /// 而这三个来源都不该含有其它字符，过滤只是防御。
        ///
        /// <para>两级长度约束：单段各有上限（本体 32、摘要 16），总长再按
        /// <see cref="MaxLength"/> 收口。三段都顶满时总长会超过协议容量，收口那一步就是为它准备的——
        /// 标识是要写进 <c>FixedString64Bytes</c> 的，超了会被静默截掉，而"截断发生在哪一段"
        /// 是无法从日志看出来的。</para>
        /// </remarks>
        public static string Compose(string bodyVersion, string contentVersion, string codeHash)
        {
            var builder = new StringBuilder(MaxLength);
            AppendSegment(builder, bodyVersion, MaxBodySegmentLength);
            AppendSegment(builder, ContentSegment(contentVersion), MaxSegmentLength);
            AppendSegment(builder, codeHash, MaxSegmentLength);

            var text = builder.ToString();
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
        }

        /// <summary>
        /// 取资源层版本里的"内容摘要"段：最后一个点号之后的部分。
        /// </summary>
        /// <param name="contentVersion">内容版本（<c>0.10.0.c6f6221e7</c>）。</param>
        /// <returns>摘要段（<c>c6f6221e7</c>）；空输入返回空串。</returns>
        /// <remarks>
        /// 内容版本的前半段是"构建时用的本体版本"，与本体段重复，
        /// 真正标识"这批资源是哪一份"的只有最后一段哈希。
        /// </remarks>
        public static string ContentSegment(string contentVersion)
        {
            if (string.IsNullOrEmpty(contentVersion))
            {
                return string.Empty;
            }

            var trimmed = contentVersion.Trim();
            var index = trimmed.LastIndexOf('.');
            return index < 0 ? trimmed : trimmed.Substring(index + 1);
        }

        /// <summary>
        /// 取出 <c>buildId</c> 里的本体版本段（第一个分隔符之前的部分）。
        /// </summary>
        /// <param name="buildId">版本标识；空输入返回空串。</param>
        public static string BodyVersionOf(string buildId)
        {
            if (string.IsNullOrEmpty(buildId))
            {
                return string.Empty;
            }

            var trimmed = buildId.Trim();
            var index = trimmed.IndexOf(SegmentSeparator);
            var body = index < 0 ? trimmed : trimmed.Substring(0, index);
            return body.Trim();
        }

        /// <summary>
        /// 握手判定：客户端的版本标识与本机（服务器）是否兼容。
        /// </summary>
        /// <param name="clientBuildId">客户端上报的标识；空 = 未上报（老客户端）。</param>
        /// <param name="serverBuildId">本机的标识。</param>
        /// <param name="detail">被拒绝时给玩家看的中文说明；通过时为空串。</param>
        /// <returns>是否允许继续（进房）。</returns>
        /// <remarks>
        /// <para><b>只比本体版本：</b>资源段与代码段不参与判定，理由见类型注释。</para>
        ///
        /// <para><b>任一侧缺失都放行：</b>老客户端不带这个字段；自建构建（例如只跑
        /// <c>-batchmode</c> 的服务器）也可能拿不到版本信息。这两种情况下"未知"不等于"不兼容"，
        /// 拦下来只会制造"明明能玩却进不去"的故障。</para>
        /// </remarks>
        public static bool CheckCompatibility(string clientBuildId, string serverBuildId, out string detail)
        {
            detail = string.Empty;

            var clientBody = BodyVersionOf(clientBuildId);
            var serverBody = BodyVersionOf(serverBuildId);

            if (clientBody.Length == 0 || serverBody.Length == 0)
            {
                return true;
            }

            if (string.Equals(clientBody, serverBody, StringComparison.Ordinal))
            {
                return true;
            }

            detail = $"客户端 {clientBody} 与服务器 {serverBody} 不一致，请更新客户端。";
            return false;
        }

        /// <summary>把一段文本清洗成合法段并追加（含分隔符）。空段不追加。</summary>
        /// <param name="builder">目标缓冲。</param>
        /// <param name="value">原始段文本。</param>
        /// <param name="maxLength">该段的长度上限。</param>
        private static void AppendSegment(StringBuilder builder, string value, int maxLength)
        {
            var segment = SanitizeSegment(value, maxLength);
            if (segment.Length == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(SegmentSeparator);
            }

            builder.Append(segment);
        }

        /// <summary>过滤段内字符并截断到长度上限。</summary>
        private static string SanitizeSegment(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(maxLength);
            for (var i = 0; i < value.Length && builder.Length < maxLength; i++)
            {
                var c = value[i];
                if (IsSegmentCharacter(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>段内允许的字符：字母、数字与 <c>. - _</c>。</summary>
        private static bool IsSegmentCharacter(char c)
        {
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                return true;
            }

            return c == '.' || c == '-' || c == '_';
        }
    }
}
