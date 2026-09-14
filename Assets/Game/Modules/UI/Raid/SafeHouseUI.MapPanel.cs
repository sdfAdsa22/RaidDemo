using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 安全屋的出击地图面板：列表、门禁文案与键盘操作（P4.5-b）。
    /// </summary>
    /// <remarks>
    /// <para>从 <c>SafeHouseUI</c> 拆出来的原因：那个文件本来就承担"金币条 / 交互提示 / 短提示"
    /// 三件事，再加上联机门禁之后超过了 400 行上限；而"出击面板"恰好是可以整体搬走的一块——
    /// 它有自己的构建、自己的文案规则、自己的按键处理。</para>
    ///
    /// <para><b>面板的三处文案各说一件事：</b>右上角说门禁状态（为什么能 / 不能出发），
    /// 可用行的右侧说这一步要按什么键，底部一行说整块面板怎么退出。只有三处一致，
    /// 玩家才不会卡在"面板看着可用、按下去没反应"的困惑里。</para>
    /// </remarks>
    public sealed partial class SafeHouseUI
    {
        /// <summary>是否处于联机房间（决定地图面板走哪套门禁）。</summary>
        private bool IsRoomMode
        {
            get { return m_RoomBadgeRoot != null && m_RoomBadgeRoot.gameObject.activeSelf; }
        }

        /// <summary>当前是否允许从这个出口出发：单机恒为真，联机要求"房主 + 全员在屋"。</summary>
        private bool CanDeployFromRoom()
        {
            return !IsRoomMode || (m_RoomBadge.IsHost && !m_RoomBadge.RaidRunning);
        }

        /// <summary>
        /// 刷新地图面板上会随联机状态变化的文案。
        /// </summary>
        /// <remarks>
        /// 房间状态改变（有人进出、队友回屋）与面板开合时各调用一次，
        /// 不需要每帧刷新——它上面没有倒计时一类的连续量。
        /// </remarks>
        private void RefreshMapPanelTexts()
        {
            if (m_MapRuleLabel == null)
            {
                return;
            }

            if (!IsRoomMode)
            {
                m_MapRuleLabel.text = "1 可用 · 2 上锁";
                m_MapActionLabel.text = "按 1 出击";
                m_MapFootnoteLabel.text = "按 1 出击　·　按 Esc 返回";
                return;
            }

            var host = m_RoomBadge.IsHost;
            var waiting = m_RoomBadge.RaidRunning;

            m_MapRuleLabel.text = !host
                ? "共享安全屋 · 由房主选图"
                : (waiting ? "共享安全屋 · 等待队友回屋" : "共享安全屋 · 全员已回屋");

            m_MapActionLabel.text = !host ? "等待房主" : (waiting ? "等待队友" : "按 1 出击");

            m_MapFootnoteLabel.text = !host
                ? "只有房主可以出发　·　按 Esc 返回"
                : (waiting
                    ? "队友还在战局中，等他们回到安全屋　·　按 Esc 返回"
                    : "按 1 出发，全队一起进图　·　按 Esc 返回");
        }

        /// <summary>地图选择面板：1 个可用 + 2 个上锁。</summary>
        private void BuildMapPanel(RectTransform canvas)
        {
            // 遮罩与面板同一个根一起显隐：遮罩单独挂在画布上的话，
            // 关掉面板后世界会一直暗着（这批已经踩过一次，见排障记录）。
            var screen = UiFactory.CreateRect(canvas, "MapScreen");
            UiFactory.Stretch(screen);
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(screen, "MapPanel", MapPanelSize, UiSprites.Card);
            var width = MapPanelSize.x - 56f;

            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(MapPanelSize.x, 72f), UiSprites.CardDim, Vector2.zero);
            UiFactory.CreateLabel(
                panel, "选择出击地图", new Vector2(28f, 18f), new Vector2(360f, 36f),
                26f, TextAlignmentOptions.Left, UiPalette.Ink);
            m_MapRuleLabel = UiFactory.CreateLabel(
                panel, "1 可用 · 2 上锁", new Vector2(MapPanelSize.x - 328f, 26f), new Vector2(300f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);

            BuildMapRow(panel, 0, "1", "工业区与集装箱仓库", "可用 · 已解锁", true, 92f, width);
            BuildMapRow(panel, 1, "2", "港口", "未开放", false, 170f, width);
            BuildMapRow(panel, 2, "3", "农场", "未开放", false, 248f, width);

            m_MapFootnoteLabel = UiFactory.CreateLabel(
                panel, "按 1 出击　·　按 Esc 返回",
                new Vector2(28f, 344f), new Vector2(width, 30f),
                UiPalette.BodySize, TextAlignmentOptions.Center, UiPalette.InkSoft);

            m_MapScreen = screen.gameObject;
            m_MapScreen.SetActive(false);
        }

        /// <summary>地图列表的一行：按键胶囊 + 名称 + 状态（可用的那行右侧给出出击提示）。</summary>
        private void BuildMapRow(
            RectTransform panel,
            int index,
            string key,
            string name,
            string state,
            bool unlocked,
            float top,
            float width)
        {
            var row = UiFactory.CreatePanel(
                panel, $"MapRow{index}", new Vector2(width, 66f), UiSprites.CardDim, new Vector2(28f, top));

            var chip = UiFactory.CreateAnchored(
                row, "KeyChip", UiSprites.Chip,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(46f, 46f));
            UiFactory.CreateLabel(
                chip, key, Vector2.zero, new Vector2(46f, 46f),
                22f, TextAlignmentOptions.Center, unlocked ? UiPalette.Ink : UiPalette.InkDisabled);

            UiFactory.CreateLabel(
                row, name, new Vector2(72f, 12f), new Vector2(320f, 24f),
                20f, TextAlignmentOptions.Left, unlocked ? UiPalette.Ink : UiPalette.InkDisabled);
            UiFactory.CreateLabel(
                row, state, new Vector2(72f, 36f), new Vector2(320f, 20f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, unlocked ? UiPalette.Ok : UiPalette.InkDisabled);

            if (!unlocked)
            {
                return;
            }

            var action = UiFactory.CreateLabel(
                row, "按 1 出击", new Vector2(width - 228f, 22f), new Vector2(200f, 24f),
                17f, TextAlignmentOptions.Right, UiPalette.Teal);

            // 第一行（唯一可用的地图）的出击提示会随联机门禁变化，因此留一份引用。
            if (index == 0)
            {
                m_MapActionLabel = action;
            }
        }

        /// <summary>
        /// 地图面板的键盘操作：Esc 返回、1 出击、2/3 提示未开放。
        /// </summary>
        /// <remarks>
        /// 联机的门禁只在这里"提前拦一次并给一句人话"：真正的判定在服务器上
        /// （门禁不过时服务器会回一条明确的失败结果，由流程层显示）。
        /// </remarks>
        private void HandleMapPanelKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Cancel);
                HideMap();
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                if (!CanDeployFromRoom())
                {
                    UiAudio.Play(UiCue.Locked);
                    ShowHint(m_RoomBadge.IsHost
                        ? "队友还在战局中，等他们回到安全屋"
                        : "只有房主可以选择出击地图");
                    return;
                }

                UiAudio.Play(UiCue.Confirm);
                HideMap();
                m_OnDeploy?.Invoke();
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Locked);
                ShowHint("这张地图还没有开放");
            }
        }
    }
}
