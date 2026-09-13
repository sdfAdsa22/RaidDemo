using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Bootstrap.Editor;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 角色动画的"参数与阈值是否与玩法对得上"的资产测试。
    /// </summary>
    /// <remarks>
    /// <para>这一组测试针对的是同一类缺陷：**动画控制器里的常量写在了另一处，
    /// 而那一处在玩法调整后没有跟着改**。它不报错、不崩溃，只是"某个动作永远不播"
    /// 或者"逻辑认为在跑、画面上在走"，只能靠盯着屏幕看才发现。</para>
    /// <para>把这些关系写成断言之后，调 AI 速度档位时会立刻看到失败，
    /// 而不是等验收时被问"敌人怎么从来不跑"。</para>
    /// </remarks>
    [TestFixture]
    public sealed class CharacterAnimationWiringTests
    {
        private const string PlayerControllerPath =
            "Assets/Game/Content/Art/Characters/Player/PlayerCharacter.controller";

        // male-a 继续使用旧路径 PlayerCharacter.prefab（其余 11 个角色才带 _id 后缀）。
        private const string PlayerPrefabPath =
            "Assets/Game/Content/Art/Characters/Player/PlayerCharacter.prefab";

        private static readonly string[] EnemyControllerPaths =
        {
            "Assets/Game/Content/Art/Characters/Enemies/Character_Soldier.controller",
            "Assets/Game/Content/Art/Characters/Enemies/Character_Hazmat.controller",
            "Assets/Game/Content/Art/Characters/Enemies/Character_Enemy.controller"
        };

        private static readonly string[] EnemyPrefabPaths =
        {
            "Assets/Game/Content/Art/Characters/Enemies/Character_Soldier.prefab",
            "Assets/Game/Content/Art/Characters/Enemies/Character_Hazmat.prefab",
            "Assets/Game/Content/Art/Characters/Enemies/Character_Enemy.prefab"
        };

        [Test]
        public void 敌人跑步阈值落在AI实际会走出的速度区间内()
        {
            var profile = new AIPerceptionProfile();

            Assert.Greater(EnemyCharacterBuilder.RunSpeedThreshold, profile.EngageSpeed,
                "跑步阈值不能低于交战速度，否则敌人一边交火一边迈跑步的腿。");
            Assert.LessOrEqual(EnemyCharacterBuilder.RunSpeedThreshold, profile.RetreatSpeed,
                $"跑步阈值 {EnemyCharacterBuilder.RunSpeedThreshold} 高于 AI 的最高速度 " +
                $"{profile.RetreatSpeed}，Run 状态永远不会触发——这正是 A-01 的成因。");
        }

        [Test]
        public void 玩家动画控制器用布尔参数判定奔跑而不是速度阈值()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            Assert.IsNotNull(controller, $"玩家动画控制器缺失：{PlayerControllerPath}");

            var hasSprinting = false;
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == "Sprinting" && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasSprinting = true;
                    break;
                }
            }

            Assert.IsTrue(hasSprinting,
                "玩家控制器缺少 Bool 参数 Sprinting：奔跑必须由逻辑层的判定驱动，" +
                "不能在表现层另写一个速度阈值（A-04）。");

            var walkToSprint = FindTransition(controller, "Walk", "Sprint");
            Assert.IsNotNull(walkToSprint, "玩家控制器里找不到 Walk → Sprint 的过渡。");

            var usesSprinting = false;
            var usesSpeed = false;
            foreach (var condition in walkToSprint.conditions)
            {
                if (condition.parameter == "Sprinting")
                {
                    usesSprinting = true;
                }

                if (condition.parameter == "Speed")
                {
                    usesSpeed = true;
                }
            }

            Assert.IsTrue(usesSprinting, "Walk → Sprint 必须由 Sprinting 参数决定。");
            Assert.IsFalse(usesSpeed, "Walk → Sprint 不应再依赖写死的速度阈值。");
        }

        [Test]
        public void 玩家移动状态绑定播放倍率而一次性动作不绑定()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            Assert.IsNotNull(controller, $"玩家动画控制器缺失：{PlayerControllerPath}");

            AssertLocomotionParameter(controller);
            AssertLocomotionRateBound(controller, "Walk");
            AssertLocomotionRateBound(controller, "Sprint");
            AssertLocomotionRateNotBound(controller, "Idle");
            AssertLocomotionRateNotBound(controller, "ArmedIdle");
            AssertLocomotionRateNotBound(controller, "Shoot");
            AssertLocomotionRateNotBound(controller, "Die");
        }

        [Test]
        public void 敌人移动状态绑定播放倍率而一次性动作不绑定()
        {
            for (var i = 0; i < EnemyControllerPaths.Length; i++)
            {
                var path = EnemyControllerPaths[i];
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Assert.IsNotNull(controller, $"敌人动画控制器缺失：{path}");

                AssertLocomotionParameter(controller);
                AssertLocomotionRateBound(controller, "Walk");
                AssertLocomotionRateBound(controller, "Run");
                AssertLocomotionRateNotBound(controller, "Idle");
                AssertLocomotionRateNotBound(controller, "Idle_Shoot");
                AssertLocomotionRateNotBound(controller, "Death");
                AssertLocomotionRateNotBound(controller, "HitReact");
            }
        }

        [Test]
        public void 预制体写入的设计速度与控制器绑定的剪辑一致()
        {
            AssertPrefabBinding(PlayerPrefabPath, PlayerControllerPath, "Sprint", 0f);

            for (var i = 0; i < EnemyPrefabPaths.Length; i++)
            {
                AssertPrefabBinding(
                    EnemyPrefabPaths[i],
                    EnemyControllerPaths[i],
                    "Run",
                    EnemyCharacterBuilder.RunSpeedThreshold);
            }
        }

        [Test]
        public void 敌人模型不再导入重复命名的剪辑()
        {
            const string modelPath =
                "Assets/Game/Content/External/Quaternius/ToonShooter/Characters/FBX/Character_Enemy.fbx";
            var prefixedCount = 0;
            var duplicateNames = string.Empty;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                if (clip.name.Contains("|"))
                {
                    prefixedCount++;
                }
                else
                {
                    duplicateNames = duplicateNames.Length == 0
                        ? clip.name
                        : duplicateNames + ", " + clip.name;
                }
            }

            Assert.Greater(prefixedCount, 0, $"{modelPath} 没有导入任何 CharacterArmature|... 剪辑。");
            Assert.IsEmpty(
                duplicateNames,
                $"{modelPath} 又导入了无前缀的重复剪辑（{duplicateNames}）：" +
                "它们没有被任何控制器引用，只会拖慢导入；清理依据见 A-06 与排障手册。");
        }

        /// <summary>断言控制器里有默认值为 1 的播放倍率浮点参数。</summary>
        private static void AssertLocomotionParameter(AnimatorController controller)
        {
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name != LocomotionAnimationBinding.RateParameterName)
                {
                    continue;
                }

                Assert.AreEqual(
                    AnimatorControllerParameterType.Float,
                    parameter.type,
                    $"{LocomotionAnimationBinding.RateParameterName} 必须是 Float 参数。");
                Assert.AreEqual(
                    1f,
                    parameter.defaultFloat,
                    0.0001f,
                    "播放倍率参数的默认值必须是 1：视图第一次写参数之前若为 0，移动状态会定格在第一帧。");
                return;
            }

            Assert.Fail($"控制器缺少浮点参数 {LocomotionAnimationBinding.RateParameterName}（A-02）。");
        }

        /// <summary>断言某个移动状态把播放倍率交给了共用参数。</summary>
        private static void AssertLocomotionRateBound(AnimatorController controller, string stateName)
        {
            var state = FindState(controller, stateName);
            Assert.IsNotNull(state, $"控制器里找不到状态 {stateName}。");
            Assert.IsTrue(
                state.speedParameterActive
                && state.speedParameter == LocomotionAnimationBinding.RateParameterName,
                $"状态 {stateName} 没有把播放倍率交给 {LocomotionAnimationBinding.RateParameterName}，" +
                "移动时会继续按剪辑原始速度播放（A-02）。");
        }

        /// <summary>断言一次性动作没有被播放倍率参数影响。</summary>
        private static void AssertLocomotionRateNotBound(AnimatorController controller, string stateName)
        {
            var state = FindState(controller, stateName);
            if (state == null)
            {
                return;
            }

            Assert.IsFalse(
                state.speedParameterActive,
                $"状态 {stateName} 不应绑定播放倍率：加速开火 / 受击 / 倒地会改变一次性动作的手感。");
        }

        /// <summary>核对预制体里的设计速度确实对应控制器绑定的剪辑。</summary>
        private static void AssertPrefabBinding(
            string prefabPath,
            string controllerPath,
            string runStateName,
            float expectedRunSwitchSpeed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"角色预制体缺失：{prefabPath}");

            var binding = prefab.GetComponentInChildren<LocomotionAnimationBinding>(true);
            Assert.IsNotNull(
                binding,
                $"{prefabPath} 缺少 LocomotionAnimationBinding：移动动画不会随速度缩放（A-02）。");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsNotNull(controller, $"动画控制器缺失：{controllerPath}");

            var walkState = FindState(controller, "Walk");
            var runState = FindState(controller, runStateName);
            Assert.IsNotNull(walkState, $"{controllerPath} 缺少 Walk 状态。");
            Assert.IsNotNull(runState, $"{controllerPath} 缺少 {runStateName} 状态。");

            Assert.AreEqual(
                LocomotionAnimationRate.DesignSpeed(
                    walkState.motion as AnimationClip,
                    FootstepCadence.WalkStrideMeters),
                binding.WalkDesignSpeed,
                0.001f,
                $"{prefabPath} 的走路设计速度与控制器绑定的剪辑不一致：换素材后必须重跑角色构建器。");
            Assert.AreEqual(
                LocomotionAnimationRate.DesignSpeed(
                    runState.motion as AnimationClip,
                    FootstepCadence.SprintStrideMeters),
                binding.RunDesignSpeed,
                0.001f,
                $"{prefabPath} 的跑步设计速度与控制器绑定的剪辑不一致：换素材后必须重跑角色构建器。");
            Assert.AreEqual(
                expectedRunSwitchSpeed,
                binding.RunSwitchSpeed,
                0.001f,
                $"{prefabPath} 的跑步切换阈值与构建器常量不一致。");
        }

        /// <summary>在控制器的默认层里按名字查状态。</summary>
        private static AnimatorState FindState(AnimatorController controller, string stateName)
        {
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                if (state.state.name == stateName)
                {
                    return state.state;
                }
            }

            return null;
        }

        /// <summary>在控制器的默认层里查找一条状态之间的过渡。</summary>
        private static AnimatorStateTransition FindTransition(
            AnimatorController controller,
            string fromState,
            string toState)
        {
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                if (state.state.name != fromState)
                {
                    continue;
                }

                foreach (var transition in state.state.transitions)
                {
                    if (transition.destinationState != null && transition.destinationState.name == toState)
                    {
                        return transition;
                    }
                }
            }

            return null;
        }
    }
}
