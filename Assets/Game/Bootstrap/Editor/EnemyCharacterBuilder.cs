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
        /// <remarks>
        /// <para>名单里的 <c>Walk_Shoot</c> / <c>Run_Gun</c> / <c>Run_Shoot</c> 不是"额外想循环的动作"，
        /// 而是会被兜底链当成走路或跑步播放的剪辑：士兵模型没有 Walk，走路状态取的就是 Run_Gun。
        /// 它们漏在名单外，表现就是"敌人正常走 0.73 秒，然后保持最后一帧姿势在地面上滑行"。</para>
        /// <para>这份名单只是一次性批量校准；真正防止漏网的是
        /// <see cref="EnsureStateLoops"/>，它按"用途"在绑定循环状态时再确认一次。</para>
        /// </remarks>
        private static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Run", "Duck", "Walk_Shoot", "Run_Gun", "Run_Shoot"
        };

        /// <summary>
        /// 模型自带的整套武器（全部挂在右手节点下，且会同时显示）。
        /// 只保留 <see cref="PreferredWeapon"/> 一把，其余删除——否则敌人身上会插着十几把武器，
        /// 其中按 1.0 比例建模的那把看起来"枪特别大"。
        /// </summary>
        private static readonly HashSet<string> WeaponNodes = new HashSet<string>
        {
            "AK", "GrenadeLauncher", "Knife_1", "Knife_2", "Pistol", "Revolver", "Revolver_Small",
            "RocketLauncher", "ShortCannon", "Shotgun", "Shovel", "SMG", "Sniper", "Sniper_2"
        };

        /// <summary>保留的主武器：AK 是这套模型里比例最正常的一把。</summary>
        private const string PreferredWeapon = "AK";

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
                var loopSettingsChanged = EnsureLoopingClips(name);
                var controllerPath = BuildController(name);
                var prefabPath = BuildPrefab(name, controllerPath);
                summary.Append(prefabPath)
                    .Append(loopSettingsChanged ? "（循环设置已校准）" : string.Empty)
                    .Append('\n');
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
        /// <returns>导入设置发生变化时为 <c>true</c>。</returns>
        private static bool EnsureLoopingClips(string characterName)
        {
            return CharacterAnimationLoopTool.ApplyLoopTable(ModelPath(characterName), LoopingClips);
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

        /// <summary>
        /// 按候选顺序查找剪辑。
        /// </summary>
        /// <remarks>
        /// 三个角色的剪辑集并不一致：Soldier **没有 Walk**，只有 Run / Run_Gun，
        /// 按固定名字取会拿到 null，表现就是"这个敌人走路没有动画"。
        /// 因此走路用 Walk → Walk_Shoot → Run_Gun → Run 依次兜底，跑步同理。
        /// </remarks>
        private static AnimationClip ResolveClip(string characterName, params string[] clipKeys)
        {
            foreach (var key in clipKeys)
            {
                var clip = LoadClip(characterName, key);
                if (clip != null)
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
            var idle = AddState(machine, characterName, "Idle", "Idle_Shoot");
            var walk = AddState(machine, characterName, "Walk", "Walk_Shoot", "Run_Gun", "Run");
            var run = AddState(machine, characterName, "Run", "Run_Gun", "Run_Shoot", "Walk");
            var shoot = AddState(machine, characterName, "Idle_Shoot");
            var death = AddState(machine, characterName, "Death");
            var hit = AddState(machine, characterName, "HitReact");
            machine.defaultState = idle;

            // 冒烟确认：Idle / Walk / Run 是"会一直循环下去"的状态，
            // 万一兜底链选中的剪辑还没开循环，这里当场修掉（详见 CharacterAnimationLoopTool）。
            EnsureStateLoops(characterName, idle);
            EnsureStateLoops(characterName, walk);
            EnsureStateLoops(characterName, run);

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

        /// <summary>
        /// 确认循环状态绑定的剪辑真的会循环，必要时就地打开 Loop Time 并换上新对象。
        /// </summary>
        /// <param name="characterName">角色名（用于定位模型资产）。</param>
        /// <param name="state">刚创建好的循环语义状态。</param>
        /// <remarks>
        /// <para>按"用途"而不是"名字"判断：状态会被长期停留在里面，它绑的剪辑就必须循环，
        /// 名字叫 Walk、Run_Gun 还是别的都无所谓。这条规则比 <see cref="LoopingClips"/> 名单硬，
        /// 将来换模型（新角色可能只有 Run 没有 Walk）也不会再出现"走一小段后定格滑行"。</para>
        /// <para>修不了的时候只报错、不抛异常：控制器其余部分仍然可用，
        /// 但要让人在控制台里一眼看到"这个敌人的循环动画没救回来"。</para>
        /// </remarks>
        private static void EnsureStateLoops(string characterName, AnimatorState state)
        {
            var clip = state.motion as AnimationClip;
            if (clip == null || CharacterAnimationLoopTool.IsLooping(clip))
            {
                return;
            }

            var modelPath = ModelPath(characterName);
            var reloaded = CharacterAnimationLoopTool.EnsureLooping(modelPath, clip);
            if (reloaded == null)
            {
                Debug.LogError(
                    $"[EnemyCharacterBuilder] {characterName} 的循环状态 {state.name} 绑定了不循环的剪辑 " +
                    $"{clip.name}，且无法写入导入设置：敌人会保持姿势滑行。");
                return;
            }

            // 重新导入会让旧对象失效，必须把状态指向新对象，否则动画控制器会引用一个空壳。
            state.motion = reloaded;
            Debug.LogWarning(
                $"[EnemyCharacterBuilder] {characterName} 的循环状态 {state.name} 用了不循环的剪辑 " +
                $"{reloaded.name}，已在构建期自动打开 Loop Time。");
        }

        private static AnimatorState AddState(
            AnimatorStateMachine machine,
            string characterName,
            params string[] clipKeys)
        {
            var state = machine.AddState(clipKeys[0]);
            state.motion = ResolveClip(characterName, clipKeys);
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

            RemoveExtraWeapons(instance);
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
                    material.SetColor("_BaseColor", source.color);
                }

                renderer.sharedMaterial = material;
            }

            EditorUtility.SetDirty(material);
        }
    }
}
