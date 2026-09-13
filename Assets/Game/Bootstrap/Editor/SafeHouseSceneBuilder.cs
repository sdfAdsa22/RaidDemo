using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        /// <summary>
        /// 创建玩家。根节点在脚底，身体与头部向上堆叠（与战局一致）。
        /// </summary>
        /// <returns>玩家根节点：墙上的说明牌需要把玩家写进自己的序列化引用，才能判断"玩家走近了"。</returns>
        private static GameObject CreatePlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = PlayerSpawn;

            var collider = player.AddComponent<CapsuleCollider>();
            collider.height = 1.8f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.9f, 0f);

            // M7 批次 1：安全屋与战局使用同一个角色预制体，保证两个场景里「我」是同一个形象。
            const string prefabPath = "Assets/Game/Content/Art/Characters/Player/PlayerCharacter.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.LogWarning($"玩家角色预制体缺失：{prefabPath}");
            }

            player.AddComponent<RaidDemo.Presentation.PlayerMotor>();
            player.AddComponent<RaidDemo.Presentation.PlayerCharacterView>();
            return player;
        }

        /// <summary>相机与启动对象。</summary>
        /// <param name="codexBoard">图鉴展示板上的进度组件，写入启动对象的序列化引用。</param>
        private static void CreateBootstrap(RaidDemo.UI.CodexBoardView codexBoard)
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
            var bootstrap = bootstrapObject.AddComponent<RaidDemo.Bootstrap.SafeHouseBootstrap>();

            // 必须显式写入序列化引用：这些字段只由编辑器装配，运行时不会自己去找对象。
            // 漏掉它们的症状极具迷惑性——相机停在原点朝北看（正好对着墙上的说明牌），
            // 玩家看起来「没有出生」，而实际上角色就在原点、只是没有相机跟随。
            var bootstrapSerialized = new SerializedObject(bootstrap);
            bootstrapSerialized.FindProperty("m_PlayerSpawnPosition").vector2Value =
                new Vector2(PlayerSpawn.x, PlayerSpawn.z);
            bootstrapSerialized.FindProperty("m_PlayerMotor").objectReferenceValue =
                Object.FindFirstObjectByType<RaidDemo.Presentation.PlayerMotor>();
            bootstrapSerialized.FindProperty("m_InputCollector").objectReferenceValue = collector;
            bootstrapSerialized.FindProperty("m_CameraController").objectReferenceValue = controller;
            bootstrapSerialized.FindProperty("m_ItemCatalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RaidDemo.Data.ItemCatalog>(
                    "Assets/Game/Content/Items/ItemCatalog.asset");

            // 图鉴展示板上的进度：运行时由启动对象在局外变化时刷新。
            bootstrapSerialized.FindProperty("m_CodexBoard").objectReferenceValue =
                codexBoard;

            // 表现层资产目录（音效 / 武器模型 / 战斗特效）：安全屋也要能试枪，
            // 缺少它时靶场会变成"打出去没有声音、没有枪口火焰"。
            bootstrapSerialized.FindProperty("m_PresentationCatalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RaidDemo.Presentation.PresentationCatalog>(
                    M7PresentationCatalogBuilder.CatalogPath);
            bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();

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
            var codexBoard = CreateFacilities();
            CreateTargets();
            // 玩家必须早于说明牌创建：说明牌要把玩家对象写进自己的序列化引用。
            var player = CreatePlayer();
            CreateSignBoard(player);
            CreateBootstrap(codexBoard);

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

        /// <summary>三个靶子，沿东侧排开。靶子不进战斗层的单位表（见 ShootingTarget 的说明）。</summary>
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

    }
}
