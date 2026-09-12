using UnityEngine.InputSystem;

namespace RaidDemo.Input
{
    /// <summary>
    /// 输入采集器的「意图读取」部分：战斗、切枪与交互。
    /// </summary>
    /// <remarks>
    /// <para>与移动意图分开，是因为它们的性质不同：移动是每帧持续读取的方向量，
    /// 而战斗、切枪、交互各自有「持续 / 一次性」的区别，放在一起容易看混。</para>
    ///
    /// <para>拆成 partial 文件同时满足项目的单文件行数上限。</para>
    /// </remarks>
    public sealed partial class PlayerInputCollector
    {
        /// <summary>
        /// 读取本帧的战斗输入。
        /// </summary>
        /// <param name="wantsToFire">本帧是否按住射击键。</param>
        /// <param name="wantsToReload">本帧是否刚按下换弹键。</param>
        /// <remarks>
        /// 两个输入的性质不同：射击是**持续**状态（全自动武器需要按住期间每帧都提交意图），
        /// 换弹是**一次性**动作（只在按下的那一帧提交一次）。
        /// 把它们区分开，是为了避免按住换弹键时命令每帧重复派发。
        /// </remarks>
        public void ReadCombatIntent(out bool wantsToFire, out bool wantsToReload)
        {
            Initialize();

            if (UseScriptedInput)
            {
                wantsToFire = ScriptedWantsToFire;
                wantsToReload = ScriptedWantsToReload;
                return;
            }

            if (!m_IsInitialized)
            {
                wantsToFire = false;
                wantsToReload = false;
                return;
            }

            wantsToFire = m_AttackAction != null && m_AttackAction.IsPressed();
            wantsToReload = m_ReloadAction != null && m_ReloadAction.WasPressedThisFrame();
        }

        /// <summary>
        /// 读取本帧的切换武器输入。
        /// </summary>
        /// <returns>+1 表示切到下一把，-1 表示上一把，0 表示本帧没有切换。</returns>
        /// <remarks>
        /// 滚轮与数字键共用同一对动作：滚轮上滚与数字键 2 都是「下一个」。
        /// 用同一对动作而不是各建一套，是为了让「切武器」只有一条输入通路。
        /// </remarks>
        public int ReadWeaponSwitch()
        {
            Initialize();

            if (UseScriptedInput)
            {
                return ScriptedWeaponSwitch;
            }

            if (!m_IsInitialized)
            {
                return 0;
            }

            if (m_NextWeaponAction != null && m_NextWeaponAction.WasPressedThisFrame())
            {
                return 1;
            }

            if (m_PreviousWeaponAction != null && m_PreviousWeaponAction.WasPressedThisFrame())
            {
                return -1;
            }

            return 0;
        }

        /// <summary>
        /// 读取本帧的交互输入。
        /// </summary>
        /// <returns>本帧是否按下了交互键（默认 E）。</returns>
        /// <remarks>
        /// 返回「按下的那一帧」而不是「是否按住」：搜刮读条由逻辑层的
        /// <c>LootSearchInteraction</c> 累计，输入层只负责报告一次意图。
        /// 若这里返回持续状态，读条会被每帧重复的意图不断打断。
        /// </remarks>
        public bool ReadInteractIntent()
        {
            Initialize();

            if (UseScriptedInput)
            {
                return ScriptedWantsToInteract;
            }

            if (!m_IsInitialized)
            {
                return false;
            }

            return m_InteractAction != null && m_InteractAction.WasPressedThisFrame();
        }

        /// <summary>
        /// 读取「使用医疗品」的输入。
        /// </summary>
        /// <returns>本帧是否按下了使用键（默认 H）。</returns>
        /// <remarks>
        /// <para>这个键没有做成输入资产里的动作，而是直接读键盘：它只有一个绑定，
        /// 而新增动作需要改 <c>.inputactions</c> 资产并让整条输入链路重新解析一次，
        /// 收益与风险不成比例。等出现手柄需求时再补动作。</para>
        ///
        /// <para>返回「按下的那一帧」：读条由逻辑层累计，输入层只报告一次意图。
        /// 与交互键不同的是，这个键在**读条中再次按下表示取消**，
        /// 因此它不携带「是否正在读条」的判断——那是装配层的决定。</para>
        /// </remarks>
        public bool ReadUseMedicalIntent()
        {
            if (UseScriptedInput)
            {
                return ScriptedWantsToUseMedical;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.hKey.wasPressedThisFrame;
        }

        /// <summary>
        /// 读取本帧的暂停输入（默认 Esc）。
        /// </summary>
        /// <returns>本帧是否按下了暂停键。</returns>
        /// <remarks>
        /// 暂停键与医疗键一样直接读键盘：它只有一个绑定，不值得新增输入动作。
        /// 脚本化输入模式下返回 false，保持测试输入完全可控。
        /// </remarks>
        public bool ReadPauseIntent()
        {
            if (UseScriptedInput)
            {
                return false;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }
    }
}
