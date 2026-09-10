using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 运行时界面字体提供者。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用旧版 Text 而不是 TextMeshPro：</b>本项目尚未导入 TMP 的基础资源包，
    /// 工程里没有任何 TMP 字体资产；而界面需要显示中文，TMP 的默认字体不含中日韩字形，
    /// 仍然需要额外制作一份中文字体资产。在 M2 的灰盒阶段，这些都属于美术工作，
    /// 排在系统之后（见项目决策 D-01）。等 M7 替换美术时再统一换成 TMP 与正式字体资源。</para>
    ///
    /// <para>字体按"从操作系统中取"的方式获得，而不是把字体文件放进工程：
    /// 本项目只发布 Windows 版（决策 D-15），系统的微软雅黑一定存在，
    /// 这样既不引入字体授权问题，也不用往仓库里塞几十兆的字体文件。</para>
    /// </remarks>
    public static class UiFontProvider
    {
        /// <summary>按优先级尝试的系统字体名。</summary>
        private static readonly string[] s_Candidates = { "Microsoft YaHei", "SimHei", "SimSun", "Arial" };

        /// <summary>缓存下来的字体对象。动态字体只需创建一次。</summary>
        private static Font s_Font;

        /// <summary>
        /// 取一个可用的界面字体。
        /// </summary>
        /// <param name="size">字号（像素）。</param>
        /// <returns>字体对象。系统字体不可用时退回引擎内置字体。</returns>
        public static Font Get(int size = 14)
        {
            if (s_Font != null)
            {
                return s_Font;
            }

            s_Font = Font.CreateDynamicFontFromOSFont(s_Candidates, size);
            if (s_Font == null)
            {
                // 内置字体不含中文字形，只能保证数字与英文可读。
                // 走到这里说明运行环境缺少常见中文字体，属于异常情况，但不应该让界面直接崩掉。
                s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return s_Font;
        }
    }
}
