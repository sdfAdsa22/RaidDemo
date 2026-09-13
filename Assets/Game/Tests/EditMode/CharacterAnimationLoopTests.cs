using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 角色动画循环规则的资产测试：会长期停留的状态（Idle / Walk / Run / Sprint）必须绑定循环剪辑。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么值得为它写一条测试：</b>漏勾 Loop Time 不报错、不崩溃、不写日志，
    /// 表现只是"角色先正常走一小段，然后保持最后一帧的姿势在地面上平移"，
    /// 只有进游戏用眼睛看才发现得了。这条用例把"循环状态必须循环"钉在资产上：
    /// 换模型、换剪辑命名、重新构建角色资产时会立刻被拦住。</para>
    ///
    /// <para><b>为什么按状态名判断而不是按剪辑名：</b>状态才是"循环语义"的载体。
    /// 士兵模型没有 Walk 剪辑，走路状态绑的是 Run_Gun；只要剪辑会随状态一直循环，
    /// 就不该因为它叫什么名字而被判成错误。</para>
    /// </remarks>
    [TestFixture]
    public sealed class CharacterAnimationLoopTests
    {
        /// <summary>角色资产目录：玩家与三个敌人都放在这里。</summary>
        private const string CharacterArtFolder = "Assets/Game/Content/Art/Characters";

        /// <summary>会长期停留、因此必须循环的状态名（一次性动作状态不在此列）。</summary>
        private static readonly string[] LoopingStateNames = { "Idle", "ArmedIdle", "Walk", "Run", "Sprint" };

        [Test]
        public void LoopingStates_MustReferenceLoopingClips()
        {
            var guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { CharacterArtFolder });
            Assert.Greater(guids.Length, 0, "角色资产目录下应当存在动画控制器资产。");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Assert.IsNotNull(controller, $"读不到动画控制器：{path}");

                foreach (var layer in controller.layers)
                {
                    foreach (var state in layer.stateMachine.states)
                    {
                        if (!IsLoopingState(state.state.name))
                        {
                            continue;
                        }

                        AssertLoopingState(path, state.state);
                    }
                }
            }
        }

        [Test]
        public void OneShotStates_MustNotLoop()
        {
            // 一次性动作（射击、倒地、受击）如果被打开循环，就会在播完之后反复抽搐，
            // 因此这条规则与上面那条互为边界：循环名单要准，不能"顺手全开"。
            var guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { CharacterArtFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                var loopingClips = CollectLoopingClips(controller);

                foreach (var layer in controller.layers)
                {
                    foreach (var state in layer.stateMachine.states)
                    {
                        var name = state.state.name;
                        if (IsLoopingState(name) || !IsOneShotState(name))
                        {
                            continue;
                        }

                        var clip = state.state.motion as AnimationClip;
                        Assert.IsNotNull(clip, $"{path} 的 {name} 状态没有绑定动画剪辑。");

                        // 共享剪辑的例外：循环状态与一次性状态可能用同一段剪辑
                        // （敌人待机播的就是持枪瞄准的 Idle_Shoot）。一个剪辑只有一份 Loop Time，
                        // 必须以循环状态的需求为准，否则待机播放 0.367 秒后会僵在最后一帧；
                        // 一次性状态只有满足"有 exitTime=1 的过渡、播完必定切走"时才允许共享，
                        // 否则死亡这类会停在原地重播的状态仍然必须被拦住。
                        if (Contains(loopingClips, clip) && HasExitTimeTransition(state.state))
                        {
                            continue;
                        }

                        Assert.IsFalse(
                            AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                            $"{path} 的 {name} 是一次性动作，不应打开 Loop Time（当前剪辑：{clip.name}）。");
                    }
                }
            }
        }

        /// <summary>收集所有循环状态引用的剪辑。</summary>
        private static List<AnimationClip> CollectLoopingClips(AnimatorController controller)
        {
            var clips = new List<AnimationClip>();
            foreach (var layer in controller.layers)
            {
                foreach (var state in layer.stateMachine.states)
                {
                    if (!IsLoopingState(state.state.name))
                    {
                        continue;
                    }

                    if (state.state.motion is AnimationClip clip && !Contains(clips, clip))
                    {
                        clips.Add(clip);
                    }
                }
            }

            return clips;
        }

        /// <summary>该状态是否存在"播完（exitTime=1）就切走"的过渡。</summary>
        private static bool HasExitTimeTransition(AnimatorState state)
        {
            foreach (var transition in state.transitions)
            {
                if (transition.hasExitTime
                    && transition.exitTime >= 1f
                    && transition.destinationState != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>剪辑列表里是否已包含同一个对象。</summary>
        private static bool Contains(List<AnimationClip> clips, AnimationClip clip)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i] == clip)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>该状态是否属于"会长期停留、必须循环"的类型。</summary>
        private static bool IsLoopingState(string stateName)
        {
            foreach (var candidate in LoopingStateNames)
            {
                if (stateName == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>该状态是否属于"播完就该切走"的一次性动作。</summary>
        /// <remarks>
        /// 只认明确列出的名字，而不是"不在循环名单里就算一次性"：
        /// 后者会把将来新增的状态类型（例如蹲下、跳跃）误判成错误，测试就会变成噪音。
        /// </remarks>
        private static bool IsOneShotState(string stateName)
        {
            return stateName == "Shoot"
                || stateName == "Idle_Shoot"
                || stateName == "Death"
                || stateName == "Die"
                || stateName == "HitReact";
        }

        /// <summary>断言某个循环状态绑定了会循环的剪辑。</summary>
        private static void AssertLoopingState(string controllerPath, AnimatorState state)
        {
            var clip = state.motion as AnimationClip;
            Assert.IsNotNull(
                clip,
                $"{controllerPath} 的循环状态 {state.name} 没有绑定动画剪辑，角色会保持默认姿势滑行。");
            Assert.IsTrue(
                AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                $"{controllerPath} 的循环状态 {state.name} 绑定了不循环的剪辑 {clip.name}：" +
                "角色会走一小段后保持最后一帧姿势滑行。");
        }
    }
}
