using System.IO;
using NUnit.Framework;
using RaidDemo.Bootstrap.Editor;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// M7 批次 3 的资产接线测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么"资产有没有接上"要写成测试：</b>这一批的产物全部是加工出来的资产
    /// （剪裁后的音效、武器预制体、特效预制体、总目录）。它们不在代码里，编译器不会检查，
    /// 漏接的症状又恰好是"游戏能跑但没声音"，很容易被当成功能没做，而不是资产没接。</para>
    /// <para>测试失败时的提示直接写明"请执行哪个菜单"，让修复成本降到一条命令。</para>
    /// </remarks>
    [TestFixture]
    public sealed class M7PresentationAssetTests
    {
        private const string RebuildHint =
            "请执行菜单 RaidDemo/M7/重建表现层资产（音效/武器/特效）。";

        private static readonly string[] ScenePaths =
        {
            "Assets/Game/Content/Scenes/GreyboxRaid.unity",
            "Assets/Game/Content/Scenes/SafeHouse.unity"
        };

        [Test]
        public void 音效目录已生成且枪声槽位不为空()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(M7AudioAssetBuilder.CatalogPath);
            Assert.IsNotNull(catalog, $"音效目录缺失：{M7AudioAssetBuilder.CatalogPath}。{RebuildHint}");
            Assert.IsTrue(catalog.HasCombatSounds, $"音效目录里没有步枪或手枪枪声。{RebuildHint}");
        }

        [Test]
        public void 音效目录的关键提示音都已接线()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(M7AudioAssetBuilder.CatalogPath);
            Assert.IsNotNull(catalog, RebuildHint);

            Assert.IsNotNull(catalog.PickRifleShot(0), "缺少步枪枪声。" + RebuildHint);
            Assert.IsNotNull(catalog.PickPistolShot(0), "缺少手枪枪声。" + RebuildHint);
            Assert.IsNotNull(catalog.PickImpactFlesh(0), "缺少命中活体音。" + RebuildHint);
            Assert.IsNotNull(catalog.PickImpactHard(0), "缺少命中硬物音。" + RebuildHint);
            Assert.IsNotNull(catalog.PickFootstepGrass(0), "缺少草地脚步音。" + RebuildHint);
            Assert.IsNotNull(catalog.MagazineOut, "缺少换弹音。" + RebuildHint);
            Assert.IsNotNull(catalog.PickLootRummage(0), "缺少翻找音。" + RebuildHint);
            Assert.IsNotNull(catalog.RaidSuccess, "缺少撤离成功提示音。" + RebuildHint);
        }

        [Test]
        public void 剪裁后的枪声长度在可用区间内()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                $"{M7AudioAssetBuilder.SfxRoot}/Weapons/Rifle_Shot_01.wav");
            Assert.IsNotNull(clip, RebuildHint);

            // 太短会听不出膛压，太长会在连射时糊成一片；0.5~1.2 秒是可用的窗口。
            Assert.That(clip.length, Is.InRange(0.5f, 1.2f),
                $"步枪枪声长度 {clip.length:F2} 秒超出可用区间，请检查加工参数。");
        }

        [Test]
        public void 两把武器预制体都存在并带枪口标记()
        {
            AssertWeapon(M7WeaponPrefabBuilder.RiflePrefabPath);
            AssertWeapon(M7WeaponPrefabBuilder.PistolPrefabPath);
        }

        [Test]
        public void 四个特效预制体都带粒子系统且不自动播放()
        {
            foreach (var name in new[]
                     {
                         "Vfx_MuzzleFlash", "Vfx_ImpactSpark", "Vfx_ImpactDust", "Vfx_ImpactFlesh"
                     })
            {
                var path = $"{M7VfxPrefabBuilder.VfxFolder}/{name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, $"特效预制体缺失：{path}。{RebuildHint}");

                var system = prefab.GetComponent<ParticleSystem>();
                Assert.IsNotNull(system, $"{name} 根节点上没有粒子系统。{RebuildHint}");
                Assert.IsFalse(system.main.playOnAwake,
                    $"{name} 不能自动播放：它由对象池在开火时才唤醒，自动播放会在池里提前喷一次。");
            }
        }

        [Test]
        public void 表现层目录引用了全部资产()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PresentationCatalog>(
                M7PresentationCatalogBuilder.CatalogPath);
            Assert.IsNotNull(catalog, $"表现层目录缺失：{M7PresentationCatalogBuilder.CatalogPath}。{RebuildHint}");

            Assert.IsNotNull(catalog.Audio, "表现层目录没有引用音效目录。" + RebuildHint);
            Assert.IsNotNull(catalog.RifleWeaponPrefab, "表现层目录没有引用步枪预制体。" + RebuildHint);
            Assert.IsNotNull(catalog.PistolWeaponPrefab, "表现层目录没有引用手枪预制体。" + RebuildHint);
            Assert.IsNotNull(catalog.MuzzleFlashPrefab, "表现层目录没有引用枪口火焰。" + RebuildHint);
            Assert.IsNotNull(catalog.ImpactSparkPrefab, "表现层目录没有引用命中火花。" + RebuildHint);
            Assert.IsNotNull(catalog.ImpactDustPrefab, "表现层目录没有引用命中尘土。" + RebuildHint);
            Assert.IsNotNull(catalog.ImpactFleshPrefab, "表现层目录没有引用命中活体特效。" + RebuildHint);
        }

        [Test]
        public void 两个场景都接上了表现层目录()
        {
            var guid = AssetDatabase.AssetPathToGUID(M7PresentationCatalogBuilder.CatalogPath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), RebuildHint);

            foreach (var scenePath in ScenePaths)
            {
                Assert.IsTrue(File.Exists(scenePath), $"场景文件缺失：{scenePath}");
                var text = File.ReadAllText(scenePath);
                Assert.IsTrue(text.Contains(guid),
                    $"{scenePath} 没有引用表现层目录，进游戏会没有音效与枪口特效。" +
                    "请执行菜单 RaidDemo/M7/把表现层目录接入场景。");
            }
        }

        private static void AssertWeapon(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"武器预制体缺失：{path}。{RebuildHint}");
            Assert.IsNotNull(prefab.GetComponentInChildren<Renderer>(true), $"{path} 里没有任何可渲染网格。");

            var hasMuzzle = false;
            foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Muzzle")
                {
                    hasMuzzle = true;
                    break;
                }
            }

            Assert.IsTrue(hasMuzzle, $"{path} 上找不到名为 Muzzle 的枪口标记，弹道起点会退化成估算位置。");
        }
    }
}
