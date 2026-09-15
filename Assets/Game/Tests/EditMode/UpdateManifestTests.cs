using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Kernel.Updates;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 更新清单模型与比对逻辑的测试（M10 批次 1）。
    /// </summary>
    /// <remarks>
    /// <para>比对逻辑是三层更新的核心：它一旦算错，现象是"永远提示有更新"或"更新完起不来"，
    /// 而且这两种现象在手动测试里都很容易被误判成网络问题。因此这里把每种情形都固定成用例。</para>
    ///
    /// <para>序列化部分刻意覆盖 <c>long</c> 字段与嵌套列表——<c>JsonUtility</c> 的能力边界
    /// （支持哪些类型、null 怎么写入）必须以实测为准，不能靠印象。</para>
    /// </remarks>
    [TestFixture]
    public sealed class UpdateManifestTests
    {
        /// <summary>构造一个清单条目。</summary>
        private static ManifestFileEntry Entry(string path, long size, string hash)
        {
            return new ManifestFileEntry { path = path, size = size, sha256 = hash };
        }

        [Test]
        public void Diff_EmptyLocal_ReportsEveryRemoteFileAsAdded()
        {
            var remote = new List<ManifestFileEntry>
            {
                Entry("RaidDemo.exe", 100, "aaa"),
                Entry("RaidDemo_Data/level0", 200, "bbb"),
            };

            var changes = UpdateManifestDiff.CompareLayer(new List<ManifestFileEntry>(), remote);

            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(UpdateChangeKind.Added, changes[0].Kind);
            Assert.AreEqual(UpdateChangeKind.Added, changes[1].Kind);
            Assert.AreEqual(300L, UpdateManifestDiff.SumDownloadBytes(changes));
        }

        [Test]
        public void Diff_IdenticalLists_ReportsNoChange()
        {
            var local = new List<ManifestFileEntry> { Entry("a.dll", 10, "hash-a") };
            var remote = new List<ManifestFileEntry> { Entry("a.dll", 10, "hash-a") };

            var changes = UpdateManifestDiff.CompareLayer(local, remote);

            Assert.IsEmpty(changes);
            Assert.IsFalse(UpdateManifestDiff.HasChanges(changes));
            Assert.AreEqual(0L, UpdateManifestDiff.SumDownloadBytes(changes));
        }

        [Test]
        public void Diff_SameSizeDifferentHash_IsReportedAsChanged()
        {
            // 同样的文件大小、不同的内容——只看大小会漏掉这类改动，这正是必须用哈希的原因。
            var local = new List<ManifestFileEntry> { Entry("a.dll", 10, "hash-a") };
            var remote = new List<ManifestFileEntry> { Entry("a.dll", 10, "hash-b") };

            var changes = UpdateManifestDiff.CompareLayer(local, remote);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(UpdateChangeKind.Changed, changes[0].Kind);
            Assert.IsTrue(changes[0].NeedsDownload);
        }

        [Test]
        public void Diff_HashComparisonIsCaseInsensitive()
        {
            // 构建脚本与启动器可能一个输出大写、一个小写；这不该被当成"内容变化"。
            var local = new List<ManifestFileEntry> { Entry("a.dll", 10, "ABCDEF") };
            var remote = new List<ManifestFileEntry> { Entry("a.dll", 10, "abcdef") };

            Assert.IsEmpty(UpdateManifestDiff.CompareLayer(local, remote));
        }

        [Test]
        public void Diff_LocalOnlyFile_IsReportedAsRemoved()
        {
            var local = new List<ManifestFileEntry>
            {
                Entry("keep.dll", 10, "same"),
                Entry("stale.dll", 20, "old"),
            };
            var remote = new List<ManifestFileEntry> { Entry("keep.dll", 10, "same") };

            var changes = UpdateManifestDiff.CompareLayer(local, remote);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(UpdateChangeKind.Removed, changes[0].Kind);
            Assert.AreEqual("stale.dll", changes[0].Path);
            Assert.IsFalse(changes[0].NeedsDownload);
            Assert.AreEqual(0L, UpdateManifestDiff.SumDownloadBytes(changes));
        }

        [Test]
        public void Diff_PathSeparatorsAndCasing_AreNormalized()
        {
            // 本地清单可能来自 Windows 构建（反斜杠、大小写不同），远端清单一律正斜杠；
            // 若按原样比较，会出现"同一文件被判定为新增 + 删除"的双重错误。
            var local = new List<ManifestFileEntry>
            {
                Entry("RaidDemo_Data\\Managed\\RaidDemo.Combat.dll", 10, "hash-x"),
            };
            var remote = new List<ManifestFileEntry>
            {
                Entry("raiddemo_data/managed/raiddemo.combat.dll", 10, "hash-x"),
            };

            Assert.IsEmpty(UpdateManifestDiff.CompareLayer(local, remote));
        }

        [Test]
        public void Diff_ResultIsSortedByPath()
        {
            var remote = new List<ManifestFileEntry>
            {
                Entry("z.dll", 1, "hash"),
                Entry("a.dll", 1, "hash"),
                Entry("m/x.dll", 1, "hash"),
            };

            var changes = UpdateManifestDiff.CompareLayer(new List<ManifestFileEntry>(), remote);

            Assert.AreEqual("a.dll", changes[0].Path);
            Assert.AreEqual("m/x.dll", changes[1].Path);
            Assert.AreEqual("z.dll", changes[2].Path);
        }

        [Test]
        public void NormalizePath_TrimsDotSlashAndLeadingSlash()
        {
            Assert.AreEqual("a/b.dll", ManifestFileEntry.NormalizePath(".\\a\\b.dll"));
            Assert.AreEqual("a/b.dll", ManifestFileEntry.NormalizePath("/a/b.dll"));
            Assert.AreEqual(string.Empty, ManifestFileEntry.NormalizePath(null));
        }

        [Test]
        public void Hash_MatchesKnownSha256Vector()
        {
            // 已知向量：SHA-256("abc")，用于防止"实现改错但仍自我一致"的假通过。
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                UpdateFileHash.ComputeTextHash("abc"));
        }

        [Test]
        public void Manifest_JsonRoundTrip_PreservesEveryLayer()
        {
            var manifest = new UpdateManifest
            {
                generatedAt = UpdateManifest.CreateTimestamp(),
                body = new ManifestLayer
                {
                    version = "0.10.0",
                    files = new List<ManifestFileEntry>
                    {
                        // 刻意用超过 int 上限的大小，验证 long 字段不会被静默截断。
                        Entry("RaidDemo.exe", 3000000000L, "hash-body"),
                    },
                },
                content = new ManifestContentLayer
                {
                    version = "0.10.0.3",
                    catalog = Entry("catalog_0.10.0.3.json", 1234, "hash-catalog"),
                    files = new List<ManifestFileEntry> { Entry("bundle_a", 42, "hash-bundle") },
                },
                code = new ManifestCodeLayer
                {
                    version = "0.10.0.7",
                    assemblies = new List<ManifestFileEntry> { Entry("RaidDemo.Combat.dll", 7, "hash-dll") },
                    metadata = new List<ManifestFileEntry> { Entry("RaidDemo.AotMetadata.dll", 8, "hash-meta") },
                },
            };

            var json = JsonUtility.ToJson(manifest, true);
            var parsed = JsonUtility.FromJson<UpdateManifest>(json);

            Assert.AreEqual(UpdateManifest.CurrentSchemaVersion, parsed.schemaVersion);
            Assert.AreEqual("0.10.0", parsed.body.version);
            Assert.AreEqual(3000000000L, parsed.body.files[0].size);
            Assert.AreEqual("hash-body", parsed.body.files[0].sha256);
            Assert.AreEqual("catalog_0.10.0.3.json", parsed.content.catalog.path);
            Assert.AreEqual("RaidDemo.Combat.dll", parsed.code.assemblies[0].path);
            Assert.AreEqual("RaidDemo.AotMetadata.dll", parsed.code.metadata[0].path);
        }

        [Test]
        public void Manifest_EmptyLayers_AreTreatedAsAbsent()
        {
            // 批次 1 阶段只有本体层。注意 JsonUtility 的行为：null 层会被写成"字段齐全但内容为空"
            // 的对象，而不是 null——因此"层是否存在"的判据是 version 是否为空字符串。
            var json = JsonUtility.ToJson(new UpdateManifest
            {
                body = new ManifestLayer { version = "0.10.0" },
            });

            var parsed = JsonUtility.FromJson<UpdateManifest>(json);

            Assert.IsNotNull(parsed);
            Assert.IsTrue(parsed.HasBodyLayer);
            Assert.IsFalse(parsed.HasContentLayer, "资源层尚未接入，应判定为不存在");
            Assert.IsFalse(parsed.HasCodeLayer, "代码层尚未接入，应判定为不存在");
            Assert.AreEqual(0, parsed.body.files.Count);
        }
    }
}
