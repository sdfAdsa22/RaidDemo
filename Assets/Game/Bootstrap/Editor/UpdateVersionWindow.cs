using System;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 本体版本号设置窗口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>本体版本号（<c>PlayerSettings.bundleVersion</c>）是启动器比对的第一个依据，
    /// 也是清单目录名、更新源版本目录名的来源。它平时藏在 Project Settings 里，
    /// 发布时忘了改就会出现"新包覆盖旧版本目录"这类静默事故。
    /// 给它一个显式入口，是为了让"发版本"成为一个有仪式感的动作。</para>
    ///
    /// <para>只做格式提示（<c>主.次.修订</c>），不做强制校验：演示期可能需要
    /// <c>0.10.0-rc1</c> 之类的后缀，工具不应挡路。</para>
    /// </remarks>
    public sealed class UpdateVersionWindow : EditorWindow
    {
        /// <summary>输入框内容。</summary>
        private string m_Version = string.Empty;

        /// <summary>
        /// 打开窗口。
        /// </summary>
        [MenuItem("RaidDemo/M10/版本/设置本体版本…", priority = 120)]
        public static void Open()
        {
            var window = GetWindow<UpdateVersionWindow>(utility: true, title: "本体版本", focus: true);
            window.m_Version = PlayerSettings.bundleVersion;
            window.minSize = new Vector2(320f, 120f);
            window.Show();
        }

        /// <summary>绘制窗口。</summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("当前版本", PlayerSettings.bundleVersion);

            EditorGUILayout.Space(4f);
            m_Version = EditorGUILayout.TextField("新版本号", m_Version);

            var looksValid = LooksLikeSemanticVersion(m_Version);
            if (!looksValid)
            {
                EditorGUILayout.HelpBox("建议使用 主.次.修订 形式（例：0.10.0）。", MessageType.Info);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(m_Version)))
            {
                if (GUILayout.Button("写入 PlayerSettings"))
                {
                    PlayerSettings.bundleVersion = m_Version.Trim();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[M10 版本] 本体版本已更新为 {PlayerSettings.bundleVersion}");
                    Close();
                }
            }
        }

        /// <summary>粗略判断是否形如 <c>x.y.z</c>（允许后缀）。</summary>
        private static bool LooksLikeSemanticVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Trim().Split('.');
            if (parts.Length < 3)
            {
                return false;
            }

            foreach (var part in parts)
            {
                var digits = part;
                var dashIndex = digits.IndexOf('-');
                if (dashIndex >= 0)
                {
                    digits = digits.Substring(0, dashIndex);
                }

                if (digits.Length == 0)
                {
                    return false;
                }

                foreach (var character in digits)
                {
                    if (!char.IsDigit(character))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
