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
    /// <para><b>文件拆分</b>：本类按职责拆成三个 partial 文件，
    /// 既是为了遵守项目「单文件不超过 400 行」的规定，也让改动的影响范围一目了然：</para>
    /// <list type="bullet">
    /// <item><description>本文件：入口流程、光照、地面、玩家、启动对象；</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Factory</c>：几何体工厂（方块、墙、坡道、材质）；</description></item>
    /// <item><description><c>GreyboxSceneBuilder.Layout</c>：地图分区布局（厂房、堆场、装卸平台、撤离点、战利品）。</description></item>
    /// </list>
    ///
    /// <para>本工具只在编辑器中运行，不参与游戏构建。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>生成场景的输出路径（仓库相对路径）。</summary>
        private const string ScenePath = "Assets/Game/Content/Scenes/GreyboxRaid.unity";

        /// <summary>地面边长（米）。</summary>
        /// <remarks>
        /// 60x60 是权衡后的尺寸：AI 视距只有 9 米、武器射程 8~12 米，
        /// 地图再大就只是让玩家在空地上跑，搜刮节奏反而变差。尺寸属于打磨项，
        /// 先把闭环跑通更重要。
        /// </remarks>
        private const float GroundSize = 60f;

        /// <summary>外围围墙高度（米）。</summary>
        private const float WallHeight = 4f;

        /// <summary>外围围墙厚度（米）。</summary>
        private const float WallThickness = 0.8f;

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

            CreateLighting();
            CreateGround();
            CreatePerimeterWalls();
            CreateFactoryZone();
            CreateContainerYardZone();
            CreateLoadingDockZone();
            CreateOuterRing();
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

        /// <summary>创建地面。</summary>
        /// <remarks>
        /// <para>地面上额外挂一个 <see cref="NavMeshSurface"/>，但**不在这里烘焙**：
        /// 导航网格由启动层在运行时调用 <c>BuildNavMesh()</c> 生成。</para>
        ///
        /// <para>这样选择的原因是本场景由代码生成：若在编辑器里预先烘焙，数据会与布局脱节，
        /// 改了箱子的位置却忘记重新烘焙时，AI 会绕着已经不存在的箱子走——
        /// 这种问题从画面上完全看不出来，只能靠人偶然发现。运行时烘焙保证导航网格
        /// 永远与当前布局一致，而灰盒地图只有 60x60，这点开销可以忽略。</para>
        /// </remarks>
        private static void CreateGround()
        {
            var ground = CreateBox(
                "Ground",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(GroundSize, 1f, GroundSize));
            SetMaterialColor(ground, new Color(0.32f, 0.34f, 0.36f));

            // 采集方式显式指定而不是依赖默认值：默认值随包版本变过，
            // 而「哪些物体参与烘焙」直接决定 AI 能不能绕过集装箱。
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
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
            player.transform.position = PlayerSpawn;

            // 碰撞体放在根节点并居中于身体，使碰撞范围与视觉体积一致。
            var collider = player.AddComponent<CapsuleCollider>();
            collider.height = PlayerHeight;
            collider.radius = PlayerRadius;
            collider.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);

            // 身体：圆柱体，高度略低于总身高，把头部占用的空间留出来。
            var bodyHeight = PlayerHeight - (PlayerRadius * 2f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(player.transform, worldPositionStays: false);
            body.transform.localPosition = new Vector3(0f, PlayerRadius + (bodyHeight * 0.5f), 0f);
            body.transform.localScale = new Vector3(PlayerRadius * 2f, bodyHeight * 0.5f, PlayerRadius * 2f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            SetMaterialColor(body, new Color(0.25f, 0.6f, 0.95f));

            // 头部：球体，放在身体顶端。
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(player.transform, worldPositionStays: false);
            head.transform.localPosition = new Vector3(0f, PlayerHeight - PlayerRadius, 0f);
            head.transform.localScale = Vector3.one * (PlayerRadius * 2f);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            SetMaterialColor(head, new Color(0.85f, 0.72f, 0.2f));

            // 朝向指示：在角色前方（本地 +Z）放一个小方块。
            // 斜俯视下角色本身近似圆形，若不放置指示物将无法判断朝向是否正确。
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingIndicator";
            nose.transform.SetParent(player.transform, worldPositionStays: false);
            nose.transform.localPosition = new Vector3(0f, 0.25f, PlayerRadius + 0.3f);
            nose.transform.localScale = new Vector3(0.2f, 0.15f, 0.5f);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            SetMaterialColor(nose, new Color(0.95f, 0.35f, 0.2f));

            player.AddComponent<RaidDemo.Presentation.PlayerMotor>();
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

            // 战局参数写进场景：它们是需要反复调的游戏节奏数值，
            // 放在 Inspector 里改比每次重新生成场景快得多。
            serialized.FindProperty("m_RaidDurationSeconds").floatValue = RaidDurationSeconds;
            serialized.FindProperty("m_ExtractionDurationSeconds").floatValue = ExtractionDurationSeconds;
            serialized.FindProperty("m_LootSearchDurationSeconds").floatValue = LootSearchDurationSeconds;
            serialized.FindProperty("m_LootSearchRangeMeters").floatValue = LootSearchRangeMeters;
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
