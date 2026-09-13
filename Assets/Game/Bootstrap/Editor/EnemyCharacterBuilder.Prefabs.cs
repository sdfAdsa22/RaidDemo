using System.Collections.Generic;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 敌人角色构建器的预制体部分：生成模型、写动画参数、重赋项目材质。
    /// </summary>
    /// <remarks>
    /// 拆成 partial 是为了让单个源码文件保持在工程规范的 400 行以内；
    /// 构建流程与控制器的部分见 <c>EnemyCharacterBuilder.cs</c>。
    /// </remarks>
    public static partial class EnemyCharacterBuilder
    {
        /// <summary>生成敌人预制体：解包、缩放、落地、挂 Animator、写移动动画参数、重赋材质。</summary>
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

            RemoveExtraWeapons(instance);
            var scale = FitToHeight(instance, TargetHeight);
            var animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            ApplyLocomotionAnimationBinding(characterName, instance);
            ApplyProjectMaterial(characterName, instance);

            PrefabUtility.SaveAsPrefabAsset(container, path);
            Object.DestroyImmediate(container);
            AssetDatabase.SaveAssets();
            return $"{path} (scale={scale:F2})";
        }

        /// <summary>
        /// 把走 / 跑剪辑的设计速度与跑步切换阈值写进模型预制体（A-02）。
        /// </summary>
        /// <remarks>
        /// 剪辑兜底链与动画控制器用的是同一份常量，因此这里算出的设计速度
        /// 永远对应控制器实际绑定的那个剪辑；换素材导致兜底落到别的剪辑时，两边会一起变。
        /// </remarks>
        private static void ApplyLocomotionAnimationBinding(string characterName, GameObject instance)
        {
            var walkClip = ResolveClip(characterName, WalkClipKeys);
            var runClip = ResolveClip(characterName, RunClipKeys);
            var binding = instance.AddComponent<LocomotionAnimationBinding>();
            binding.Configure(
                LocomotionAnimationRate.DesignSpeed(walkClip, FootstepCadence.WalkStrideMeters),
                LocomotionAnimationRate.DesignSpeed(runClip, FootstepCadence.SprintStrideMeters),
                RunSpeedThreshold);
        }

        /// <summary>只保留一把主武器，其余武器节点删除。</summary>
        private static void RemoveExtraWeapons(GameObject instance)
        {
            var toRemove = new List<GameObject>();
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child == instance.transform || !WeaponNodes.Contains(child.name))
                {
                    continue;
                }

                if (child.name != PreferredWeapon)
                {
                    toRemove.Add(child.gameObject);
                }
            }

            foreach (var target in toRemove)
            {
                Object.DestroyImmediate(target);
            }
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
                    M7MaterialLibrary.SetColorIfDifferent(material, "_BaseColor", source.color);

                    // `_Color` 是内置渲染管线时代的遗留别名。本项目的材质都基于 URP，
                    // 渲染只读 `_BaseColor`，但 Unity 保存材质时会把这个属性一起写进文件；
                    // 不同步的话两个属性会长期不一致（实测出现过 `_BaseColor` 灰蓝、`_Color` 深灰），
                    // 将来排查配色问题的人会先被这个假象带偏。
                    M7MaterialLibrary.SetColorIfDifferent(material, "_Color", source.color);
                }

                renderer.sharedMaterial = material;
            }

            EditorUtility.SetDirty(material);
        }
    }
}
