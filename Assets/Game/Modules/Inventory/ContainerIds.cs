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

        /// <summary>场景容器的起始编号。留出前面的区间给玩家侧容器。</summary>
        public const int SceneBase = 100;

        /// <summary>按生成顺序取第 index 个场景容器的编号。</summary>
        /// <param name="index">场景容器序号（从 0 开始）。</param>
        public static int SceneContainer(int index)
        {
            return SceneBase + (index < 0 ? 0 : index);
        }
    }
}
