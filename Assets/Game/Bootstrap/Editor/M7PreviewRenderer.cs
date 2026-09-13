using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 把当前打开的场景渲染成几张预览图，供负责人验收观感。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>地形、材质与道具的改动只能靠眼睛判断，
    /// 而项目里没有美术同学、也不能靠「进 Play 模式截屏」这种需要人工在场的方式。
    /// 用编辑器脚本离屏渲染，可以把「改完 → 出图 → 评审」变成一个可重复、无人值守的步骤。</para>
    ///
    /// <para>输出目录固定为 <c>Library/M7Preview</c>：<c>Library</c> 是 Unity 的临时目录，
    /// 已被 .gitignore 忽略，因此预览图不会进入仓库，看完即可删。</para>
    /// </remarks>
    public static class M7PreviewRenderer
    {
        /// <summary>预览图输出目录（仓库相对路径，Library 不入库）。</summary>
        private const string OutputFolder = "Library/M7Preview";

        /// <summary>战局场景路径。命令行渲染入口先打开它，再按固定机位出图。</summary>
        private const string RaidScenePath = "Assets/Game/Content/Scenes/GreyboxRaid.unity";

        /// <summary>安全屋场景路径（M8 开篇的设施验收机位使用）。</summary>
        private const string SafeHouseScenePath = "Assets/Game/Content/Scenes/SafeHouse.unity";

        private const int ImageWidth = 1600;

        private const int ImageHeight = 900;

        /// <summary>一张预览图的机位定义。</summary>
        private readonly struct Shot
        {
            public Shot(
                string fileName,
                Vector3 focus,
                float pitch,
                float yaw,
                float distance,
                float fieldOfView,
                bool peephole = false,
                bool marker = false)
            {
                FileName = fileName;
                Focus = focus;
                Pitch = pitch;
                Yaw = yaw;
                Distance = distance;
                FieldOfView = fieldOfView;
                Peephole = peephole;
                Marker = marker;
            }

            public string FileName { get; }

            public Vector3 Focus { get; }

            public float Pitch { get; }

            public float Yaw { get; }

            public float Distance { get; }

            public float FieldOfView { get; }

            /// <summary>是否在渲染前按焦点位置设置一次遮挡透视孔参数（用于验收该功能）。</summary>
            public bool Peephole { get; }

            /// <summary>是否在焦点处放一个代用角色（胶囊），用于对比开孔前后的画面。</summary>
            public bool Marker { get; }
        }

        /// <summary>
        /// 全部机位。
        /// </summary>
        /// <remarks>
        /// 焦距与俯角刻意贴近实战相机（俯角 62 度、距离 13 米），
        /// 这样预览图看到的就是玩家实际会看到的画面；全景与坡道两张是例外，
        /// 它们用来检查地形结构，而不是检查手感。
        /// </remarks>
        private static readonly Shot[] s_Shots =
        {
            // 出生点：草地上看出去，能同时看到厂房、堆场与南侧谷口方向
            new Shot("01_玩家视角_出生点", new Vector3(-4f, -5f, 0f), 62f, 0f, 13f, 55f),

            // 集装箱堆场：从堆场南侧入口向北看，检查集装箱的比例、朝向与配色
            new Shot("02_集装箱堆场", new Vector3(14f, -4f, -4f), 52f, 0f, 17f, 60f),

            // 北坡道：检查土墙、坡道与塬面的衔接
            new Shot("03_北坡道与土墙", new Vector3(0f, -2f, 25f), 32f, 0f, 22f, 55f),

            // 南谷口：从谷底向北看回来，检查平进平出的走廊与撤离点
            // （相机若摆在谷口内侧会贴到尽头土墙上，因此这里用 180 度朝向从北往南拍）
            new Shot("04_南谷口", new Vector3(17f, -5f, -27f), 42f, 180f, 15f, 55f),

            // 全景：检查整张地图的地形结构（谷底、土墙、四条通道、远景山体）
            new Shot("05_盆地全景", new Vector3(0f, -2f, 0f), 78f, 0f, 105f, 42f),

            // 塬面回望：站上北坡顶向南看，这是玩家撤离前看到的画面，也是「下沉盆地」最直观的一帧
            new Shot("06_北塬面回望盆地", new Vector3(0f, 3f, 32f), 40f, 180f, 22f, 60f),

            // 遮挡透视孔：角色站在集装箱北侧、相机在集装箱南侧，箱子正好挡在中间。
            // 这一帧用来验收「不开孔把相机拉近，而是把遮挡物抠出一个圆洞」。
            // 代用角色贴着箱体北面（z=0.6），从 62 度俯角看过去身体几乎全被箱体挡住——正是需要开孔的场合。
            new Shot("07_遮挡透视孔", new Vector3(10f, -5f, -1.2f), 62f, 0f, 13f, 55f, peephole: true, marker: true),

            // 对照组：同一机位、同一个代用角色，但关闭透视孔——两图对比即可看出孔的作用。
            new Shot("08_遮挡透视孔_关闭对照", new Vector3(10f, -5f, -1.2f), 62f, 0f, 13f, 55f, peephole: false, marker: true),

            // 厂房内部（批次 6）：检查加固机床、料斗、机器人与地面标识组成的近战区。
            new Shot("09_厂房内部", new Vector3(-19f, 1f, 1f), 58f, 0f, 16f, 60f),

            // 堆场工业车辆（批次 6）：集装箱、卡车、工程车与水塔 / 吊车同框，检查主题是否成立。
            new Shot("10_堆场工业车辆", new Vector3(18f, 1f, 6f), 52f, 0f, 20f, 60f),
        };

        /// <summary>
        /// 安全屋专用机位（M8 开篇的设施美化验收）。
        /// </summary>
        /// <remarks>
        /// 战局机位拍的是 72×72 的盆地，安全屋是室内小场景，焦距与距离完全不同，
        /// 因此单独一组；编号从 11 开始，避免与战局预览图重名。
        /// </remarks>
        private static readonly Shot[] s_SafeHouseShots =
        {
            // 全景：从南侧俯视整个安全屋，检查四个设施与靶场的相对位置
            new Shot("11_安全屋全景", new Vector3(0f, 1f, 2f), 58f, 0f, 20f, 55f),

            // 仓库：货架、柜体、木箱 / 油桶 / 宝箱组成的仓储角
            new Shot("12_安全屋_仓库", new Vector3(-8f, 1f, 4.2f), 46f, 0f, 9f, 55f),

            // 商人摊位：两张柜台、地毯与站在柜台后的商人 NPC
            new Shot("13_安全屋_商人", new Vector3(-2f, 1.1f, 5.2f), 42f, 0f, 8.5f, 55f),

            // 出口：绿色地垫与金属门框
            new Shot("14_安全屋_出口", new Vector3(5f, 1f, 5f), 45f, 0f, 8f, 55f),

            // 衣柜：柜体与镜面（柜体朝东，因此相机从西侧看过去）
            new Shot("15_安全屋_衣柜", new Vector3(-11.2f, 1f, -1.5f), 45f, 90f, 7.5f, 55f),
        };

        /// <summary>菜单入口：渲染战局预览图。</summary>
        [MenuItem("RaidDemo/M7/渲染场景预览图")]
        public static void RenderFromMenu()
        {
            Debug.Log(RenderAll());
        }

        /// <summary>菜单入口：打开安全屋场景并渲染设施验收图（M8 开篇）。</summary>
        [MenuItem("RaidDemo/M7/渲染安全屋预览图")]
        public static void RenderSafeHouseFromMenu()
        {
            OpenSafeHouseScene();
            Debug.Log(RenderSafeHouse());
        }

        /// <summary>
        /// 打开战局场景并渲染全部预览图，供命令行验收使用。
        /// </summary>
        /// <remarks>
        /// 批处理模式没有"当前打开的场景"，直接调 <see cref="RenderAll"/> 只会渲染到空场景。
        /// 这个方法先打开战局场景再渲染，命令行用法：
        /// <c>Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod
        /// RaidDemo.Bootstrap.Editor.M7PreviewRenderer.OpenRaidSceneAndRender -quit</c>。
        /// 克隆验证用它确认"素材全部入库 + Unity 首次导入"之后地图外观完整。
        /// </remarks>
        public static void OpenRaidSceneAndRender()
        {
            OpenRaidScene();
            Debug.Log(RenderAll());
        }

        /// <summary>
        /// 打开安全屋场景并渲染设施验收图，供命令行使用。
        /// </summary>
        /// <remarks>
        /// 命令行用法与战局版本一致，把 <c>-executeMethod</c> 换成
        /// <c>RaidDemo.Bootstrap.Editor.M7PreviewRenderer.OpenSafeHouseAndRender</c> 即可。
        /// </remarks>
        public static void OpenSafeHouseAndRender()
        {
            OpenSafeHouseScene();
            Debug.Log(RenderSafeHouse());
        }

        private static void OpenRaidScene()
        {
            EditorSceneManager.OpenScene(RaidScenePath, OpenSceneMode.Single);
        }

        private static void OpenSafeHouseScene()
        {
            EditorSceneManager.OpenScene(SafeHouseScenePath, OpenSceneMode.Single);
        }

        /// <summary>按固定机位渲染战局预览图，返回输出目录摘要。</summary>
        public static string RenderAll()
        {
            return RenderShots(s_Shots);
        }

        /// <summary>渲染安全屋专用机位，返回输出目录摘要。</summary>
        public static string RenderSafeHouse()
        {
            return RenderShots(s_SafeHouseShots);
        }

        /// <summary>按给定机位渲染并输出，战局与安全屋两个入口共用。</summary>
        private static string RenderShots(Shot[] shots)
        {
            var folder = System.IO.Path.GetFullPath(OutputFolder);
            System.IO.Directory.CreateDirectory(folder);

            var summary = new StringBuilder("[RaidDemo] 预览图已输出：");
            foreach (var shot in shots)
            {
                var path = $"{OutputFolder}/{shot.FileName}.png";
                if (RenderShot(shot, path))
                {
                    summary.Append('\n').Append("  ").Append(path);
                }
            }

            return summary.ToString();
        }

        /// <summary>渲染单张预览图。</summary>
        private static bool RenderShot(Shot shot, string outputPath)
        {
            var host = new GameObject("M7PreviewCamera");
            var camera = host.AddComponent<Camera>();
            camera.fieldOfView = shot.FieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.18f, 0.22f);

            var rotation = Quaternion.Euler(shot.Pitch, shot.Yaw, 0f);
            var forward = rotation * Vector3.forward;
            host.transform.position = shot.Focus - (forward * shot.Distance);
            host.transform.rotation = rotation;

            // 验收透视孔时要有个「角色」站在孔里，否则看不出洞开在哪。
            // 用一个与玩家同高的胶囊代替：脚底对齐焦点下方 1 米（焦点取的是胸口高度）。
            GameObject marker = null;
            if (shot.Marker)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                marker.name = "PeepholePreviewMarker";
                marker.transform.position = shot.Focus - new Vector3(0f, 1f, 0f) + new Vector3(0f, 0.85f, 0f);
                marker.transform.localScale = new Vector3(0.8f, 1.7f, 0.8f);
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var shader = Shader.Find(M7MaterialLibrary.LitShaderName);
                    if (shader != null)
                    {
                        var material = new Material(shader) { name = "PeepholePreviewMarker" };
                        material.SetColor("_BaseColor", new Color(0.95f, 0.45f, 0.2f));
                        renderer.sharedMaterial = material;
                    }
                }
            }

            // 先把渲染目标挂上，再算孔参数：屏幕坐标依赖相机的 pixelWidth / pixelHeight，
            // 没挂目标纹理时读到的是编辑器的预览尺寸，算出来的圆心会偏。
            var renderTexture = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            camera.targetTexture = renderTexture;

            // 验收透视孔时手动喂一次全局参数：预览相机不在运行时组件管理之下，
            // 不设的话着色器收到的半径是 0，也就看不到孔。
            if (shot.Peephole
                && RaidDemo.Presentation.OcclusionPeepholeController.TryResolvePeephole(
                    camera,
                    shot.Focus,
                    heightOffset: 0.9f,
                    radiusRatio: 0.11f,
                    out var peephole))
            {
                Shader.SetGlobalVector(Shader.PropertyToID("_PeepholeParams"), peephole);
            }
            else
            {
                Shader.SetGlobalVector(Shader.PropertyToID("_PeepholeParams"), Vector4.zero);
            }

            camera.Render();
            camera.targetTexture = previous;

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, ImageWidth, ImageHeight), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            var bytes = texture.EncodeToPNG();
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(outputPath), bytes);

            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(renderTexture);
            if (marker != null)
            {
                Object.DestroyImmediate(marker);
            }

            Object.DestroyImmediate(host);
            return true;
        }
    }
}
