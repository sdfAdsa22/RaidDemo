using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 盆地地形使用的材质与贴图：程序化草地噪点、黏土土墙、悬崖瓦片配色。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么草地贴图要自己生成：</b>纯色草地在俯视角下会显得非常平，
    /// 玩家分不清「自己有没有移动」——相机跟随单色地面时缺少参照物。
    /// 但卡通风格又不适合写实草地贴图，因此生成一张 512 的**低对比噪点**：
    /// 大块颜色只有几个百分点差异，近看能提供地面参照，远看仍然是一整块纯草地，
    /// 符合「纯草地、不要杂草与石头」的设计要求。</para>
    ///
    /// <para>噪点用固定随机种子生成并落盘成 PNG（而不是每次运行随机生成），
    /// 这样任何人重新生成场景都会得到完全相同的地面，diff 也是干净的。</para>
    /// </remarks>
    public static class BasinTerrainMaterialBuilder
    {
        /// <summary>项目美术资产目录（材质与贴图都放这里）。</summary>
        public const string MaterialsFolder = "Assets/Game/Content/Art/Materials";

        /// <summary>草地噪点贴图路径。</summary>
        public const string GrassTexturePath = MaterialsFolder + "/T_TerrainGrassNoise.png";

        /// <summary>草地材质路径。</summary>
        public const string GrassMaterialPath = MaterialsFolder + "/M_TerrainGrass.mat";

        /// <summary>土墙材质路径（地形网格上的黏土立面）。</summary>
        public const string ClayMaterialPath = MaterialsFolder + "/M_TerrainClay.mat";

        /// <summary>悬崖瓦片材质路径（Broken Vector 模块专用，使用其黄色配色贴图）。</summary>
        public const string CliffTileMaterialPath = MaterialsFolder + "/M_CliffClay.mat";

        /// <summary>草地噪点的随机种子。改动它等于换一套草地纹理。</summary>
        private const int GrassNoiseSeed = 20260912;

        /// <summary>贴图边长（像素）。512 足够 4 米一循环的密度，也是标准里给场景贴图的默认档。</summary>
        private const int GrassTextureSize = 512;

        /// <summary>草色深浅范围。深色是阴影里的草，浅色是受光面，两者只差十余个百分点。</summary>
        private static readonly Color GrassDark = new Color(0.30f, 0.56f, 0.25f);

        private static readonly Color GrassLight = new Color(0.52f, 0.76f, 0.34f);

        /// <summary>少数偏黄的干草斑块，用来打破大面积的均匀绿。</summary>
        private static readonly Color GrassDry = new Color(0.62f, 0.74f, 0.36f);

        /// <summary>土墙基础色：橙黄黏土，对应负责人选定的参考图观感。</summary>
        private static readonly Color ClayColor = new Color(0.72f, 0.42f, 0.21f);

        /// <summary>菜单入口：强制重建草地贴图（调过噪点参数后用）。</summary>
        [MenuItem("RaidDemo/M7/重建地形草地贴图")]
        public static void RebuildGrassTextureFromMenu()
        {
            AssetDatabase.DeleteAsset(GrassTexturePath);
            AssetDatabase.Refresh();
            EnsureGrassNoiseTexture();
            Debug.Log("[RaidDemo] 草地噪点贴图已重建：" + GrassTexturePath);
        }

        /// <summary>创建/更新草地材质。</summary>
        public static Material EnsureGrassMaterial()
        {
            var texture = EnsureGrassNoiseTexture();
            return M7MaterialLibrary.EnsureLitMaterial(
                GrassMaterialPath,
                Color.white,
                texture,
                smoothness: 0.04f);
        }

        /// <summary>创建/更新土墙材质（地形网格上的黏土立面）。</summary>
        public static Material EnsureClayMaterial()
        {
            return M7MaterialLibrary.EnsureLitMaterial(
                ClayMaterialPath,
                ClayColor,
                texture: null,
                smoothness: 0.06f);
        }

        /// <summary>
        /// 创建/更新悬崖瓦片材质。
        /// </summary>
        /// <remarks>
        /// Broken Vector 的瓦片 UV 是**指向调色板贴图上的具体色块**的，
        /// 因此这里必须使用它自带的 Colorscheme 贴图，不能换成普通平铺纹理，
        /// 否则模型会采到调色板里的随机像素，出现一片片杂色。
        /// 找不到该素材时回退成纯黏土色——此时场景里本来也不会有悬崖瓦片。
        /// </remarks>
        public static Material EnsureCliffTileMaterial()
        {
            var palette = M7SceneAssetResolver.LoadCliffPalette();
            return M7MaterialLibrary.EnsureLitMaterial(
                CliffTileMaterialPath,
                palette != null ? Color.white : ClayColor,
                palette,
                smoothness: 0.06f);
        }

        /// <summary>确保草地噪点贴图存在；不存在时按固定种子生成并写入 PNG。</summary>
        private static Texture2D EnsureGrassNoiseTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath);
            if (existing != null)
            {
                return existing;
            }

            var texture = BuildGrassNoiseTexture();
            var fullPath = System.IO.Path.GetFullPath(GrassTexturePath);
            M7MaterialLibrary.EnsureFolder(MaterialsFolder);
            System.IO.File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(GrassTexturePath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(GrassTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = GrassTextureSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath);
        }

        /// <summary>生成草地噪点贴图：三层值噪声叠加后映射到草色区间。</summary>
        private static Texture2D BuildGrassNoiseTexture()
        {
            var random = new System.Random(GrassNoiseSeed);
            var coarse = BuildNoiseGrid(4, random);
            var medium = BuildNoiseGrid(16, random);
            var fine = BuildNoiseGrid(64, random);

            var texture = new Texture2D(GrassTextureSize, GrassTextureSize, TextureFormat.RGBA32, true)
            {
                name = "T_TerrainGrassNoise"
            };

            var pixels = new Color32[GrassTextureSize * GrassTextureSize];
            for (var y = 0; y < GrassTextureSize; y++)
            {
                var v = (float)y / GrassTextureSize;
                for (var x = 0; x < GrassTextureSize; x++)
                {
                    var u = (float)x / GrassTextureSize;
                    var value = (0.55f * SampleNoise(coarse, u, v))
                                + (0.30f * SampleNoise(medium, u, v))
                                + (0.15f * SampleNoise(fine, u, v));

                    var color = Color.Lerp(GrassDark, GrassLight, Mathf.Clamp01(value * 1.35f));

                    // 只有最高的那一小段噪声会混入干草色，保证「大体纯绿、偶有变化」。
                    if (value > 0.72f)
                    {
                        color = Color.Lerp(color, GrassDry, (value - 0.72f) * 1.6f);
                    }

                    pixels[(y * GrassTextureSize) + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return texture;
        }

        /// <summary>生成一张可无缝平铺的随机值网格。</summary>
        private static float[,] BuildNoiseGrid(int cells, System.Random random)
        {
            var grid = new float[cells, cells];
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                {
                    grid[x, y] = (float)random.NextDouble();
                }
            }

            return grid;
        }

        /// <summary>双线性采样值噪声，索引环绕以实现无缝平铺。</summary>
        private static float SampleNoise(float[,] grid, float u, float v)
        {
            var cells = grid.GetLength(0);
            var fx = u * cells;
            var fy = v * cells;
            var x0 = Mathf.FloorToInt(fx);
            var y0 = Mathf.FloorToInt(fy);
            var tx = Mathf.SmoothStep(0f, 1f, fx - x0);
            var ty = Mathf.SmoothStep(0f, 1f, fy - y0);
            var x1 = (x0 + 1) % cells;
            var y1 = (y0 + 1) % cells;
            x0 %= cells;
            y0 %= cells;

            var bottom = Mathf.Lerp(grid[x0, y0], grid[x1, y0], tx);
            var top = Mathf.Lerp(grid[x0, y1], grid[x1, y1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }
    }
}
