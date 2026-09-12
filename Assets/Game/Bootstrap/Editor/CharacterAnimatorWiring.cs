using UnityEditor.Animations;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 角色动画控制器接线的共用工具。
    /// </summary>
    /// <remarks>
    /// <para>玩家与敌人两个构建器都需要"加一条过渡"这件小事，而且都需要两种语义：
    /// 条件成立就切（走 / 跑 / 待机之间），以及播完再切（开火、受击这类一次性动作）。
    /// 各写一份的代价是两边会慢慢长歪——例如一边补了条件参数、另一边没补。</para>
    /// <para>这里只放这两条与具体角色无关的规则；"什么状态绑什么剪辑"仍然留在各自的构建器里，
    /// 因为那才是玩家与敌人真正的差别。</para>
    /// </remarks>
    internal static class CharacterAnimatorWiring
    {
        /// <summary>添加"条件成立就切换"的过渡。</summary>
        /// <param name="from">源状态。</param>
        /// <param name="to">目标状态。</param>
        /// <param name="duration">过渡时长（秒）。</param>
        /// <param name="conditions">条件列表，全部满足才切换。</param>
        public static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            float duration,
            params (string Name, AnimatorConditionMode Mode, float Threshold)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            AddConditions(transition, conditions);
        }

        /// <summary>添加"播放完再切换"的过渡（用于开火、受击这类一次性动作）。</summary>
        /// <param name="from">源状态。</param>
        /// <param name="to">目标状态。</param>
        /// <param name="duration">过渡时长（秒）。</param>
        /// <param name="conditions">额外的条件；不传表示播完就切。</param>
        public static void AddExitTransition(
            AnimatorState from,
            AnimatorState to,
            float duration,
            params (string Name, AnimatorConditionMode Mode, float Threshold)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = duration;
            AddConditions(transition, conditions);
        }

        private static void AddConditions(
            AnimatorStateTransition transition,
            (string Name, AnimatorConditionMode Mode, float Threshold)[] conditions)
        {
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Name);
            }
        }
    }
}
