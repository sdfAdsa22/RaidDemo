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

        /// <summary>跑步阈值（米/秒），需与移动配置的冲刺阈值一致。</summary>
        private const float SprintThreshold = 5f;

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

            EnsureLoopingClips();
            var controllerPath = BuildController();
            var prefabPath = BuildPrefab(controllerPath);
            return $"controller={controllerPath}\nprefab={prefabPath}";
        }

        /// <summary>
        /// 给循环类剪辑打开 Loop Time。
        /// </summary>
        /// <remarks>
        /// 不打开的话，剪辑播放一次就停在最后一帧：角色位置仍被移动逻辑推着走，
        /// 看起来就是"走两步之后开始滑步"。一次性动作（射击、倒地、拾取）必须保持不循环。
        /// </remarks>
        private static void EnsureLoopingClips()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            var looping = new HashSet<string> { "idle", "walk", "sprint", "holding-right", "holding-left", "holding-both" };
            foreach (var clip in clips)
            {
                if (clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                clip.loopTime = looping.Contains(clip.name);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>创建动画控制器：Idle / ArmedIdle / Walk / Sprint / Shoot / Die。</summary>
        private static string BuildController()
        {
            var path = ArtFolder + "/PlayerCharacter.controller";
            AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
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

            AddTransition(idle, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            AddTransition(armedIdle, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            AddTransition(walk, idle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f), ("Armed", AnimatorConditionMode.IfNot, 0f));
            AddTransition(walk, armedIdle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f), ("Armed", AnimatorConditionMode.If, 0f));
            AddTransition(walk, sprint, 0.1f, ("Speed", AnimatorConditionMode.Greater, SprintThreshold));
            AddTransition(sprint, walk, 0.1f, ("Speed", AnimatorConditionMode.Less, SprintThreshold));
            AddTransition(sprint, armedIdle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f));
            AddTransition(idle, armedIdle, 0.15f, ("Armed", AnimatorConditionMode.If, 0f));
            AddTransition(armedIdle, idle, 0.15f, ("Armed", AnimatorConditionMode.IfNot, 0f));

            var toShoot = machine.AddAnyStateTransition(shoot);
            toShoot.hasExitTime = false;
            toShoot.duration = 0.05f;
            toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            AddExitTransition(shoot, armedIdle, 0.15f);

            var toDie = machine.AddAnyStateTransition(die);
            toDie.hasExitTime = false;
            toDie.duration = 0.1f;
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            AssetDatabase.SaveAssets();
            return path;
        }

        /// <summary>添加一个状态并绑定 Kenney 动画剪辑（自动跳过 __preview__ 副本）。</summary>
        private static AnimatorState AddState(AnimatorStateMachine machine, string name, string clipName)
        {
            var state = machine.AddState(name);
            state.motion = LoadClip(clipName);
            return state;
        }

        /// <summary>添加"到达条件后切换"的过渡。</summary>
        private static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            float duration,
            params (string Name, AnimatorConditionMode Mode, float Threshold)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Name);
            }
        }

        /// <summary>添加"播放完再切换"的过渡（用于开火、死亡这类一次性动作）。</summary>
        private static void AddExitTransition(AnimatorState from, AnimatorState to, float duration)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = duration;
        }

        /// <summary>从模型文件里按名称取动画剪辑。</summary>
        private static AnimationClip LoadClip(string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
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
