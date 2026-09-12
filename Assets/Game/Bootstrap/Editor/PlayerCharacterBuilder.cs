using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 玩家角色资产的构建器：用 Kenney Mini Characters 生成动画控制器与角色预制体。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用代码生成而不是手工拖拽：</b>动画状态、过渡条件与阈值是"会随玩法调整"的数据，
    /// 写成代码后可以 diff、可以复现，也不会出现"预制体里漏连了一根线"这类只在实际运行时才暴露的问题。</para>
    ///
    /// <para><b>动画复用方式：</b>Kenney 的 384 段动画直接驱动骨骼（路径形如 <c>root/torso/head</c>），
    /// 因此 Animator 必须挂在**模型实例**上（它的子节点就叫 root），而不是外层容器上；
    /// 外层容器只负责摆放与缩放。这条约束如果搞错，表现是"模型完全不动"。</para>
    /// </remarks>
    public static class PlayerCharacterBuilder
    {
        /// <summary>角色模型（已提升为正式素材，CC0）。</summary>
        private const string ModelPath = "Assets/Game/Content/External/Kenney/MiniCharacters/character-male-a.fbx";

        /// <summary>调色板贴图：Kenney 角色用一张 8×8 色板 + UV 映射上色。</summary>
        private const string PalettePath = "Assets/Game/Content/External/Kenney/MiniCharacters/colormap.png";

        /// <summary>生成物目录（项目自有资产，随仓库提交）。</summary>
        private const string ArtFolder = "Assets/Game/Content/Art/Characters/Player";

        /// <summary>角色总身高（米），与玩家胶囊 1.8 米对齐。</summary>
        private const float TargetHeight = 1.72f;

        /// <summary>
        /// 需要循环播放的剪辑（Kenney 剪辑名是小写、不带模型前缀）。
        /// </summary>
        /// <remarks>
        /// 不在名单里的是一次性动作（射击、倒地），它们必须保持不循环。
        /// 校准逻辑与"漏勾"后果见 <see cref="CharacterAnimationLoopTool"/>。
        /// </remarks>
        private static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "idle", "walk", "sprint", "holding-right", "holding-left", "holding-both"
        };

        /// <summary>菜单入口：一键重建动画控制器与角色预制体。</summary>
        [MenuItem("RaidDemo/M7/重建玩家角色资产")]
        public static void BuildFromMenu()
        {
            var summary = BuildAll();
            Debug.Log(summary);
        }

        /// <summary>
        /// 重建动画控制器与角色预制体，返回过程摘要。
        /// </summary>
        public static string BuildAll()
        {
            Directory.CreateDirectory(ArtFolder);
            AssetDatabase.Refresh();

            var loopSettingsChanged = EnsureLoopingClips();
            var controllerPath = BuildController();
            var prefabPath = BuildPrefab(controllerPath);
            var loopNote = loopSettingsChanged ? "（循环设置已校准）" : string.Empty;
            return $"controller={controllerPath}{loopNote}\nprefab={prefabPath}";
        }

        /// <summary>
        /// 给循环类剪辑打开 Loop Time。
        /// </summary>
        /// <remarks>
        /// 不打开的话，剪辑播放一次就停在最后一帧：角色位置仍被移动逻辑推着走，
        /// 看起来就是"走两步之后开始滑步"。一次性动作（射击、倒地、拾取）必须保持不循环。
        /// </remarks>
        /// <returns>导入设置发生变化时为 <c>true</c>。</returns>
        private static bool EnsureLoopingClips()
        {
            return CharacterAnimationLoopTool.ApplyLoopTable(ModelPath, LoopingClips);
        }

        /// <summary>创建动画控制器：Idle / ArmedIdle / Walk / Sprint / Shoot / Die。</summary>
        /// <remarks>
        /// <para><b>奔跑用布尔参数而不是速度阈值。</b>冲刺门槛会随负重变化
        /// （超载时 `PlayerMovementProfile` 会按速度修正系数下调门槛），
        /// 表现层若自己再写一个固定速度去比，两边迟早对不上——
        /// 症状是"系统认为你在跑（掉体力、噪音按奔跑算），画面上还在走"。
        /// 现在动画与脚步都直接使用模拟层给出的 <c>IsSprinting</c>，全工程只剩一个判定点。</para>
        /// </remarks>
        private static string BuildController()
        {
            var path = ArtFolder + "/PlayerCharacter.controller";
            AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Sprinting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Armed", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            var idle = AddState(machine, "Idle", "idle");
            var armedIdle = AddState(machine, "ArmedIdle", "holding-right");
            var walk = AddState(machine, "Walk", "walk");
            var sprint = AddState(machine, "Sprint", "sprint");
            var shoot = AddState(machine, "Shoot", "holding-right-shoot");
            var die = AddState(machine, "Die", "die");

            machine.defaultState = idle;

            // 与敌人构建器同一道保险：会长期停留的状态必须绑循环剪辑，
            // 否则玩家会出现"走两步后保持姿势滑行"（M7 批次 1 已经踩过一次）。
            EnsureStateLoops(idle);
            EnsureStateLoops(armedIdle);
            EnsureStateLoops(walk);
            EnsureStateLoops(sprint);

            CharacterAnimatorWiring.AddTransition(idle, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            CharacterAnimatorWiring.AddTransition(armedIdle, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            CharacterAnimatorWiring.AddTransition(walk, idle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f), ("Armed", AnimatorConditionMode.IfNot, 0f));
            CharacterAnimatorWiring.AddTransition(walk, armedIdle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f), ("Armed", AnimatorConditionMode.If, 0f));
            // 顺序有意义：Unity 按添加顺序取第一个条件成立的过渡。
            // "停下来"排在"取消奔跑"前面，否则站定的那一帧会先切回走路再切待机，多一次无谓的过渡。
            CharacterAnimatorWiring.AddTransition(sprint, armedIdle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f));
            CharacterAnimatorWiring.AddTransition(walk, sprint, 0.1f, ("Sprinting", AnimatorConditionMode.If, 0f));
            CharacterAnimatorWiring.AddTransition(sprint, walk, 0.1f,
                ("Sprinting", AnimatorConditionMode.IfNot, 0f),
                ("Speed", AnimatorConditionMode.Greater, 0.2f));
            CharacterAnimatorWiring.AddTransition(idle, armedIdle, 0.15f, ("Armed", AnimatorConditionMode.If, 0f));
            CharacterAnimatorWiring.AddTransition(armedIdle, idle, 0.15f, ("Armed", AnimatorConditionMode.IfNot, 0f));

            var toShoot = machine.AddAnyStateTransition(shoot);
            toShoot.hasExitTime = false;
            toShoot.duration = 0.05f;
            toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");

            // 开火播完回到"当时的移动状态"，而不是一律回站立：
            // 追击中每打一枪闪一下站立，在斜俯视下是非常显眼的割裂感。
            // 三条互斥条件（在走 / 站着持枪 / 站着空手），按顺序判定。
            CharacterAnimatorWiring.AddExitTransition(shoot, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            CharacterAnimatorWiring.AddExitTransition(shoot, armedIdle, 0.15f,
                ("Speed", AnimatorConditionMode.Less, 0.2f),
                ("Armed", AnimatorConditionMode.If, 0f));
            CharacterAnimatorWiring.AddExitTransition(shoot, idle, 0.15f,
                ("Speed", AnimatorConditionMode.Less, 0.2f),
                ("Armed", AnimatorConditionMode.IfNot, 0f));

            var toDie = machine.AddAnyStateTransition(die);
            toDie.hasExitTime = false;
            toDie.duration = 0.1f;
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            AssetDatabase.SaveAssets();
            return path;
        }

        /// <summary>
        /// 确认循环状态绑定的剪辑真的会循环，必要时就地打开 Loop Time 并换上新对象。
        /// </summary>
        /// <param name="state">刚创建好的循环语义状态（Idle / Walk / Sprint 这类）。</param>
        /// <remarks>
        /// 这是 <see cref="LoopingClips"/> 名单之外的"按用途"校验：名单靠名字，
        /// 换素材、换命名时容易漏；状态是循环语义，它绑的剪辑就必须循环。
        /// 修不了时只报错不抛异常，控制器其余部分仍然可用。
        /// </remarks>
        private static void EnsureStateLoops(AnimatorState state)
        {
            var clip = state.motion as AnimationClip;
            if (clip == null || CharacterAnimationLoopTool.IsLooping(clip))
            {
                return;
            }

            var reloaded = CharacterAnimationLoopTool.EnsureLooping(ModelPath, clip);
            if (reloaded == null)
            {
                Debug.LogError(
                    $"[PlayerCharacterBuilder] 循环状态 {state.name} 绑定了不循环的剪辑 {clip.name}，" +
                    "且无法写入导入设置：玩家会保持姿势滑行。");
                return;
            }

            // 重新导入会让旧对象失效，必须把状态指向新对象。
            state.motion = reloaded;
            Debug.LogWarning(
                $"[PlayerCharacterBuilder] 循环状态 {state.name} 用了不循环的剪辑 {reloaded.name}，" +
                "已在构建期自动打开 Loop Time。");
        }

        /// <summary>添加一个状态并绑定 Kenney 动画剪辑（自动跳过 __preview__ 副本）。</summary>
        private static AnimatorState AddState(AnimatorStateMachine machine, string name, string clipName)
        {
            var state = machine.AddState(name);
            state.motion = LoadClip(clipName);
            return state;
        }

        /// <summary>从模型文件里按名称取动画剪辑。</summary>
        private static AnimationClip LoadClip(string clipName)
        {
            return CharacterAnimationLoopTool.FindClip(ModelPath, clipName);
        }

        /// <summary>
        /// 生成玩家角色预制体：外层容器负责摆放，模型实例挂 Animator 并缩放到目标身高。
        /// </summary>
        private static string BuildPrefab(string controllerPath)
        {
            var path = ArtFolder + "/PlayerCharacter.prefab";

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var container = new GameObject("PlayerCharacter");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "Model";
            instance.transform.SetParent(container.transform, worldPositionStays: false);

            // 解包成普通节点再修改：嵌套预制体实例上的缩放/位移属于"实例覆盖"，
            // 直接保存为新预制体时可能被回退（本构建器第一版就踩到了这个坑：
            // 报告缩放 2.56，但存进预制体的仍是 1.0）。解包后数值就是普通序列化数据，
            // 网格与材质仍按 GUID 引用原 FBX，不影响后续替换模型。
            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            var scale = FitToHeight(instance, TargetHeight);

            var animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            ApplyProjectMaterial(instance);

            PrefabUtility.SaveAsPrefabAsset(container, path);
            Object.DestroyImmediate(container);
            AssetDatabase.SaveAssets();
            return $"{path} (scale={scale:F3})";
        }

        /// <summary>把模型缩放到目标身高，并让脚底落在容器原点。</summary>
        private static float FitToHeight(GameObject instance, float targetHeight)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return 1f;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var scale = bounds.size.y > 0.0001f ? targetHeight / bounds.size.y : 1f;
            instance.transform.localScale = Vector3.one * scale;

            // 缩放后重新量一次，把模型整体下移，使脚底正好在 y=0。
            var scaled = instance.GetComponentsInChildren<Renderer>()[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                scaled.Encapsulate(instance.GetComponentsInChildren<Renderer>()[i].bounds);
            }

            instance.transform.localPosition = new Vector3(0f, -scaled.min.y, 0f);
            return scale;
        }

        /// <summary>
        /// 重赋项目材质：用调色板贴图的 URP Lit，而不是资源包自带材质。
        /// </summary>
        /// <remarks>
        /// Kenney 角色靠一张 8×8 色板 + UV 上色，所以材质只需把色板当 BaseMap；
        /// 关键是过滤方式必须是 Point，否则色块之间会互相渗色。
        /// </remarks>
        private static void ApplyProjectMaterial(GameObject instance)
        {
            var materialPath = ArtFolder + "/M_KenneyCharacter.mat";
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            var importer = AssetImporter.GetAtPath(PalettePath) as TextureImporter;
            if (importer != null && importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "M_KenneyCharacter"
                };
                material.SetFloat("_Smoothness", 0.05f);
                material.SetFloat("_Metallic", 0f);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
