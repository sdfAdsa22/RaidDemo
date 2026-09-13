using RaidDemo.Shared;
using TMPro;
using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战斗信息面板的布局部分：底板与各行文字、换弹进度条的搭建。
    /// </summary>
    /// <remarks>从主文件拆出来的原因与其它界面一致：单文件 400 行上限。
    /// 这里只有"东西摆在哪"，数值的读取与提示逻辑都在主文件里。</remarks>
    public sealed partial class CombatHud
    {

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            // 高于准星（100），低于背包面板（200）：
            // 背包打开时面板会盖住屏幕中央，而弹药信息在左下角，两者不冲突。
            var canvas = UiFactory.CreateCanvas(transform, "CombatHudCanvas", 150);
            m_CanvasHost = canvas.gameObject;

            // 一块深色半透明底板托住全部信息：HUD 直接压在草地上时，
            // 浅色文字会与亮绿背景糊在一起；底板让它在任何背景上都读得清。
            var plateRect = UiFactory.CreateAnchored(
                canvas,
                "Plate",
                UiSprites.Card,
                anchor: new Vector2(0f, 0f),
                pivot: new Vector2(0f, 0f),
                offset: new Vector2(Margin, Margin),
                size: new Vector2(PanelWidth, PanelHeight));

            // 生命值放在最上方：它是玩家最先要看的数字，
            // 而弹匣数量在交火中反而是次要信息。
            m_HealthLabel = CreateLabel(plateRect, "生命 -- / --", 12f, 19f, TextColor);
            m_ArmorLabel = CreateLabel(plateRect, "甲 无  ｜  盔 无", 38f, 15f, DimTextColor);
            m_WeaponLabel = CreateLabel(plateRect, "无武器", 72f, 15f, DimTextColor);
            m_AmmoLabel = CreateLabel(plateRect, "-- / --", 92f, 34f, TextColor);
            m_ReserveLabel = CreateLabel(plateRect, "弹挂 0   背包 0", 132f, 14f, DimTextColor);
            m_HintLabel = CreateLabel(plateRect, string.Empty, 150f, 12f, DimTextColor);

            BuildReloadBar(plateRect);
        }

        /// <summary>创建换弹进度条。</summary>
        private void BuildReloadBar(RectTransform parent)
        {
            // 进度条贴在底板内部的最下沿：换弹时它出现，不换弹时整条隐藏，
            // 因此不会长期占着屏幕空间。
            m_ReloadBarRoot = UiFactory.CreateBar(
                parent,
                new Vector2(16f, PanelHeight - 22f),
                new Vector2(PanelWidth - 32f, BarHeight),
                BarFillColor,
                out m_ReloadBarFill);

            m_ReloadBarRoot.gameObject.SetActive(false);
        }

        /// <summary>在底板内创建一个左对齐的文本标签。</summary>
        /// <param name="parent">底板。</param>
        /// <param name="content">初始文字。</param>
        /// <param name="top">距底板顶部的像素。</param>
        /// <param name="fontSize">字号。</param>
        /// <param name="color">颜色。</param>
        private static TextMeshProUGUI CreateLabel(
            RectTransform parent,
            string content,
            float top,
            float fontSize,
            Color color)
        {
            return UiFactory.CreateLabel(
                parent,
                content,
                new Vector2(16f, top),
                new Vector2(PanelWidth - 32f, fontSize + 8f),
                fontSize,
                TextAlignmentOptions.Left,
                color);
        }
    }
}
