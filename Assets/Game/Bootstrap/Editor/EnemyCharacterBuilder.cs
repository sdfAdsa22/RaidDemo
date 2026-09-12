using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 敌人角色资产构建器：用 Quaternius Toon Shooter 的三个角色生成动画控制器与预制体。
    /// </summary>
    /// <remarks>
    /// <para>与玩家角色构建器同构：模型解包后缩放、脚底对齐原点、重赋项目 URP 材质，
    /// Animator 挂在模型实例上（动画剪辑的路径相对模型根节点解析）。</para>
    /// <para>三个角色共用同一套状态：Idle / Walk / Run / Shoot / Death / Hit，
    /// 差别只在模型与贴图，因此控制器逐个生成、预制体逐个保存。</para>
    /// </remarks>
    public static class EnemyCharacterBuilder
    {
        /// <summary>源模型目录（Quaternius Toon Shooter，CC0）。</summary>
        private const string SourceFolder =
            "Assets/Game/Content/External/Quaternius/ToonShooter/Characters/FBX";

        /// <summary>生成物目录（项目自有资产）。</summary>
        private const string ArtFolder = "Assets/Game/Content/Art/Characters/Enemies";

        /// <summary>敌人身高（米），与玩家一致，便于对照视线与弹道。</summary>
        private const float TargetHeight = 1.8f;

        private static readonly string[] CharacterNames =
        {
            "Character_Soldier", "Character_Hazmat", "Character_Enemy"
        };

        /// <summary>需要循环播放的剪辑（其余都是一次性动作）。</summary>
        private static readonly HashSet<string> LoopingClips = new HashSet<string> { "Idle", "Walk", "Run", "Duck" };

        /// <summary>菜单入口：一键重建三个敌人的控制器与预制体。</summary>
        [MenuItem("RaidDemo/M7/重建敌人角色资产")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>重建全部敌人角色资产，返回过程摘要。</summary>
        public static string BuildAll()
        {
            Directory.CreateDirectory(ArtFolder);
            AssetDatabase.Refresh();

            var summary = new System.Text.StringBuilder();
            foreach (var name in CharacterNames)
            {
                EnsureLoopingClips(name);
                var controllerPath = BuildController(name);
                var prefabPath = BuildPrefab(name, controllerPath);
                summary.Append(prefabPath).Append('\n');
            }

            return summary.ToString();
        }

        /// <summary>读取某个角色的模型路径。</summary>
        private static string ModelPath(string characterName)
        {
            return $"{SourceFolder}/{characterName}.fbx";
        }

        /// <summary>
        /// 给循环类剪辑打开 Loop Time（与玩家角色同一处理，否则会"走两步就滑步"）。
        /// </summary>
        private static void EnsureLoopingClips(string characterName)
        {
            var path = ModelPath(characterName);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            foreach (var clip in clips)
            {
                var shortName = clip.name.Contains("|")
                    ? clip.name.Substring(clip.name.LastIndexOf('|') + 1)
                    : clip.name;
                clip.loopTime = LoopingClips.Contains(shortName);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>按名称片段取动画剪辑（剪辑名形如 CharacterArmature|Idle）。</summary>
        private static AnimationClip LoadClip(string characterName, string clipKey)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath(characterName)))
            {
                if (asset is AnimationClip clip
                    && !clip.name.StartsWith("__preview__")
                    && clip.name.EndsWith("|" + clipKey))
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>生成一个敌人的动画控制器。</summary>
        private static string BuildController(string characterName)
        {
            var path = $"{ArtFolder}/{characterName}.controller";
            AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            var idle = AddState(machine, characterName, "Idle");
            var walk = AddState(machine, characterName, "Walk");
            var run = AddState(machine, characterName, "Run");
            var shoot = AddState(machine, characterName, "Idle_Shoot");
            var death = AddState(machine, characterName, "Death");
            var hit = AddState(machine, characterName, "HitReact");
            machine.defaultState = idle;

            AddTransition(idle, walk, 0.15f, ("Speed", AnimatorConditionMode.Greater, 0.2f));
            AddTransition(walk, run, 0.12f, ("Speed", AnimatorConditionMode.Greater, 4.5f));
            AddTransition(run, walk, 0.12f, ("Speed", AnimatorConditionMode.Less, 4.5f));
            AddTransition(walk, idle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f));
            AddTransition(run, idle, 0.15f, ("Speed", AnimatorConditionMode.Less, 0.2f));

            var toShoot = machine.AddAnyStateTransition(shoot);
            toShoot.hasExitTime = false;
            toShoot.duration = 0.05f;
            toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            var shootBack = shoot.AddTransition(idle);
            shootBack.hasExitTime = true;
            shootBack.exitTime = 1f;
            shootBack.duration = 0.1f;

            var toHit = machine.AddAnyStateTransition(hit);
            toHit.hasExitTime = false;
            toHit.duration = 0.05f;
            toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            var hitBack = hit.AddTransition(idle);
            hitBack.hasExitTime = true;
            hitBack.exitTime = 1f;
            hitBack.duration = 0.1f;

            var toDie = machine.AddAnyStateTransition(death);
            toDie.hasExitTime = false;
            toDie.duration = 0.1f;
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            AssetDatabase.SaveAssets();
            return path;
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string characterName, string clipKey)
        {
            var state = machine.AddState(clipKey);
            state.motion = LoadClip(characterName, clipKey);
            return state;
        }

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

        /// <summary>生成敌人预制体：解包、缩放、落地、挂 Animator、重赋材质。</summary>
        private static string BuildPrefab(string characterName, string controllerPath)
        {
            var path = $"{ArtFolder}/{characterName}.prefab";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(characterName));
            var container = new GameObject(characterName);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "Model";
            instance.transform.SetParent(container.transform, worldPositionStays: false);

            // 解包后再改缩放：嵌套预制体实例上的覆盖在保存新预制体时可能被回退（M7-P-01）。
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var scale = FitToHeight(instance, TargetHeight);
            var animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            ApplyProjectMaterial(characterName, instance);

            PrefabUtility.SaveAsPrefabAsset(container, path);
            Object.DestroyImmediate(container);
            AssetDatabase.SaveAssets();
            return $"{path} (scale={scale:F2})";
        }

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

            renderers = instance.GetComponentsInChildren<Renderer>();
            var scaled = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                scaled.Encapsulate(renderers[i].bounds);
            }

            instance.transform.localPosition = new Vector3(0f, -scaled.min.y, 0f);
            return scale;
        }

        /// <summary>把资源包材质换成项目 URP Lit 材质，保留原贴图。</summary>
        private static void ApplyProjectMaterial(string characterName, GameObject instance)
        {
            var materialPath = $"{ArtFolder}/M_{characterName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "M_" + characterName
                };
                material.SetFloat("_Smoothness", 0.05f);
                material.SetFloat("_Metallic", 0f);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var source = renderer.sharedMaterial;
                var texture = source != null ? source.mainTexture : null;
                if (texture != null)
                {
                    material.SetTexture("_BaseMap", texture);
                }
                else if (source != null)
                {
                    material.SetColor("_BaseColor", source.color);
                }

                renderer.sharedMaterial = material;
            }

            EditorUtility.SetDirty(material);
        }
    }
}
