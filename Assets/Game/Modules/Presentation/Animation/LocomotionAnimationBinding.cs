using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 移动动画的资产侧参数：每个移动状态的设计速度，以及敌人的走路↔跑步切换阈值。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么挂在模型预制体上：</b>设计速度由模型自带剪辑的长度决定，属于资产数据。
    /// 玩家在运行时可以换装（<see cref="PlayerCharacterView.SetCharacter"/>），
    /// 敌人也是先有逻辑单位、再实例化模型。参数跟着模型走，视图换一份模型就自动读到一份新参数，
    /// 不需要在视图里维护"哪个角色用哪套数字"的分支。</para>
    ///
    /// <para><b>写入方：</b>由角色资产构建器在生成预制体时写入
    /// （<c>PlayerCharacterBuilder</c> / <c>EnemyCharacterBuilder</c>），运行时只读。
    /// 设计速度的推算公式见 <see cref="LocomotionAnimationRate.DesignSpeed"/>。</para>
    ///
    /// <para><b>为什么跑步阈值也在绑定里：</b>敌人视图需要知道"速度超过多少才该按跑步剪辑算倍率"，
    /// 而该阈值同时决定动画控制器的 Walk→Run 过渡。写两份必然有一天对不上，
    /// 因此由构建器把控制器里用的同一个值写进来。玩家不需要它——
    /// 玩家的奔跑判定来自模拟层的 <c>IsSprinting</c> 结论，构建器写入 0 表示"由外部状态决定"。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LocomotionAnimationBinding : MonoBehaviour
    {
        /// <summary>播放倍率参数名。控制器、构建器与运行时视图共用这一个来源。</summary>
        public const string RateParameterName = "LocomotionRate";

        /// <summary>走路状态的设计速度（米/秒）。</summary>
        [SerializeField] private float m_WalkDesignSpeed;

        /// <summary>跑步 / 冲刺状态的设计速度（米/秒）。</summary>
        [SerializeField] private float m_RunDesignSpeed;

        /// <summary>
        /// 进入跑步状态的速度阈值（米/秒）。0 表示由外部状态决定（玩家用 Sprinting 布尔）。
        /// </summary>
        [SerializeField] private float m_RunSwitchSpeed;

        /// <summary>走路状态的设计速度（米/秒）。</summary>
        public float WalkDesignSpeed
        {
            get { return m_WalkDesignSpeed; }
        }

        /// <summary>跑步 / 冲刺状态的设计速度（米/秒）。</summary>
        public float RunDesignSpeed
        {
            get { return m_RunDesignSpeed; }
        }

        /// <summary>进入跑步状态的速度阈值（米/秒）；0 表示由外部状态决定。</summary>
        public float RunSwitchSpeed
        {
            get { return m_RunSwitchSpeed; }
        }

        /// <summary>
        /// 写入参数。只应由角色构建器在生成预制体时调用。
        /// </summary>
        /// <param name="walkDesignSpeed">走路剪辑的设计速度（米/秒）。</param>
        /// <param name="runDesignSpeed">跑步 / 冲刺剪辑的设计速度（米/秒）。</param>
        /// <param name="runSwitchSpeed">跑步切换阈值；0 表示由外部状态决定。</param>
        public void Configure(float walkDesignSpeed, float runDesignSpeed, float runSwitchSpeed)
        {
            m_WalkDesignSpeed = walkDesignSpeed;
            m_RunDesignSpeed = runDesignSpeed;
            m_RunSwitchSpeed = runSwitchSpeed;
        }
    }
}
