using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 安全屋场景生成器：局外空间的灰盒。
    /// </summary>
    /// <remarks>
    /// 与战局地图一样由代码生成（可重复、可版本控制、改布局只需改常量）。
    /// 安全屋是玩家在局外待的地方：仓库、商人、靶子与出口都在这里，
    /// 出击前的准备动作全部发生在这个空间里，而不是菜单里。
    /// <b>它必须是绝对安全区</b>：没有敌人、没有计时、不会掉任何东西。
    /// </remarks>
    public static partial class SafeHouseSceneBuilder
    {
        /// <summary>生成场景的输出路径（仓库相对路径）。</summary>
        private const string ScenePath = "Assets/Game/Content/Scenes/SafeHouse.unity";

        /// <summary>房间尺寸（米）。小是刻意的：安全屋是功能空间，不是地图。</summary>
        private const float RoomWidth = 24f;

        private const float RoomDepth = 18f;

        /// <summary>墙高与厚度。</summary>
        private const float WallHeight = 4f;

        private const float WallThickness = 0.6f;

        /// <summary>玩家出生点：南侧中央，一进来就能看到北面的三个设施。</summary>
        private static readonly Vector3 PlayerSpawn = new Vector3(0f, 0f, -5f);

        /// <summary>相机参数与战局保持一致：玩家在两个场景里的视角手感必须相同。</summary>
        private const float CameraPitchDegrees = 62f;

        private const float CameraDistance = 13f;

        private const float CameraFieldOfView = 55f;

        /// <summary>创建玩家。根节点在脚底，身体与头部向上堆叠（与战局一致）。</summary>
        private static void CreatePlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = PlayerSpawn;

            var collider = player.AddComponent<CapsuleCollider>();
            collider.height = 1.8f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.9f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(player.transform, worldPositionStays: false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            SetColor(body, new Color(0.25f, 0.6f, 0.95f));

            player.AddComponent<RaidDemo.Presentation.PlayerMotor>();
        }

        /// <summary>相机与启动对象。</summary>
        private static void CreateBootstrap()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = CameraFieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.14f, 0.15f, 0.18f);
            cameraObject.AddComponent<AudioListener>();

            var controller = cameraObject.AddComponent<RaidDemo.Presentation.TopDownCameraController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("m_PitchDegrees").floatValue = CameraPitchDegrees;
            serialized.FindProperty("m_Distance").floatValue = CameraDistance;
            serialized.FindProperty("m_WorldOffset").vector3Value = new Vector3(0f, 1f, 0f);
            serialized.FindProperty("m_SmoothTime").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            controller.SnapToTarget();

            var bootstrapObject = new GameObject("SafeHouseBootstrap");
            var collector = bootstrapObject.AddComponent<RaidDemo.Input.PlayerInputCollector>();
            bootstrapObject.AddComponent<RaidDemo.Bootstrap.SafeHouseBootstrap>();

            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");
            if (actions != null)
            {
                var collectorSerialized = new SerializedObject(collector);
                collectorSerialized.FindProperty("m_Actions").objectReferenceValue = actions;
                collectorSerialized.FindProperty("m_AimCamera").objectReferenceValue = camera;
                collectorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>给对象挂上交互标记并写入类型与显示名。</summary>
        private static void AddInteractable(
            GameObject host,
            RaidDemo.Presentation.SafeHouseInteractable.Kind kind,
            string displayName,
            Vector3 position)
        {
            host.transform.position = position;
            var marker = host.AddComponent<RaidDemo.Presentation.SafeHouseInteractable>();
            var serialized = new SerializedObject(marker);
            serialized.FindProperty("m_Kind").enumValueIndex = (int)kind;
            serialized.FindProperty("m_DisplayName").stringValue = displayName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>生成安全屋场景。</summary>
        [MenuItem("RaidDemo/生成安全屋场景")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateRoom();
            CreateFacilities();
            CreateTargets();
            CreateSignBoard();
            CreatePlayer();
            CreateBootstrap();

            EnsureFolder("Assets/Game/Content");
            EnsureFolder("Assets/Game/Content/Scenes");

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[RaidDemo] 安全屋场景已生成：{ScenePath}");
        }

        /// <summary>平行光。与战局同样的角度，避免两个场景的光影观感不一致。</summary>
        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>
        /// 三个靶子，沿东侧排开。
        /// </summary>
        /// <remarks>
        /// 靶子不进战斗层的单位表（见 <c>ShootingTarget</c> 的说明），
        /// 因此它们不会出现在击杀统计里，也不会被 AI 当成目标。
        /// </remarks>
        private static void CreateTargets()
        {
            var root = new GameObject("Targets").transform;
            var positions = new[]
            {
                new Vector3(6f, 0f, -3f),
                new Vector3(8.5f, 0f, -3f),
                new Vector3(11f, 0f, -3f),
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var host = new GameObject($"Target_{i + 1:D2}");
                host.transform.SetParent(root, worldPositionStays: false);
                host.transform.position = positions[i];

                var post = CreateBox(
                    "Post",
                    positions[i] + new Vector3(0f, 0.5f, 0f),
                    new Vector3(0.18f, 1f, 0.18f),
                    host.transform);
                SetColor(post, new Color(0.35f, 0.35f, 0.38f));

                var plate = CreateBox(
                    "Plate",
                    positions[i] + new Vector3(0f, 1.4f, 0f),
                    new Vector3(0.7f, 0.9f, 0.12f),
                    host.transform);
                SetColor(plate, new Color(0.88f, 0.86f, 0.80f));

                host.AddComponent<RaidDemo.Presentation.ShootingTarget>();
            }
        }

        /// <summary>
        /// 写在墙上的操作说明。
        /// </summary>
        /// <remarks>
        /// 用世界空间画布而不是贴在屏幕上的 UI：说明应当属于这个空间，
        /// 玩家走到墙前读它——这也是把「操作说明」从主菜单搬进安全屋的意义。
        /// </remarks>
        private static void CreateSignBoard()
        {
            var board = CreateBox(
                "SignBoard",
                new Vector3(0f, 2.2f, 8.2f),
                new Vector3(9f, 3f, 0.15f),
                null);
            SetColor(board, new Color(0.12f, 0.13f, 0.16f));

            // 画的牌子与文字的载体刻意**不做父子关系**：牌子是个被缩放到 9x3x0.15 的立方体，
            // 文字若挂在它下面会继承那份非等比缩放，字会被拉得又大又扁（还会看起来是镜像的）。
            // 文字自己作为根节点、只做等比缩放，位置摆在牌面正前方。
            var canvasHost = new GameObject("SignText", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasHost.transform;
            rect.sizeDelta = new Vector2(900f, 300f);
            rect.position = new Vector3(0f, 2.2f, 8f);
            // 不旋转：世界空间画布的正面朝向 +Z（房间在 z 更小的一侧），
            // 从房间里看过去正好是正面。加 180 度反而会看到镜像的文字。
            rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.01f;

            var textHost = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var textRect = (RectTransform)textHost.transform;
            textRect.SetParent(rect, worldPositionStays: false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textHost.GetComponent<Text>();
            text.font = RaidDemo.UI.UiFontProvider.Get(28);
            text.fontSize = 28;
            text.color = new Color(0.92f, 0.92f, 0.95f);
            text.alignment = TextAnchor.MiddleCenter;
            text.text =
                "安全屋 · 出击准备\n\n" +
                "WASD 移动    鼠标 瞄准    左键 射击\n" +
                "R 装弹    滚轮 / 1 2 换武器    Tab 背包    E 交互\n" +
                "H 使用医疗品    右键菜单\n\n" +
                "走到仓库前按 E 整理装备，走到出口前按 E 选择地图出击。\n" +
                "阵亡会丢掉随身携带的一切，仓库里的东西永远安全。";
        }

        /// <summary>地面与四面墙。</summary>
        private static void CreateRoom()
        {
            var room = new GameObject("Room").transform;

            var floor = CreateBox("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(RoomWidth, 1f, RoomDepth), room);
            SetColor(floor, new Color(0.34f, 0.33f, 0.30f));

            var halfX = (RoomWidth * 0.5f) - (WallThickness * 0.5f);
            var halfZ = (RoomDepth * 0.5f) - (WallThickness * 0.5f);
            var wallColor = new Color(0.46f, 0.43f, 0.38f);

            CreateWall("Wall_West", -halfX, 0f, RoomDepth, false, room, wallColor);
            CreateWall("Wall_East", halfX, 0f, RoomDepth, false, room, wallColor);
            CreateWall("Wall_South", 0f, -halfZ, RoomWidth, true, room, wallColor);
            CreateWall("Wall_North", 0f, halfZ, RoomWidth, true, room, wallColor);
        }

        /// <summary>
        /// 三处设施：仓库（西）、商人（中）、出口（东），沿北墙一字排开。
        /// </summary>
        /// <remarks>排成一排是为了**一眼看全**：从南侧出生抬头就知道这里能做什么。设施变多后再分区。</remarks>
        private static void CreateFacilities()
        {
            var root = new GameObject("Facilities").transform;

            CreateStashBox(root, new Vector3(-8f, 0f, 5f));
            CreateMerchantStall(root, new Vector3(-2f, 0f, 5f));
            CreateExitGate(root, new Vector3(5f, 0f, 5f));
        }

        /// <summary>仓库箱：一个带交互标记的箱子。</summary>
        private static void CreateStashBox(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Stash");
            host.transform.SetParent(parent, worldPositionStays: false);

            var box = CreateBox(
                "StashBox",
                position + new Vector3(0f, 0.7f, 0f),
                new Vector3(2f, 1.4f, 1.2f),
                host.transform);
            SetColor(box, new Color(0.30f, 0.42f, 0.34f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Stash, "仓库", position);
        }

        /// <summary>商人摊位：一张柜台 + 一个「人」的占位块。</summary>
        private static void CreateMerchantStall(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Merchant");
            host.transform.SetParent(parent, worldPositionStays: false);

            var counter = CreateBox(
                "Counter",
                position + new Vector3(0f, 0.55f, 0f),
                new Vector3(2.4f, 1.1f, 1f),
                host.transform);
            SetColor(counter, new Color(0.52f, 0.42f, 0.28f));

            var keeper = CreateBox(
                "Keeper",
                position + new Vector3(0f, 1.7f, -0.8f),
                new Vector3(0.6f, 1.8f, 0.6f),
                host.transform);
            SetColor(keeper, new Color(0.85f, 0.72f, 0.2f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Merchant, "商人", position);
        }

        /// <summary>出口：一道门框，走近选地图。</summary>
        private static void CreateExitGate(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Exit");
            host.transform.SetParent(parent, worldPositionStays: false);

            var pad = CreateBox(
                "Pad",
                position + new Vector3(0f, 0.03f, 0f),
                new Vector3(3f, 0.06f, 3f),
                host.transform);
            SetColor(pad, new Color(0.18f, 0.62f, 0.38f));

            var left = CreateBox(
                "Post_L",
                position + new Vector3(-1.5f, 1.4f, 0f),
                new Vector3(0.25f, 2.8f, 0.25f),
                host.transform);
            var right = CreateBox(
                "Post_R",
                position + new Vector3(1.5f, 1.4f, 0f),
                new Vector3(0.25f, 2.8f, 0.25f),
                host.transform);
            SetColor(left, new Color(0.16f, 0.18f, 0.20f));
            SetColor(right, new Color(0.16f, 0.18f, 0.20f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Exit, "出口", position);
        }
    }
}
