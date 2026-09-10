using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒测试场景生成器。
    /// </summary>
    /// <remarks>
    /// 场景由脚本生成而非手工搭建，原因有三个：
    /// 一是可重复，任何人执行同一个菜单命令都能得到完全一致的场景；
    /// 二是便于版本控制，生成脚本本身是文本，而场景文件是复杂 YAML，冲突极难处理；
    /// 三是调整布局只需改常量，不必在编辑器里逐个拖动对象。
    ///
    /// 本工具只在编辑器中运行，不参与游戏构建。
    /// </remarks>
    public static class GreyboxSceneBuilder
    {
        /// <summary>生成场景的输出路径（仓库相对路径）。</summary>
        private const string ScenePath = "Assets/Game/Content/Scenes/GreyboxRaid.unity";

        /// <summary>地面边长。</summary>
        private const float GroundSize = 60f;

        /// <summary>外围围墙高度。</summary>
        private const float WallHeight = 4f;

        /// <summary>围墙厚度。</summary>
        private const float WallThickness = 0.8f;

        /// <summary>集装箱标准尺寸。</summary>
        private static readonly Vector3 ContainerSize = new Vector3(6f, 3f, 2.5f);

        /// <summary>玩家出生点。</summary>
        private static readonly Vector3 PlayerSpawn = Vector3.zero;

        [MenuItem("RaidDemo/生成灰盒测试场景")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            CreateLighting();
            CreateGround();
            CreatePerimeterWalls();
            CreateContainers();
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
        private static void CreateGround()
        {
            var ground = CreateBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(GroundSize, 1f, GroundSize));
            SetMaterialColor(ground, new Color(0.32f, 0.34f, 0.36f));
        }

        /// <summary>创建四面围墙，把玩家限制在场景内。</summary>
        private static void CreatePerimeterWalls()
        {
            var half = (GroundSize * 0.5f) - (WallThickness * 0.5f);
            var length = GroundSize;

            var walls = new (string Name, Vector3 Position, Vector3 Size)[]
            {
                ("Wall_North", new Vector3(0f, WallHeight * 0.5f, half), new Vector3(length, WallHeight, WallThickness)),
                ("Wall_South", new Vector3(0f, WallHeight * 0.5f, -half), new Vector3(length, WallHeight, WallThickness)),
                ("Wall_East", new Vector3(half, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, length)),
                ("Wall_West", new Vector3(-half, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, length))
            };

            var parent = new GameObject("Perimeter").transform;
            foreach (var (name, position, size) in walls)
            {
                var wall = CreateBox(name, position, size);
                wall.transform.SetParent(parent, worldPositionStays: true);
                SetMaterialColor(wall, new Color(0.45f, 0.43f, 0.40f));
            }
        }

        /// <summary>
        /// 创建集装箱阵列作为掩体与空间分隔。
        /// </summary>
        /// <remarks>
        /// 布局采用固定数据而非随机生成：灰盒阶段的目的是验证移动与相机手感，
        /// 布局必须稳定可复现，否则每次生成结果不同会让问题排查失去基准。
        /// </remarks>
        private static void CreateContainers()
        {
            var parent = new GameObject("Containers").transform;

            var layout = new (Vector3 Position, float YawDegrees)[]
            {
                (new Vector3(8f, 0f, 6f), 0f),
                (new Vector3(-9f, 0f, 7f), 90f),
                (new Vector3(12f, 0f, -8f), 0f),
                (new Vector3(-11f, 0f, -6f), 0f),
                (new Vector3(0f, 0f, 14f), 90f),
                (new Vector3(-4f, 0f, -15f), 0f),
                (new Vector3(16f, 0f, 16f), 0f),
                (new Vector3(-16f, 0f, 15f), 90f),
                (new Vector3(18f, 0f, -16f), 90f),
                (new Vector3(-18f, 0f, -17f), 0f)
            };

            var index = 0;
            foreach (var (position, yaw) in layout)
            {
                index++;
                var center = position + new Vector3(0f, ContainerSize.y * 0.5f, 0f);
                var container = CreateBox($"Container_{index:D2}", center, ContainerSize);
                container.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                container.transform.SetParent(parent, worldPositionStays: true);

                // 交替使用两种明确的橙色调，便于在斜俯视下区分相邻箱体、判断空间关系。
                // 注意配色必须是"红 > 绿 > 蓝"的暖色系才能读出集装箱的观感：
                // 若绿色分量高于红色，物体在 URP 光照下会呈现紫色，与预期完全相反。
                var containerColor = index % 2 == 0
                    ? new Color(0.82f, 0.48f, 0.22f)
                    : new Color(0.68f, 0.38f, 0.18f);
                SetMaterialColor(container, containerColor);
            }
        }

        /// <summary>创建玩家对象，挂载表现层组件。</summary>
        private static void CreatePlayer()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = PlayerSpawn + new Vector3(0f, 1f, 0f);

            SetMaterialColor(player, new Color(0.25f, 0.6f, 0.95f));

            // 用于指示朝向：在角色前方放一个小方块，方便在画面中判断转向是否正确。
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingIndicator";
            nose.transform.SetParent(player.transform, worldPositionStays: false);
            nose.transform.localPosition = new Vector3(0f, 0.2f, 0.55f);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.5f);
            SetMaterialColor(nose, new Color(0.95f, 0.85f, 0.2f));

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
            serialized.FindProperty("m_PlayerSpawnPosition").vector2Value = Vector2.zero;
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
            camera.fieldOfView = 50f;
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
            serialized.FindProperty("m_PitchDegrees").floatValue = 45f;
            serialized.FindProperty("m_Distance").floatValue = 14f;
            serialized.FindProperty("m_WorldOffset").vector3Value = new Vector3(0f, 1.5f, 0f);
            serialized.FindProperty("m_SmoothTime").floatValue = 0.12f;
            serialized.FindProperty("m_LookAheadDistance").floatValue = 2.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            controller.SnapToTarget();
            return cameraObject;
        }

        /// <summary>创建一个带碰撞体的方块，作为灰盒几何体。</summary>
        private static GameObject CreateBox(string name, Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            return box;
        }

        /// <summary>
        /// 设置对象的材质颜色。
        /// </summary>
        /// <remarks>
        /// 通过创建材质资产而非直接改 renderer.material，是为了避免在场景中
        /// 隐式生成匿名材质实例——那会让材质无法被版本控制统一管理。
        /// </remarks>
        private static void SetMaterialColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader) { name = $"Greybox_{target.name}" };
            material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
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
