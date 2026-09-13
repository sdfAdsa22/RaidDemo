using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 稀有度的展示配色。
    /// </summary>
    /// <remarks>
    /// <para>颜色与档位的对应关系见设计文档 2.4 节。放在内容层而不是规则层，
    /// 是因为它是纯粹的表现决策：规则层不知道颜色，界面层也不需要自己维护一套色表，
    /// 两处引用同一个来源，改色只需要改这里。</para>
    ///
    /// <para>最高档用金色而不是红色。射击游戏里红色被敌人、危险、禁用占用了语义，
    /// 再拿它表示最值钱会抢占视觉通道。</para>
    /// </remarks>
    public static class RarityPalette
    {
        /// <summary>普通档（白）。</summary>
        public const string CommonHex = "#E8E8E8";

        /// <summary>精良档（绿）。</summary>
        public const string UncommonHex = "#4CCE5A";

        /// <summary>稀有档（蓝）。</summary>
        public const string RareHex = "#3E8BFF";

        /// <summary>史诗档（紫）。</summary>
        public const string EpicHex = "#A855F7";

        /// <summary>传说档（金）。</summary>
        public const string LegendaryHex = "#FFB300";

        /// <summary>
        /// 浅色背景上使用的稀有度变体（M7 批次 4）。
        /// </summary>
        /// <remarks>
        /// <para>上面那组颜色是为**深色背景**定的：在近黑面板上它们足够跳。
        /// 但 M7 批次 4 的界面改成奶油色纸面之后，同样的颜色会"发飘"——
        /// 白字在米白底上几乎看不见，金与黄也糊在一起。</para>
        /// <para>做法不是改掉原色，而是为浅底另配一组**同色相、压暗降饱和**的变体：
        /// 世界里的伤害数字、地图标记继续用原色，物品格与提示文字用这里的变体。
        /// 两套都从这个文件出，改色仍然只有一个地方。</para>
        /// </remarks>
        public const string CommonOnPaperHex = "#7C7568";

        /// <summary>精良档在浅底上的变体。</summary>
        public const string UncommonOnPaperHex = "#2F9A45";

        /// <summary>稀有档在浅底上的变体。</summary>
        public const string RareOnPaperHex = "#2F6FD0";

        /// <summary>史诗档在浅底上的变体。</summary>
        public const string EpicOnPaperHex = "#8B3FD6";

        /// <summary>传说档在浅底上的变体。</summary>
        public const string LegendaryOnPaperHex = "#C08A00";

        /// <summary>取指定档位的代表色。解析失败时回退到白色，避免界面出现全黑方块。</summary>
        public static Color GetColor(RarityTier tier)
        {
            var hex = GetHex(tier);
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.white;
        }

        /// <summary>
        /// 取浅色背景上使用的稀有度颜色。
        /// </summary>
        /// <param name="tier">稀有度档位。</param>
        /// <returns>压暗后的颜色；解析失败时回退到深色 Ink 灰，避免界面出现纯白方块。</returns>
        public static Color GetColorOnPaper(RarityTier tier)
        {
            var hex = GetHexOnPaper(tier);
            return ColorUtility.TryParseHtmlString(hex, out var color)
                ? color
                : new Color(0.48f, 0.46f, 0.41f);
        }

        /// <summary>取浅色背景上使用的稀有度色值字符串。</summary>
        public static string GetHexOnPaper(RarityTier tier)
        {
            switch (tier)
            {
                case RarityTier.Uncommon:
                    return UncommonOnPaperHex;
                case RarityTier.Rare:
                    return RareOnPaperHex;
                case RarityTier.Epic:
                    return EpicOnPaperHex;
                case RarityTier.Legendary:
                    return LegendaryOnPaperHex;
                default:
                    return CommonOnPaperHex;
            }
        }

        /// <summary>取指定档位的十六进制色值字符串。</summary>
        public static string GetHex(RarityTier tier)
        {
            switch (tier)
            {
                case RarityTier.Uncommon:
                    return UncommonHex;
                case RarityTier.Rare:
                    return RareHex;
                case RarityTier.Epic:
                    return EpicHex;
                case RarityTier.Legendary:
                    return LegendaryHex;
                default:
                    return CommonHex;
            }
        }

        /// <summary>取指定档位的中文名称，用于提示框与日志。</summary>
        public static string GetDisplayName(RarityTier tier)
        {
            switch (tier)
            {
                case RarityTier.Uncommon:
                    return "精良";
                case RarityTier.Rare:
                    return "稀有";
                case RarityTier.Epic:
                    return "史诗";
                case RarityTier.Legendary:
                    return "传说";
                default:
                    return "普通";
            }
        }
    }
}
