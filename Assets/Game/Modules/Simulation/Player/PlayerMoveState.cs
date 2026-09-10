using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 玩家当前的移动状态。纯数据，不包含任何行为。
    /// </summary>
    /// <remarks>
    /// 本类型的字段全部与 Unity 无关，因此移动模拟可以在 EditMode 测试中
    /// 直接构造与断言，无需启动游戏或加载场景。
    /// </remarks>
    public struct PlayerMoveState
    {
        /// <summary>当前所在位置（世界坐标的平面投影）。</summary>
        public Vector2F Position;

        /// <summary>
        /// 角色朝向（单位向量）。
        /// 朝向与移动方向刻意分离：俯视角下玩家经常需要侧向移动同时朝前瞄准，
        /// 若两者绑定，就无法做出这类操作。
        /// </summary>
        public Vector2F Facing;

        /// <summary>本帧的实际移动速度（单位/秒）。用于计算噪音与判断是否在奔跑。</summary>
        public float CurrentSpeed;

        /// <summary>当前体力值。</summary>
        public float Stamina;

        /// <summary>是否处于力竭状态：体力耗尽后无法奔跑，直到恢复到力竭阈值以上。</summary>
        public bool IsExhausted;

        /// <summary>是否正在奔跑（本帧移动速度达到奔跑判定阈值）。</summary>
        public bool IsSprinting;

        /// <summary>创建初始状态。</summary>
        /// <param name="position">初始位置。</param>
        /// <param name="facing">初始朝向。传入零向量时默认朝右。</param>
        /// <param name="maxStamina">体力上限，用于把初始体力填满。</param>
        public static PlayerMoveState CreateInitial(Vector2F position, Vector2F facing, float maxStamina)
        {
            return new PlayerMoveState
            {
                Position = position,
                Facing = facing.IsNearlyZero ? Vector2F.Right : facing.Normalized,
                CurrentSpeed = 0f,
                Stamina = maxStamina,
                IsExhausted = false,
                IsSprinting = false
            };
        }

        public override string ToString()
        {
            return $"Pos={Position} Facing={Facing} Speed={CurrentSpeed:F2} Stamina={Stamina:F1}";
        }
    }
}
