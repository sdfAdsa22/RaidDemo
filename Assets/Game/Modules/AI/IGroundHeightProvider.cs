using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 把逻辑层的平面坐标翻译成"脚下的世界高度"的场景侧能力。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>AI 逻辑层只有平面坐标（必须能在无头服务端运行），
    /// 而装卸平台、坡道这些高差属于场景信息。感知与射击都需要"眼睛/枪口离地多高"，
    /// 若不补高度，站在平台上的单位会朝平台下方看、朝平台下方开枪。</para>
    ///
    /// <para><b>实现放在表现层</b>（导航网格采样），无头环境注入 null 时退化为 0，
    /// 与既有测试的假设保持一致。</para>
    /// </remarks>
    public interface IGroundHeightProvider
    {
        /// <summary>采样指定平面位置脚下的地面高度（米）。采样失败返回 0。</summary>
        float SampleHeight(Vector2F position);
    }
}
