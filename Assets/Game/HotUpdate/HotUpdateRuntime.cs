using System;
using System.Collections;
using System.IO;
using System.Linq;
using RaidDemo.Kernel.Updates;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace RaidDemo.HotUpdate
{
    /// <summary>
    /// 游戏内热更运行时：检查更新 → 下载内容 → 加载 catalog → 重写寻址。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决的问题：</b>Addressables 的"远端目录"能力只负责按 catalog 找 bundle，
    /// 而"从哪里下载、下载到哪、失败了怎么办"是项目自己的事。这一层就是那段项目代码。</para>
    ///
    /// <para><b>三个关键词：</b></para>
    /// <list type="number">
    /// <item><b>版本判断</b>：远端清单里的 <c>content.version</c> 与本地状态比对；一致就什么都不做
    /// （这也是"重复构建同样内容不会让客户端白下载"的实现）；</item>
    /// <item><b>失败回退</b>：任何一步失败都保留上一次成功的内容，游戏照常进得去；
    /// 只有第一次运行且网络不可用时才会退到"本体自带内容"（无内容层，游戏仍能起）；</item>
    /// <item><b>寻址重写</b>：<c>InternalIdTransformFunc</c> 把 catalog 里记录的
    /// <c>content/&lt;文件名&gt;</c> 映射到**本地缓存文件**；缓存里没有时才回落到更新源 URL。
    /// 这一步让"下载过的内容优先用本地"成为唯一规则，也让离线游玩成立。</item>
    /// </list>
    ///
    /// <para><b>服务器同样使用这个类</b>，但它通常不带更新源（服务器是版本锚点），
    /// 于是走的就是"本体自带内容"那条路径。</para>
    /// </remarks>
    public static class HotUpdateRuntime
    {
        /// <summary>本地状态（已生效的内容版本）。</summary>
        private static HotUpdateState s_State = new HotUpdateState();

        /// <summary>当前更新源地址。</summary>
        private static string s_Source = string.Empty;

        /// <summary>是否已经完成一次初始化（避免重复初始化）。</summary>
        private static bool s_Initialized;

        /// <summary>是否已经接管了 Addressables 寻址。</summary>
        private static bool s_TransformInstalled;

        /// <summary>最近一次状态文本（界面与日志用）。</summary>
        public static string StatusText { get; private set; } = "未初始化";

        /// <summary>当前生效的内容版本（空 = 使用本体自带内容）。</summary>
        public static string ContentVersion
        {
            get { return s_State.contentVersion; }
        }

        /// <summary>当前使用的更新源地址。</summary>
        public static string Source
        {
            get { return s_Source; }
        }

        /// <summary>
        /// 配置更新源（通常在场景启动时由启动参数或默认值给出）。
        /// </summary>
        /// <param name="source">更新源地址；空字符串表示不启用热更。</param>
        public static void Configure(string source)
        {
            s_Source = string.IsNullOrWhiteSpace(source) ? string.Empty : source.Trim();
            s_State = HotUpdateState.Load();
        }

        /// <summary>
        /// 执行一次热更初始化（幂等）。
        /// </summary>
        /// <param name="onStatus">状态文本回调（界面显示用）。</param>
        /// <returns>协程。</returns>
        public static IEnumerator Initialize(Action<string> onStatus = null)
        {
            if (s_Initialized)
            {
                onStatus?.Invoke(StatusText);
                yield break;
            }

            s_Initialized = true;
            InstallTransform();

            if (string.IsNullOrEmpty(s_Source))
            {
                SetStatus("未配置更新源，使用本体自带内容", onStatus);
                yield break;
            }

            SetStatus("正在检查资源更新…", onStatus);

            string manifestText = null;
            string error = null;
            yield return HotUpdateClient.FetchManifestText(s_Source, text => manifestText = text, message => error = message);

            if (!string.IsNullOrEmpty(error))
            {
                s_State.lastFailure = error;
                s_State.Save();
                SetStatus("更新源不可用，使用本地内容（" + error + "）", onStatus);
                yield break;
            }

            UpdateManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<UpdateManifest>(manifestText);
            }
            catch (Exception exception)
            {
                SetStatus("清单解析失败，使用本地内容（" + exception.Message + "）", onStatus);
                yield break;
            }

            if (manifest == null || !manifest.HasContentLayer)
            {
                SetStatus("远端没有资源层，使用本地内容", onStatus);
                yield break;
            }

            if (string.Equals(manifest.content.version, s_State.contentVersion, StringComparison.Ordinal))
            {
                SetStatus($"资源已是最新（{manifest.content.version}）", onStatus);
                yield return LoadCatalogAndReport(onStatus);
                yield break;
            }

            var entries = manifest.content.files ?? new System.Collections.Generic.List<ManifestFileEntry>();
            if (manifest.content.catalog != null && !string.IsNullOrEmpty(manifest.content.catalog.path))
            {
                entries = new System.Collections.Generic.List<ManifestFileEntry>(entries);
                if (entries.All(entry => !string.Equals(entry.path, manifest.content.catalog.path, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(manifest.content.catalog);
                }
            }

            SetStatus($"正在下载资源 {manifest.content.version}（{entries.Count} 个文件）…", onStatus);

            var failed = false;
            yield return HotUpdateClient.DownloadContent(
                s_Source,
                manifest.content.version,
                entries,
                (progress, file) => SetStatus($"下载 {file}（{progress * 100:F0}%）", onStatus),
                message =>
                {
                    failed = true;
                    error = message;
                });

            if (failed)
            {
                s_State.lastFailure = error;
                s_State.Save();
                SetStatus("资源下载失败，继续使用本地内容（" + error + "）", onStatus);
                yield break;
            }

            s_State.contentVersion = manifest.content.version;
            s_State.catalogFileName = manifest.content.catalog != null ? HotUpdateClient.GetFileName(manifest.content.catalog.path) : string.Empty;
            s_State.updatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            s_State.lastFailure = string.Empty;
            s_State.Save();

            yield return LoadCatalogAndReport(onStatus);
        }

        /// <summary>
        /// 按地址取资产；取不到或热更未就绪时返回调用方给的兜底引用。
        /// </summary>
        /// <typeparam name="T">资产类型。</typeparam>
        /// <param name="fallback">兜底资产（通常来自场景里的序列化引用）。</param>
        /// <param name="address">Addressables 地址。</param>
        /// <returns>可用的资产实例。</returns>
        /// <remarks>
        /// <para><b>为什么保留兜底：</b>兜底引用来自场景序列化，永远存在；
        /// 而 Addressables 可能因为"没配置更新源 / 内容包缺失"而取不到。
        /// 两者都失效才会真正没数据，而那种情况在工程上意味着打包配置错了，
        /// 不该让玩家用黑屏来发现。</para>
        ///
        /// <para>加载用 <c>WaitForCompletion</c>：内容已在本地（缓存或 StreamingAssets），
        /// 这是毫秒级操作；换成异步会把"初始化顺序"复杂度引入所有调用点，收益不成比例。</para>
        /// </remarks>
        public static T Resolve<T>(T fallback, string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address))
            {
                return fallback;
            }

            // 即使没走过 Initialize（例如直接打开战局场景、或服务器进程），
            // 也要保证寻址重写可用——否则 catalog 里的相对地址会解析失败。
            InstallTransform();

            try
            {
                if (!Addressables.ResourceLocators.Any(locator => locator.Locate(address, typeof(T), out _)))
                {
                    return fallback;
                }

                var handle = Addressables.LoadAssetAsync<T>(address);
                var asset = handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded && asset != null)
                {
                    return asset;
                }

                Addressables.Release(handle);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[热更] 按地址取资产失败（{address}）：{exception.Message}");
            }

            return fallback;
        }

        /// <summary>加载已下载的 catalog，并把结果写进状态文本。</summary>
        private static IEnumerator LoadCatalogAndReport(Action<string> onStatus)
        {
            var catalogPath = s_State.GetCatalogPath();
            if (string.IsNullOrEmpty(catalogPath))
            {
                SetStatus("使用本体自带内容", onStatus);
                yield break;
            }

            var handle = Addressables.LoadContentCatalogAsync(catalogPath);
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                SetStatus("catalog 加载失败，使用本体自带内容", onStatus);
                yield break;
            }

            SetStatus($"资源已生效（{s_State.contentVersion}）", onStatus);
        }

        /// <summary>
        /// 安装寻址重写：把 catalog 里的相对地址映射到本地缓存文件，其次才是更新源 URL。
        /// </summary>
        private static void InstallTransform()
        {
            if (s_TransformInstalled)
            {
                return;
            }

            Addressables.InternalIdTransformFunc = TransformInternalId;
            s_TransformInstalled = true;
        }

        /// <summary>寻址重写实现（规则只有一条：本地有就用本地）。</summary>
        private static string TransformInternalId(IResourceLocation location)
        {
            var internalId = location?.InternalId;
            if (string.IsNullOrEmpty(internalId))
            {
                return internalId;
            }

            var fileName = HotUpdateClient.GetFileName(internalId);
            if (string.IsNullOrEmpty(fileName))
            {
                return internalId;
            }

            if (!string.IsNullOrEmpty(s_State.contentVersion))
            {
                var localPath = HotUpdatePaths.GetVersionFilePath(s_State.contentVersion, fileName);
                if (File.Exists(localPath))
                {
                    return localPath;
                }
            }

            if (HotUpdateClient.IsHttpSource(s_Source))
            {
                // 缓存里没有（例如只下载了 catalog）：直接回落到更新源，
                // 让 Addressables 自己走 UnityWebRequest 取——比"先下载再加载"少一层等待。
                return s_Source.TrimEnd('/') + "/" + HotUpdateClient.ContentUrlPrefix + "/" + Uri.EscapeDataString(fileName);
            }

            return internalId;
        }

        /// <summary>更新状态文本并回调。</summary>
        private static void SetStatus(string text, Action<string> onStatus)
        {
            StatusText = text;
            onStatus?.Invoke(text);
            Debug.Log("[热更] " + text);
        }
    }
}
