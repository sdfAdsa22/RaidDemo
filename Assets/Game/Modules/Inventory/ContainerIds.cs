namespace RaidDemo.Inventory
{
    /// <summary>
    /// 容器编号的约定：哪些编号固定属于谁，场景容器从哪个编号开始。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要一份约定：</b>所有背包命令都只带一个整数容器编号
    /// （"把 3 号容器 (2,1) 的东西移到 7 号容器 (0,0)"）。单机时编号由注册顺序决定就够了，
    /// 因为只有一个进程；联机时**两端必须对同一个编号给出同一个箱子**，
    /// 否则命令会成功执行——只是执行到了别的容器上，而现象是"东西莫名其妙进了别的箱子"，
    /// 极难定位。</para>
    ///
    /// <para><b>分段规则：</b></para>
    /// <list type="bullet">
    /// <item><description><b>1~99：玩家侧容器</b>（背包 / 弹药挂 / 仓库），由下面的常量固定；
    /// 它们与"谁在玩"绑定，两端各自建立但编号一致。</description></item>
    /// <item><description><b>100 起：场景容器</b>（地图上的箱子），编号 = 起始值 + 生成顺序。
    /// 顺序来自场景里的生成点数组，两端读的是同一张地图，因此编号天然一致。</description></item>
    /// </list>
    ///
    /// <para>新增容器类型时**只在这里加常量**，不要在装配处写字面量。</para>
    /// </remarks>
    public static class ContainerIds
    {
        /// <summary>角色的随身背包。</summary>
        public const int PlayerBackpack = 1;

        /// <summary>弹药挂。换弹只从这里取弹。</summary>
        public const int AmmoPouch = 2;

        /// <summary>局外仓库。</summary>
        public const int Stash = 3;

        /// <summary>
        /// 装备槽镜像的传输编号。
        /// </summary>
        /// <remarks>
        /// <para><b>它为什么存在：</b>装备槽（<c>EquipmentLoadout</c>）不是网格容器，
        /// 原本没有任何下行通道——服务器配发给玩家的武器、玩家在局外换上的枪，
        /// 客户端一概看不见。表现是"进图后 HUD 显示无武器、按开火没有反应"，
        /// 而服务器日志里一切正常（2026-09-14 联机基础问题修复的定位结论）。</para>
        ///
        /// <para><b>怎么用：</b>服务器把它按"1×N 的镜像网格"（列固定 0，行号即槽位序号）
        /// 塞进既有的容器内容批次，复用同一条下行与重发机制，不新增协议；
        /// 客户端在容器批次处理里把它单独挑出来应用，<b>不注册进容器注册表</b>，
        /// 因为它不参与任何命令——装备变更仍然走装备命令（服务器权威）。</para>
        ///
        /// <para><b>为什么是 4：</b>1~99 段是玩家侧容器编号，1/2/3 已分别为
        /// 背包 / 弹药挂 / 仓库，4 是段内第一个空位。</para>
        /// </remarks>
        public const int EquipmentMirror = 4;

        /// <summary>
        /// 服务器侧的**房间共享仓库**容器编号（P5）。
        /// </summary>
        /// <remarks>
        /// <para><b>它为什么存在：</b>联机时仓库是房间级的——同一间安全屋里的所有玩家看的是同一份。
        /// 客户端说的"3 号容器"在服务器上必须落到**同一个**仓库，而不是"每人一份"。
        /// 因此服务器用这个固定编号注册共享仓库，收到 3 号容器的命令时翻译成它
        /// （见 <c>ServerRuntime.Inventory.Commands.TranslateContainerId</c>）。</para>
        ///
        /// <para><b>为什么是 5：</b>1~99 段是"客户端认识的编号"，1/2/3/4 已分别为
        /// 背包 / 弹药挂 / 仓库 / 装备镜像；服务器侧的容器用段内空位，且**必须避开
        /// 100~999 的场景容器段**——那一段会在切图时被整段注销（见 `UnregisterSceneContainers`），
        /// 共享仓库显然不该跟着地图一起消失。</para>
        /// </remarks>
        public const int ServerSharedStash = 5;

        /// <summary>场景容器的起始编号。留出前面的区间给玩家侧容器。</summary>
        public const int SceneBase = 100;

        /// <summary>按生成顺序取第 index 个场景容器的编号。</summary>
        /// <param name="index">场景容器序号（从 0 开始）。</param>
        public static int SceneContainer(int index)
        {
            return SceneBase + (index < 0 ? 0 : index);
        }

        /// <summary>
        /// 服务器侧"某名玩家自己的容器"的编号起点。
        /// </summary>
        /// <remarks>
        /// <para>客户端说"1 号容器"时，指的永远是**它自己的背包**（见上面的常量）。
        /// 但服务器上同时存在多名玩家的背包，一个注册表放不下两个"1 号"。
        /// 因此服务器给每名玩家的容器分配一段独占编号，命令进来时先做一次翻译：
        /// 客户端编号 &lt; <see cref="SceneBase"/> 的，翻译成"这名玩家自己的那一个"。</para>
        /// </remarks>
        public const int ServerPlayerBase = 1000;

        /// <summary>每名玩家占用的编号个数（背包 / 弹药挂 / 仓库各一个）。</summary>
        private const int SlotsPerPlayer = 10;

        /// <summary>玩家容器的槽位。</summary>
        public enum PlayerSlot
        {
            /// <summary>随身背包。</summary>
            Backpack = 0,

            /// <summary>弹药挂。</summary>
            AmmoPouch = 1,

            /// <summary>仓库。</summary>
            Stash = 2,
        }

        /// <summary>取某名玩家某个槽位在**服务器注册表**里的编号。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="slot">槽位。</param>
        public static int ServerPlayerContainer(int playerId, PlayerSlot slot)
        {
            var index = playerId < 0 ? 0 : playerId;
            return ServerPlayerBase + (index * SlotsPerPlayer) + (int)slot;
        }
    }
}
