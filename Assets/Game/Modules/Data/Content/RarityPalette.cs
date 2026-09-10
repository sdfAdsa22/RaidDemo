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

        /// <summary>取指定档位的代表色。解析失败时回退到白色，避免界面出现全黑方块。</summary>
        public static Color GetColor(RarityTier tier)
        {
            var hex = GetHex(tier);
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.white;
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
