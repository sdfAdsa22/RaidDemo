using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Bootstrap.Editor;
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
