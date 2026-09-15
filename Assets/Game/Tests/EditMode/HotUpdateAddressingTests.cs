using System.IO;
using NUnit.Framework;
using RaidDemo.HotUpdate;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// M10 内容热更：寻址重写里"哪些地址该翻译"的判断。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这条判断值得单测：</b>它出错时的现象不是报错，而是"资源下载成功、
    /// 内容却退回本体自带"——真机上要跑一整轮"发布 → 更新 → 启动"才看得到，
    /// 而规则本身只是一次字符串判断。</para>
    ///
    /// <para><b>踩过的坑（2026-09-15 真机演练）：</b>早期实现按"能不能取到文件名"判断，
    /// 于是 Addressables 自己的 catalog 缓存路径
    /// （<c>&lt;persistentDataPath&gt;/com.unity.addressables/&lt;哈希&gt;.bin</c>）
    /// 也被翻译成更新源 URL；Addressables 随后把 URL 当目录去建，Windows 抛
    /// <c>文件名、目录名或卷标语法不正确</c>，catalog 加载失败。</para>
    /// </remarks>
    [TestFixture]
    public sealed class HotUpdateAddressingTests
    {
        /// <summary>内容层地址（RemoteLoadPath 命名空间）应被识别。</summary>
        [Test]
        public void 内容层地址_被识别()
        {
            Assert.IsTrue(HotUpdateRuntime.IsContentLayerAddress("content/StandaloneWindows64/group-data.bundle"));
            Assert.IsTrue(HotUpdateRuntime.IsContentLayerAddress("content\\group-data.bundle"), "反斜杠要归一化。");
            Assert.IsTrue(HotUpdateRuntime.IsContentLayerAddress("/content/catalog_0.10.1.bin"), "前导斜杠要忽略。");
            Assert.IsTrue(HotUpdateRuntime.IsContentLayerAddress("CONTENT/group-data.bundle"), "平台路径大小写不敏感。");
        }

        /// <summary>
        /// 本地路径与 Addressables 自己的缓存路径不能被当成内容层地址——
        /// 它们是要写进去的目标，被翻译成 URL 就会在 Windows 上抛非法路径。
        /// </summary>
        [Test]
        public void 本地与缓存路径_不被识别()
        {
            // 路径用运行时拼出来的，不在源码里写死本机盘符（工程规范：源码不得含绝对路径）。
            var cachePath = Path.Combine(Application.persistentDataPath, "com.unity.addressables", "558132896.bin");
            var installPath = Path.Combine(Path.GetTempPath(), "RaidDemo", "RaidDemo_Data");

            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress(cachePath), "Addressables 的 catalog 缓存路径是要写入的目标，不能被翻译成 URL。");
            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress(installPath));
            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress("558132896.bin"), "裸文件名不带命名空间，无法判断归属。");
            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress("http://127.0.0.1:8090/content/x.bundle"), "已经是 URL，不需要再翻译。");
            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress(null));
            Assert.IsFalse(HotUpdateRuntime.IsContentLayerAddress(string.Empty));
        }
    }
}
