using TMPro;
using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 联机界面的构建与交互。
    /// </summary>
    /// <remarks>与公开 API 分开放：那条回答“装配层要调什么”，这条回答“界面怎么长”。</remarks>
    public sealed partial class MultiplayerMenuScreen
    {
        /// <summary>标题条。</summary>
        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel, "联机", new Vector2(Padding, 16f), new Vector2(320f, 48f),
                UiPalette.TitleSize, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                panel, "一台服务器 = 一个房间 · 2~4 人合作",
                new Vector2(Padding + 130f, 32f), new Vector2(460f, 30f),
                UiPalette.SubtitleSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }

        /// <summary>左栏：地址、昵称、口令与三个按钮。</summary>
        private void BuildLeftColumn(RectTransform panel)
        {
            var top = TitleBarHeight;

            UiFactory.CreateLabel(
                panel, "服务器地址", new Vector2(Padding, top + 16f), new Vector2(240f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_AddressInput = UiTextInput.Create(
                panel, "AddressInput", new Vector2(Padding, top + 42f), new Vector2(260f, 46f),
                AddressPlaceholder, false, LobbyText.MaxAddressLength);

            // 一键选云主机（预填了地址才显示）；地址框收窄到 260 仍然放得下「IP:端口」。
            m_CloudServerButton = UiFactory.CreateButton(panel, "云主机", new Vector2(Padding + 272f, top + 42f), new Vector2(88f, 46f));
            m_CloudServerButton.Rect.gameObject.SetActive(false);

            UiFactory.CreateLabel(
                panel, "端口", new Vector2(Padding + 376f, top + 16f), new Vector2(120f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_PortInput = UiTextInput.Create(
                panel, "PortInput", new Vector2(Padding + 376f, top + 42f), new Vector2(94f, 46f),
                "7777", false, LobbyText.MaxPortLength, "7777");

            UiFactory.CreateLabel(
                panel, "昵称（两个客户端必须不同名）", new Vector2(Padding, top + 106f), new Vector2(360f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_NicknameInput = UiTextInput.Create(
                panel, "NicknameInput", new Vector2(Padding, top + 132f), new Vector2(300f, 46f),
                "Player1234", false, LobbyText.MaxNicknameLength);
            m_RandomNicknameButton = UiFactory.CreateButton(
                panel, "随机", new Vector2(Padding + 312f, top + 132f), new Vector2(158f, 46f));

            UiFactory.CreateLabel(
                panel, "口令（4~6 位数字；第一次就是建号）", new Vector2(Padding, top + 196f), new Vector2(400f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_PassphraseInput = UiTextInput.Create(
                panel, "PassphraseInput", new Vector2(Padding, top + 222f), new Vector2(300f, 46f),
                // 明文显示（负责人 2026-09-14 决定）：这是单机演示与局域网合作，口令只用于区分账号，
                // 遮罩反而让"到底打进去了没有"变成每次都要猜的事（本批就因为遮罩字形缺失白查了一轮）。
                "例：1234", false, LobbyText.MaxPassphraseDigits);

            m_ConnectButton = UiFactory.CreateButton(
                panel, "连接并进入大厅", new Vector2(Padding, top + 290f), new Vector2(300f, 62f), UiButtonKind.Primary);
            m_ScanButton = UiFactory.CreateButton(
                panel, "扫描局域网", new Vector2(Padding + 312f, top + 290f), new Vector2(158f, 62f));
            m_BackButton = UiFactory.CreateButton(
                panel, "返回主菜单（Esc）", new Vector2(Padding, top + 364f), new Vector2(240f, 52f));

            UiFactory.CreateLabel(
                panel,
                "服务器由「-server -port 7777」启动。\n中文昵称可在启动参数里用 -nickname 指定（本批输入框只收 ASCII）。",
                new Vector2(Padding, top + 428f),
                new Vector2(LeftColumnWidth, 64f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkDisabled,
                wrap: true);
        }

        /// <summary>右栏：局域网房间列表。</summary>
        private void BuildRightColumn(RectTransform panel)
        {
            var top = TitleBarHeight;

            m_RoomListLabel = UiFactory.CreateLabel(
                panel, "局域网房间", new Vector2(RightColumnX, top + 16f), new Vector2(RightColumnWidth, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);

            for (var i = 0; i < m_RoomRows.Length; i++)
            {
                var row = UiFactory.CreateButton(
                    panel, string.Empty, new Vector2(RightColumnX, top + 46f + (i * 58f)), new Vector2(RightColumnWidth, 50f));
                row.Rect.gameObject.SetActive(false);
                m_RoomRows[i] = row;
            }

            UiFactory.CreateLabel(
                panel,
                "发现只用来省去手输地址：密码与人数仍由服务器在加入时判定。",
                new Vector2(RightColumnX, top + 400f),
                new Vector2(RightColumnWidth, 60f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkDisabled,
                wrap: true);
        }

        /// <summary>底部状态行。</summary>
        private void BuildStatusLine(RectTransform panel)
        {
            m_StatusLabel = UiFactory.CreateLabel(
                panel, string.Empty, new Vector2(Padding, PanelSize.y - 46f), new Vector2(PanelSize.x - (Padding * 2f), 30f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }
    }
}
