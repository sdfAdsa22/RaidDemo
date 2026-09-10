using System;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 瞄准求解：把鼠标位置转换为世界空间中的瞄准点与角色朝向。
    /// </summary>
    /// <remarks>
    /// <para>抽成纯函数而非写在 MonoBehaviour 里，原因是这段逻辑包含多个容易出错、
    /// 且难以在场景中手工验证的边界条件：鼠标与角色重合时方向向量长度趋近于零、
    /// 瞄准点超出最大射程、鼠标移出屏幕范围。放在共享层后可以直接用单元测试
    /// 覆盖这些情形，不需要启动游戏，也不需要真实鼠标。</para>
    ///
    /// <para>本类同时服务于两件事：决定角色的朝向，以及决定准星画在哪。
    /// 两者共用同一个输出点，因此不会出现视觉偏差——若各自独立计算，必然会出现
    /// "准星在一个地方、角色朝向另一个地方"的现象。</para>
    /// </remarks>
    public static class AimResolver
    {
        /// <summary>
        /// 把瞄准点限制在以角色为中心的最大射程范围内。
        /// </summary>
        /// <param name="aimPoint">鼠标指示的原始瞄准点。</param>
        /// <param name="origin">角色位置。</param>
        /// <param name="maxAimDistance">
        /// 最大瞄准距离。小于等于 0 表示不限制。
        /// </param>
        /// <returns>限制后的瞄准点。超出射程时会沿原方向拉回边界。</returns>
        /// <remarks>
        /// 限制射程的目的有两个：一是避免玩家把准星指到极远处导致角色朝一个
        /// 几乎无法辨识的方向；二是为后续的武器射程与命中判定提供统一的边界，
        /// 使准星位置始终代表"实际能打到的方向"。
        /// </remarks>
        public static Vector2F ClampToMaxRange(Vector2F aimPoint, Vector2F origin, float maxAimDistance)
        {
            if (maxAimDistance <= 0f)
            {
                return aimPoint;
            }

            var delta = aimPoint - origin;
            var sqrDistance = delta.SqrMagnitude;
            if (sqrDistance <= maxAimDistance * maxAimDistance)
            {
                return aimPoint;
            }

            // 沿原方向等比例缩放到边界上，保证朝向不变、只缩短距离。
            var scale = maxAimDistance / MathF.Sqrt(sqrDistance);
            return origin + (delta * scale);
        }

        /// <summary>
        /// 把屏幕坐标限制在可视区域内。
        /// </summary>
        /// <param name="screenPoint">原始屏幕坐标。</param>
        /// <param name="width">屏幕宽度（像素）。</param>
        /// <param name="height">屏幕高度（像素）。</param>
        /// <returns>限制后的屏幕坐标。</returns>
        /// <remarks>
        /// 锁定鼠标后仍可能出现瞄准点被推出屏幕的情况（例如快速甩动鼠标）。
        /// 若不限制，射线可能打不到地面，导致准星与朝向同时失效。
        /// </remarks>
        public static Vector2F ClampToScreen(Vector2F screenPoint, float width, float height)
        {
            var maxX = MathF.Max(0f, width - 1f);
            var maxY = MathF.Max(0f, height - 1f);

            return new Vector2F(
                Clamp(screenPoint.X, 0f, maxX),
                Clamp(screenPoint.Y, 0f, maxY));
        }

        /// <summary>
        /// 由鼠标的世界投影点求角色应有的朝向。
        /// </summary>
        /// <param name="aimWorldPoint">鼠标在角色所在平面上的投影点。</param>
        /// <param name="origin">角色位置。</param>
        /// <param name="deadZoneRadius">
        /// 死区半径。当投影点与角色的距离小于该值时判定为"瞄准点被吸住"。
        /// </param>
        /// <returns>
        /// 单位朝向向量；当投影点落在死区内时返回零向量，
        /// 表示调用方应保持上一次朝向不变。
        /// </returns>
        /// <remarks>
        /// 死区存在的必要性：当鼠标停在角色身上时，方向向量极短，
        /// 玩家手指的微小抖动或角色的微小位移都会让方向在 360 度内乱跳，
        /// 表现为角色剧烈自转。引入死区后角色会稳定保持原朝向。
        /// </remarks>
        public static Vector2F Resolve(Vector2F aimWorldPoint, Vector2F origin, float deadZoneRadius)
        {
            var delta = aimWorldPoint - origin;
            var threshold = MathF.Max(0f, deadZoneRadius);

            return delta.SqrMagnitude < threshold * threshold
                ? Vector2F.Zero
                : delta.Normalized;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
