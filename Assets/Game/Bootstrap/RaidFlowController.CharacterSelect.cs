using System;
using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的角色选择部分：打开界面、保存选择、恢复时间与光标。
    /// </summary>
    /// <remarks>
    /// <para>角色选择既可从主菜单进入，也可从安全屋的更衣镜进入；
    /// 两条路都必须冻结世界、释放鼠标，并在关闭时回到原来的时间/光标状态。</para>
    /// <para>界面只负责显示与回调，真正写存档、刷新场景角色的是这里。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        private PresentationCatalog m_CharacterCatalog;
        private CharacterSelectScreen m_CharacterScreen;
        private Action<string> m_CharacterSelectionChanged;
        private Action m_CharacterCloseCallback;
        private float m_TimeScaleBeforeCharacterSelect = 1f;
        private CursorLockMode m_CursorLockBeforeCharacterSelect;
        private bool m_CursorVisibleBeforeCharacterSelect;

        /// <summary>注入表现层目录；由场景启动层在拿到目录后调用。</summary>
        public void SetCharacterCatalog(PresentationCatalog catalog)
        {
            m_CharacterCatalog = catalog;
        }

        /// <summary>
        /// 打开角色选择界面。
        /// </summary>
        /// <param name="onSelected">用户切换角色后调用；可为空。</param>
        /// <param name="onClosed">界面关闭后调用；可为空。</param>
        public void ShowCharacterSelect(Action<string> onSelected = null, Action onClosed = null)
        {
            if (m_CharacterScreen != null && m_CharacterScreen.IsOpen)
            {
                return;
            }

            if (m_CharacterCatalog == null)
            {
                Debug.LogWarning("[RaidDemo] 表现层目录未注入，无法打开角色选择。");
                onClosed?.Invoke();
                return;
            }

            var options = BuildCharacterOptions();
            if (options.Count == 0)
            {
                Debug.LogWarning("[RaidDemo] 表现层目录里没有玩家角色，无法打开角色选择。");
                onClosed?.Invoke();
                return;
            }

            if (m_CharacterScreen == null)
            {
                var host = new GameObject("CharacterSelectScreen");
                host.transform.SetParent(transform, worldPositionStays: false);
                m_CharacterScreen = host.AddComponent<CharacterSelectScreen>();
            }

            m_CharacterSelectionChanged = onSelected;
            m_CharacterCloseCallback = onClosed;
            m_TimeScaleBeforeCharacterSelect = Time.timeScale;
            m_CursorLockBeforeCharacterSelect = Cursor.lockState;
            m_CursorVisibleBeforeCharacterSelect = Cursor.visible;

            Time.timeScale = 0f;
            UnlockCursor();

            m_CharacterScreen.Initialize(
                options,
                Progress.SelectedCharacterId,
                OnCharacterSelected,
                CloseCharacterSelect);
            m_CharacterScreen.SetVisible(true);
        }

        /// <summary>关闭角色选择并恢复打开前的时间与光标状态。</summary>
        public void CloseCharacterSelect()
        {
            if (m_CharacterScreen != null)
            {
                m_CharacterScreen.SetVisible(false);
            }

            Time.timeScale = m_TimeScaleBeforeCharacterSelect;
            Cursor.lockState = m_CursorLockBeforeCharacterSelect;
            Cursor.visible = m_CursorVisibleBeforeCharacterSelect;

            var closed = m_CharacterCloseCallback;
            m_CharacterCloseCallback = null;
            m_CharacterSelectionChanged = null;
            closed?.Invoke();
        }

        /// <summary>用户选中一个角色：校验、写存档、通知场景刷新。</summary>
        private void OnCharacterSelected(string id)
        {
            if (m_CharacterCatalog == null || m_CharacterCatalog.FindCharacterPrefab(id) == null)
            {
                return;
            }

            Progress.SetSelectedCharacter(id);
            SaveNow();
            m_CharacterSelectionChanged?.Invoke(id);
        }

        /// <summary>把目录里的角色转成 UI 层的数据结构。</summary>
        private List<CharacterSelectScreen.CharacterOption> BuildCharacterOptions()
        {
            var options = new List<CharacterSelectScreen.CharacterOption>();
            var entries = m_CharacterCatalog != null ? m_CharacterCatalog.PlayerCharacters : null;
            if (entries == null)
            {
                return options;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.Prefab == null)
                {
                    continue;
                }

                options.Add(new CharacterSelectScreen.CharacterOption(
                    entry.Id,
                    entry.DisplayName,
                    entry.Prefab));
            }

            return options;
        }
    }
}
