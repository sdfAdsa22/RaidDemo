using System.Collections.Generic;

namespace RaidDemo.Kernel.Updates
{
    /// <summary>
    /// 更新清单的完整性校验：一份清单在被消费前必须通过的检查。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么校验要放在共享协议层：</b>启动器与游戏内热更读的是同一份清单，
    /// 却各自有一套"读完之后做什么"的判断。把校验写成一份，两端就不会出现
    /// "启动器拒绝、游戏却照做"或反过来的分叉。</para>
    ///
    /// <para><b>它防的是哪一类缺陷：</b>M13-06——清单缺少 <c>body</c> 层时，
    /// 比对结果自然是"零差异"，界面于是显示"已是最新版本"，把一次发布事故伪装成成功。
    /// 发布不完整必须在消费端就报错，而不是靠人去看版本号。</para>
    /// </remarks>
    public static class UpdateManifestValidation
    {
        /// <summary>校验一份远端清单。</summary>
        /// <param name="manifest">要校验的清单。</param>
        /// <param name="requireBodyLayer">是否要求必须存在完整的本体层（启动器为 <c>true</c>）。</param>
        /// <returns>通过返回 <c>null</c>；失败返回可直接展示的中文原因。</returns>
        public static string Validate(UpdateManifest manifest, bool requireBodyLayer)
        {
            if (manifest == null)
            {
                return "清单为空。";
            }

            if (manifest.schemaVersion != UpdateManifest.CurrentSchemaVersion)
            {
                return $"清单协议版本不受支持（需要 v{UpdateManifest.CurrentSchemaVersion}，" +
                       $"实际 {manifest.schemaVersion}）。";
            }

            if (requireBodyLayer)
            {
                if (manifest.body == null || string.IsNullOrEmpty(manifest.body.version))
                {
                    return "清单缺少本体层（body.version 为空），更新源可能没有发布完整。";
                }

                if (manifest.body.files == null || manifest.body.files.Count == 0)
                {
                    return "清单的本体层没有任何文件，更新源可能没有发布完整。";
                }
            }

            var error = ValidateLayer(manifest.body, "本体层");
            if (error != null)
            {
                return error;
            }

            if (manifest.HasContentLayer)
            {
                if (manifest.content.catalog == null)
                {
                    return "资源层缺少 catalog 入口文件。";
                }

                error = ValidateEntry(manifest.content.catalog, "资源层 catalog");
                if (error != null)
                {
                    return error;
                }

                error = ValidateEntries(manifest.content.files, "资源层的文件");
                if (error != null)
                {
                    return error;
                }
            }

            if (manifest.HasCodeLayer)
            {
                error = ValidateEntries(manifest.code.assemblies, "代码层的程序集");
                if (error != null)
                {
                    return error;
                }

                error = ValidateEntries(manifest.code.metadata, "代码层的补充元数据");
                if (error != null)
                {
                    return error;
                }
            }

            if (!requireBodyLayer && !manifest.HasBodyLayer && !manifest.HasContentLayer && !manifest.HasCodeLayer)
            {
                return "清单不包含任何可更新的层。";
            }

            return null;
        }

        /// <summary>校验一层；空层（version 为空）视为不存在。</summary>
        private static string ValidateLayer(ManifestLayer layer, string label)
        {
            if (layer == null || string.IsNullOrEmpty(layer.version))
            {
                return null;
            }

            return ValidateEntries(layer.files, label + "的文件");
        }

        /// <summary>逐条校验文件清单。</summary>
        private static string ValidateEntries(List<ManifestFileEntry> entries, string label)
        {
            if (entries == null)
            {
                return label + "列表缺失（应为空数组而不是 null）。";
            }

            for (var index = 0; index < entries.Count; index++)
            {
                var error = ValidateEntry(entries[index], $"{label}[{index}]");
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        /// <summary>校验单个条目：路径安全 + 哈希格式 + 大小合法。</summary>
        private static string ValidateEntry(ManifestFileEntry entry, string label)
        {
            if (entry == null)
            {
                return label + "是空条目。";
            }

            if (!ManifestFileEntry.TryNormalizeSafePath(entry.path, out _, out var reason))
            {
                return $"{label} 的路径不安全（{entry.path}）：{reason}。";
            }

            if (string.IsNullOrEmpty(entry.sha256) || entry.sha256.Length != 64)
            {
                return $"{label} 的 SHA-256 缺失或格式不正确（{entry.path}）。";
            }

            if (entry.size < 0)
            {
                return $"{label} 的文件大小非法（{entry.path}）。";
            }

            return null;
        }
    }
}
