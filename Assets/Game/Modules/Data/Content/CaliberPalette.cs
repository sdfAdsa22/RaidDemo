using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 口径的展示配色与显示名。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么口径需要颜色：</b>"哪把枪用哪种子弹"在纯文字里要读一遍才能对上，
    /// 而每个口径固定一种颜色之后，武器格与弹药格上的小徽标一眼就能配对——
    /// 这正是负责人反馈的"看不出哪把枪用什么子弹"。</para>
    ///
    /// <para>颜色与稀有度色刻意错开：稀有度是白绿蓝紫金，口径取橙 / 蓝 / 红三色，
    /// 蓝与稀有度的蓝相比明显更深、更冷，避免"弹药徽标看起来像品质标记"。</para>
    /// </remarks>
    public static class CaliberPalette
    {
        /// <summary>5.45（AK-74 步枪弹）：橙。</summary>
        private const string Rifle545Hex = "#D9822B";

        /// <summary>9x19（手枪 / 冲锋枪弹）：蓝。</summary>
        private const string Pistol9x19Hex = "#2F6FD0";

        /// <summary>12 号霰弹：红。</summary>
        private const string Shotgun12GaHex = "#C0392B";

        /// <summary>未知口径的中性色。</summary>
        private const string UnknownHex = "#7C7568";

        /// <summary>取口径的代表色。未知口径回退到中性灰，避免出现刺眼的白块。</summary>
        /// <param name="caliberId">口径标识，例如 5.45 / 9x19 / 12ga。</param>
        public static Color GetColor(string caliberId)
        {
            var hex = GetHex(caliberId);
            return ColorUtility.TryParseHtmlString(hex, out var color)
                ? color
                : new Color(0.48f, 0.46f, 0.41f);
        }

        /// <summary>取口径色的十六进制字符串（给 TMP 富文本用，带 # 号）。</summary>
        public static string GetHex(string caliberId)
        {
            switch (caliberId)
            {
                case "5.45":
                    return Rifle545Hex;
                case "9x19":
                    return Pistol9x19Hex;
                case "12ga":
                    return Shotgun12GaHex;
                default:
                    return UnknownHex;
            }
        }

        /// <summary>
        /// 口径的显示名。
        /// </summary>
        /// <param name="caliberId">口径标识。</param>
        /// <remarks>内部标识保持程序友好的 "12ga"，界面上一律写"12 号"——
        /// 玩家不需要认识开发者的缩写。</remarks>
        public static string GetDisplayName(string caliberId)
        {
            switch (caliberId)
            {
                case "12ga":
                    return "12 号";
                case null:
                case "":
                    return "未知口径";
                default:
                    return caliberId;
            }
        }

        /// <summary>
        /// 从物品定义里取出它的口径标识。
        /// </summary>
        /// <param name="definition">物品定义，可为 null。</param>
        /// <returns>武器或弹药的口径；其它物品返回 null。</returns>
        /// <remarks>界面（物品格、商人货架）与提示文案都需要"这件物品吃什么口径"这一个问题的答案，
        /// 答案只写在这里一份，避免每个界面各抄一段判断。</remarks>
        public static string ResolveCaliber(RaidDemo.Data.IItemDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            var weapon = definition.WeaponStats;
            if (weapon != null)
            {
                return weapon.CaliberId;
            }

            var ammo = definition.AmmoStats;
            return ammo != null ? ammo.CaliberId : null;
        }
    }
}
