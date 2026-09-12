using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 2 的外部素材解析器：按「正式目录 → 候选暂存区 → 无」的顺序找模型。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要解析器而不是直接写死路径：</b>本项目有一条硬性规则——
    /// 候选素材先放在 <c>External/_Inbox/</c>（该目录被 .gitignore 忽略），
    /// 只有负责人确认采用后才移到 <c>External/&lt;来源&gt;/</c> 并提交。
    /// 这样一来，「场景生成代码引用的路径」在确认前后并不相同。</para>
    ///
    /// <para>把两个位置都写成候选，好处是：确认前场景就能看到真实观感；
    /// 确认后把文件夹一移就自动走正式路径，代码一行都不用改。
    /// 完全找不到素材时返回 null，由调用方回退到灰盒几何——
    /// 这样**别人克隆仓库后即使没有这些素材也能生成一个可运行的地图**
    /// （尤其是 Broken Vector 悬崖包：它的授权不允许再分发原始文件，
    /// 所以它永远不会进仓库，回退路径是必须存在的，而不是可选优化）。</para>
    /// </remarks>
    public static class M7SceneAssetResolver
    {
        /// <summary>外部素材根目录（工程相对路径）。</summary>
        private const string ExternalRoot = "Assets/Game/Content/External";

        /// <summary>候选暂存区（不入库）。</summary>
        private const string InboxRoot = ExternalRoot + "/_Inbox";

        /// <summary>肯尼工业城市套件：正式目录与暂存目录。</summary>
        private static readonly string[] CityKitFolders =
        {
            ExternalRoot + "/Kenney/CityKitIndustrial",
            InboxRoot + "/KenneyCityKitIndustrial"
        };

        /// <summary>肯尼自然套件（远景山体）：正式目录与暂存目录。</summary>
        private static readonly string[] NatureFolders =
        {
            ExternalRoot + "/Kenney/NatureKit",
            InboxRoot + "/KenneyNature"
        };

        /// <summary>Quaternius Toon Shooter 的场景道具目录。</summary>
        private static readonly string[] ToonShooterFolders =
        {
            ExternalRoot + "/Quaternius/ToonShooter/Environment/FBX",
            InboxRoot + "/QuaterniusToonShooter/Environment/FBX"
        };

        /// <summary>Broken Vector 悬崖包（只存在暂存区，永远不入库）。</summary>
        private static readonly string[] CliffFolders =
        {
            ExternalRoot + "/BrokenVector/CliffPack",
            InboxRoot + "/BrokenVectorCliffs"
        };

        /// <summary>肯尼集装箱的三个型号，索引 0/1/2 分别对应 a/b/c。</summary>
        private static readonly string[] ContainerNames =
        {
            "shipping-container-a", "shipping-container-b", "shipping-container-c"
        };

        /// <summary>本次生成过程中「素材缺失」的记录，用于在最后打印一次汇总而不是逐条刷屏。</summary>
        private static readonly HashSet<string> s_Missing = new HashSet<string>();

        /// <summary>开始一次场景生成：清空上一次的缺失记录。</summary>
        public static void BeginBuild()
        {
            s_Missing.Clear();
        }

        /// <summary>把本次缺失的素材打成一段可读文本（没有缺失时返回空串）。</summary>
        public static string BuildMissingReport()
        {
            if (s_Missing.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.Append("[RaidDemo] M7 场景素材缺失，已按灰盒/程序化外观回退：");
            foreach (var item in s_Missing)
            {
                builder.Append("\n  - ").Append(item);
            }

            return builder.ToString();
        }

        /// <summary>按候选目录查找一个模型（不含扩展名，自动尝试 fbx 与 dae）。</summary>
        /// <param name="folders">候选目录，按优先级排列。</param>
        /// <param name="fileName">不带扩展名的文件名。</param>
        public static GameObject LoadModel(string[] folders, string fileName)
        {
            var path = ResolvePath(folders, fileName, ".fbx")
                       ?? ResolvePath(folders, fileName, ".dae")
                       ?? ResolvePath(folders, fileName, ".obj");
            if (path == null)
            {
                s_Missing.Add($"{fileName}（查找目录：{string.Join(" / ", folders)}）");
                return null;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                s_Missing.Add($"{fileName}（找到 {path} 但导入失败）");
            }

            return model;
        }

        /// <summary>
        /// 在候选目录里按关键字模糊查找模型。
        /// </summary>
        /// <param name="folders">候选目录。</param>
        /// <param name="keyword">文件名需要包含的关键字（不区分大小写）。</param>
        /// <remarks>
        /// Kenney 自然套件的模型命名在不同版本间变过（cliff_large / cliff-large / cliffLarge），
        /// 与其中文文档对不上时很费时间。用关键字扫描文件夹比写死文件名更稳，
        /// 而且**多出来的模块会被自动纳入**——批次 2 只需要山石，将来若要做植被也不用改这里。
        /// </remarks>
        public static GameObject LoadFirstModelContaining(string[] folders, string keyword)
        {
            foreach (var folder in folders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                var guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
                var best = (string)null;
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!fileName.ToLowerInvariant().Contains(keyword.ToLowerInvariant()))
                    {
                        continue;
                    }

                    // 取名字最短的那个：通常是最基础的造型（cliff_large 而不是 cliff_large_detailed_b）。
                    if (best == null || fileName.Length < System.IO.Path.GetFileNameWithoutExtension(best).Length)
                    {
                        best = path;
                    }
                }

                if (best != null)
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(best);
                }
            }

            s_Missing.Add($"包含「{keyword}」的模型（查找目录：{string.Join(" / ", folders)}）");
            return null;
        }

        /// <summary>载入肯尼集装箱：索引 0/1/2 → a/b/c。</summary>
        public static GameObject LoadContainer(int index)
        {
            var safeIndex = Mathf.Clamp(index, 0, ContainerNames.Length - 1);
            return LoadModel(CityKitFolders, ContainerNames[safeIndex]);
        }

        /// <summary>载入肯尼工业套件里的一个道具（石化工件、水塔之类的场景点缀）。</summary>
        public static GameObject LoadIndustrialProp(string fileName)
        {
            return LoadModel(CityKitFolders, fileName);
        }

        /// <summary>载入 Quaternius Toon Shooter 的场景道具。</summary>
        public static GameObject LoadToonShooterProp(string fileName)
        {
            return LoadModel(ToonShooterFolders, fileName);
        }

        /// <summary>载入一块 Broken Vector 悬崖瓦片（文件名含空格，必须原样传入）。</summary>
        public static GameObject LoadCliffTile(string fileName)
        {
            return LoadModel(CliffFolders, fileName);
        }

        /// <summary>载入肯尼自然套件里的山石（按关键字模糊匹配）。</summary>
        public static GameObject LoadNatureRock(string keyword)
        {
            return LoadFirstModelContaining(NatureFolders, keyword);
        }

        /// <summary>
        /// 载入肯尼自然套件里全部山石类模型。
        /// </summary>
        /// <remarks>
        /// 远景山脊需要「一眼看不出重复」——同一块石头摆二十几次会立刻露馅。
        /// 因此这里不挑单个模型，而是把 cliff / rock / stone 三类全部取出，
        /// 由调用方轮换使用并叠加随机缩放与朝向。找不到任何一个时返回空列表，
        /// 调用方据此跳过整圈装饰（地形本身不受影响）。
        /// </remarks>
        public static List<GameObject> LoadAllNatureRocks()
        {
            var models = new List<GameObject>();
            foreach (var folder in NatureFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!fileName.Contains("cliff") && !fileName.Contains("rock") && !fileName.Contains("stone"))
                    {
                        continue;
                    }

                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model != null && !models.Contains(model))
                    {
                        models.Add(model);
                    }
                }

                if (models.Count > 0)
                {
                    break;
                }
            }

            if (models.Count == 0)
            {
                s_Missing.Add("肯尼自然套件的山石模型（cliff / rock / stone）");
            }

            return models;
        }

        /// <summary>Broken Vector 的黄色配色贴图（用户选定的「橙色黏土」观感）。</summary>
        public static Texture2D LoadCliffPalette()
        {
            foreach (var folder in CliffFolders)
            {
                var path = $"{folder}/Colorscheme Yellow.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    return texture;
                }
            }

            s_Missing.Add("Broken Vector 配色贴图 Colorscheme Yellow.png");
            return null;
        }

        /// <summary>把一个候选文件名解析成真实工程路径。</summary>
        private static string ResolvePath(string[] folders, string fileName, string extension)
        {
            foreach (var folder in folders)
            {
                var path = $"{folder}/{fileName}{extension}";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                {
                    return path;
                }
            }

            return null;
        }
    }
}
