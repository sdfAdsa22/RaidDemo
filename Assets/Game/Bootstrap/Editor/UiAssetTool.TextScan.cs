using System.Collections.Generic;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 字体字符收集器的文本扫描部分。
    /// </summary>
    /// <remarks>
    /// 从 <c>UiAssetTool.Characters.cs</c> 拆出来是为了守住单文件 400 行上限；
    /// 这里只处理 YAML 里的 <c>\uXXXX</c> 转义与“把文本里可能显示的字符加入集合”。
    /// </remarks>
    public static partial class UiAssetTool
    {
        /// <summary>把文本里所有“可能显示”的字符加入集合。</summary>
        private static void CollectAllUiCharacters(string text, HashSet<char> characters)
        {
            for (var i = 0; i < text.Length; i++)
            {
                // Unity 的 .unity / .asset YAML 会把中文写成 \uXXXX 转义；
                // 只按字面扫描会把“衣柜”这类场景内文案漏掉，正是 U-63 的根因。
                if (TryReadUnicodeEscape(text, i, out var escaped))
                {
                    AddIfUiCharacter(characters, escaped);
                    i += 5;
                    continue;
                }

                AddIfUiCharacter(characters, text[i]);
            }
        }

        /// <summary>尝试读取 \uXXXX 形式的 Unicode 转义。</summary>
        private static bool TryReadUnicodeEscape(string text, int index, out char value)
        {
            value = default;
            if (index + 5 >= text.Length || text[index] != '\\' || text[index + 1] != 'u')
            {
                return false;
            }

            var code = 0;
            for (var i = 0; i < 4; i++)
            {
                code <<= 4;
                var c = text[index + 2 + i];
                if (c >= '0' && c <= '9')
                {
                    code += c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    code += c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    code += c - 'A' + 10;
                }
                else
                {
                    return false;
                }
            }

            value = (char)code;
            return true;
        }
    }
}
