using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒测试场景生成器（入口部分）。
    /// </summary>
    /// <remarks>
    /// <para>场景由脚本生成而非手工搭建，原因有三个：
    /// 一是可重复，任何人执行同一个菜单命令都能得到完全一致的场景；
    /// 二是便于版本控制，生成脚本本身是文本，而场景文件是复杂 YAML，冲突极难处理；
    /// 三是调整布局只需改常量，不必在编辑器里逐个拖动对象。</para>
    ///
    /// <para><b>文件拆分</b>：本类按职责拆成多个 partial 文件，
    /// 既是为了遵守项目「单文件不超过 400 行」的规定，也让改动的影响范围一目了然：</para>
    /// <list type="bullet">
    /// <item><description>本文件：入口流程、光照、玩家、启动对象；</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Factory</c>：几何体工厂（方块、墙、坡道、材质）；</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Layout</c>：地图分区布局（厂房、堆场、装卸平台、撤离点、战利品）。</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Terrain</c>：M7 批次 2 的下沉盆地地形、悬崖装饰与远景山体；</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Props</c>：外部素材预制体的摆放与坐标换算。</description></item>
    /// </list>
    ///
    /// <para>本工具只在编辑器中运行，不参与游戏构建。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>生成场景的输出路径（仓库相对路径）。</summary>
        private const string ScenePath = "Assets/Game/Content/Scenes/GreyboxRaid.unity";

        // M7 批次 2 起，地面与外围围墙由下沉盆地地形取代（见 GreyboxSceneBuilder.Terrain）：
        // 谷底范围 56×56 米，外围是 6 米高的土墙与 4 米宽的塬面，不再使用平直围墙。

        /// <summary>
        /// 玩家出生点。
        /// </summary>
        /// <remarks>
        /// 选在地图中央偏西的空地：往西是主厂房、往东是集装箱堆场、往南是装卸平台，
        /// 三个方向都有内容可去，玩家一出生就能看清自己有哪些选择。
        /// 刻意不放在任何撤离点旁边——出生就能撤离等于没有风险。
        /// </remarks>
        private static readonly Vector3 PlayerSpawn = new Vector3(-4f, 0f, 0f);

        /// <summary>角色身高（米）。灰盒阶段用于确定身体与头部的位置。</summary>
        private const float PlayerHeight = 1.8f;

        /// <summary>角色身体半径（米）。</summary>
        private const float PlayerRadius = 0.4f;

        /// <summary>
        /// 相机俯角（度）。
        /// </summary>
        /// <remarks>
        /// 62 度对应参考实现的接近正俯视的视角：能清晰读出地面平面布局与掩体关系，
        /// 同时保留少量立体感用于判断高低差。45 度过于接近第三人称，不是本项目的目标视角。
        /// </remarks>
        private const float CameraPitchDegrees = 62f;

        /// <summary>
        /// 相机到目标的距离。
        /// </summary>
        /// <remarks>
        /// 该值直接决定玩家在画面中的视觉大小。距离 24 时角色仅占屏幕高度约 6%，
        /// 观感上「人太小、看不清在做什么」；收紧到 13 之后角色占比约 11%，
        /// 既能看清角色与朝向，仍保留足够的战场视野。
        ///
        /// 调整本值时必须同步考虑地面可视范围：距离越近，可见的战场越小。
        /// 若后续地图尺度变大，应优先调大视野角而不是把相机往后拉，
        /// 因为拉远会重新让角色变小。
        /// </remarks>
        private const float CameraDistance = 13f;

        /// <summary>相机视野（垂直角度）。</summary>
        private const float CameraFieldOfView = 55f;

        /// <summary>
        /// 一局战局时长（秒）。
        /// </summary>
        /// <remarks>
        /// 8 分钟：足够搜两三个区域加一次交火，又不至于长到让「再来一局」变得沉重。
        /// 该值同时写入场景里的启动对象，联调时可以直接在 Inspector 里改，不必重新生成场景。
        /// </remarks>
        private const float RaidDurationSeconds = 480f;

        /// <summary>撤离读秒时长（秒）。进入撤离区后站满这么久才算撤离成功，离开即中断并重置。</summary>
        private const float ExtractionDurationSeconds = 10f;

        /// <summary>搜刮读条时长（秒）。</summary>
        /// <remarks>
        /// 2 秒是「贪婪循环」的成本：它短到不至于让人烦躁，长到足以让玩家在开箱时
        /// 必须考虑「附近有没有敌人」。没有这条读条，搜刮就没有风险成本。
        /// </remarks>
        private const float LootSearchDurationSeconds = 2f;

        /// <summary>搜刮交互的最大距离（米）。</summary>
        private const float LootSearchRangeMeters = 2.2f;

        /// <summary>生成灰盒测试场景。</summary>
        [MenuItem("RaidDemo/生成灰盒测试场景")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            // 先把外部素材加工成项目层预制体：场景只引用 Art/Props 下的预制体，
            // 这样换素材时场景与代码都不需要改。
            M7PropPrefabBuilder.BuildAll();

            CreateLighting();
            CreateBasinTerrain();
            CreateBoundaryBarriers();
            CreateCliffRing();
            CreateFarRidge();
            CreateFactoryZone();
            CreateContainerYardZone();
            CreateLoadingDockZone();
            CreateOuterRing();
            CreateIndustrialLandmarks();
            CreateIndustrialProps();
            CreateExtractionZones();
            CreateLootContainers();
            CreatePlayer();
            CreateBootstrap();

            // 确保输出目录存在。使用 AssetDatabase 而非文件系统 API，
            // 这样 Unity 会同步更新资源数据库与 .meta 文件。
            EnsureFolder("Assets/Game/Content");
            EnsureFolder("Assets/Game/Content/Scenes");

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[RaidDemo] 灰盒场景已生成：{ScenePath}");

            // 素材缺失只提示一次：场景已经按程序化/灰盒外观生成完毕，仍然可以正常运行。
            var missing = M7SceneAssetResolver.BuildMissingReport();
            if (!string.IsNullOrEmpty(missing))
            {
                Debug.LogWarning(missing);
            }
        }

        /// <summary>创建平行光与天空盒设置，保证灰盒场景可辨识。</summary>
        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>
        /// 创建玩家对象。
        /// </summary>
        /// <remarks>
        /// <para>玩家的根节点被刻意放在地面高度（y 等于 0），身体与头部作为子节点向上堆叠。
        /// 这样做的原因是：Unity 内置的胶囊图元高度为 2 且原点位于几何中心，
        /// 若直接把胶囊当作角色根节点，会有一半体积位于地面以下，表现为角色陷入地里。</para>
        ///
        /// <para>把根节点固定在脚底，可以让移动逻辑（模拟层输出的位置）与表现层
        /// 保持一致的语义：位置就是角色站立的地面点，不需要任何额外的高度补偿。
        /// 后续替换为正式模型时，只需保证模型的脚底对齐根节点即可。</para>
        /// </remarks>
        private static void CreatePlayer()
        {
            var player = new GameObject("Player");
            // 出生点写的是布局坐标（谷底为 0），这里换算成世界坐标；
            // 玩家初始高度必须落在谷底，否则 PlayerMotor 第一次地面采样会从错误的高度开始。
            player.transform.position = new Vector3(PlayerSpawn.x, ValleyFloorY, PlayerSpawn.z);

            // 碰撞体放在根节点并居中于身体，使碰撞范围与视觉体积一致。
            var collider = player.AddComponent<CapsuleCollider>();
            collider.height = PlayerHeight;
            collider.radius = PlayerRadius;
            collider.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);

            // M7 批次 1：外观改为 Kenney 角色（预制体自带 Animator 与项目材质）。
            // 旧灰盒的圆柱/球/朝向方块全部移除——角色朝向由模型本身表达。
            AttachPlayerCharacter(player);

            player.AddComponent<RaidDemo.Presentation.PlayerMotor>();
            player.AddComponent<RaidDemo.Presentation.PlayerCharacterView>();
        }

        /// <summary>把玩家角色预制体挂到角色根节点下（脚底对齐根节点）。</summary>
        private static void AttachPlayerCharacter(GameObject player)
        {
            const string prefabPath = "Assets/Game/Content/Art/Characters/Player/PlayerCharacter.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"玩家角色预制体缺失：{prefabPath}");
                return;
            }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
        }

        /// <summary>创建场景启动对象，并把各组件引用接好。</summary>
        private static void CreateBootstrap()
        {
            var bootstrapObject = new GameObject("SceneBootstrap");

            var camera = BuildCamera();

            var collector = bootstrapObject.AddComponent<RaidDemo.Input.PlayerInputCollector>();
            var cameraController = camera.GetComponent<RaidDemo.Presentation.TopDownCameraController>();

            var bootstrap = bootstrapObject.AddComponent<RaidDemo.Bootstrap.SceneBootstrap>();

            // 使用 SerializedObject 写入私有序列化字段，避免为了初始化而在运行时类型上
            // 暴露公开 setter——那些字段只应由编辑器装配，不应被运行时代码随意修改。
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("m_PlayerMotor").objectReferenceValue =
                Object.FindFirstObjectByType<RaidDemo.Presentation.PlayerMotor>();
            serialized.FindProperty("m_InputCollector").objectReferenceValue = collector;
            serialized.FindProperty("m_CameraController").objectReferenceValue = cameraController;
            serialized.FindProperty("m_PlayerSpawnPosition").vector2Value =
                new Vector2(PlayerSpawn.x, PlayerSpawn.z);
            serialized.FindProperty("m_ItemCatalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RaidDemo.Data.ItemCatalog>(
                    "Assets/Game/Content/Items/ItemCatalog.asset");

            // 表现层资产目录（音效 / 武器模型 / 战斗特效）。与物品目录一样由编辑器装配写入，
            // 漏掉它的症状是"场景能跑但没有任何声音与枪口特效"。
            serialized.FindProperty("m_PresentationCatalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RaidDemo.Presentation.PresentationCatalog>(
                    M7PresentationCatalogBuilder.CatalogPath);

            // 战局参数写进场景：它们是需要反复调的游戏节奏数值，
            // 放在 Inspector 里改比每次重新生成场景快得多。
            serialized.FindProperty("m_RaidDurationSeconds").floatValue = RaidDurationSeconds;
            serialized.FindProperty("m_ExtractionDurationSeconds").floatValue = ExtractionDurationSeconds;
            serialized.FindProperty("m_LootSearchDurationSeconds").floatValue = LootSearchDurationSeconds;
            serialized.FindProperty("m_LootSearchRangeMeters").floatValue = LootSearchRangeMeters;

            // 敌人角色外观：三个 Toon Shooter 角色轮换使用（M7 批次 1）。
            // 预制体为空时 EnemyAgentView 会退化为灰盒胶囊，因此这里允许装配失败。
            var enemyPrefabs = serialized.FindProperty("m_EnemyCharacterPrefabs");
            if (enemyPrefabs != null)
            {
                var paths = new[]
                {
                    "Assets/Game/Content/Art/Characters/Enemies/Character_Soldier.prefab",
                    "Assets/Game/Content/Art/Characters/Enemies/Character_Hazmat.prefab",
                    "Assets/Game/Content/Art/Characters/Enemies/Character_Enemy.prefab"
                };
                enemyPrefabs.arraySize = paths.Length;
                for (var i = 0; i < paths.Length; i++)
                {
                    enemyPrefabs.GetArrayElementAtIndex(i).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");
            if (actions != null)
            {
                // 输入动作资产与瞄准相机都属于输入采集组件。
                var collectorSerialized = new SerializedObject(collector);
                collectorSerialized.FindProperty("m_Actions").objectReferenceValue = actions;
                collectorSerialized.FindProperty("m_AimCamera").objectReferenceValue = camera.GetComponent<Camera>();
                collectorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>创建斜俯视相机。</summary>
        private static GameObject BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = CameraFieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.18f, 0.22f);

            cameraObject.AddComponent<AudioListener>();
            var controller = cameraObject.AddComponent<RaidDemo.Presentation.TopDownCameraController>();

            // 相机参数通过序列化字段写入，保证与运行时使用的值一致。
            // 直接设置 Transform 是不够的：TopDownCameraController 会在 LateUpdate 中
            // 按自己的参数重算位置，若两者不一致，进游戏瞬间画面会跳一下。
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("m_PitchDegrees").floatValue = CameraPitchDegrees;
            serialized.FindProperty("m_Distance").floatValue = CameraDistance;
            serialized.FindProperty("m_WorldOffset").vector3Value = new Vector3(0f, 1f, 0f);
            serialized.FindProperty("m_SmoothTime").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            controller.SnapToTarget();
            return cameraObject;
        }

        /// <summary>确保资源目录存在。</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
