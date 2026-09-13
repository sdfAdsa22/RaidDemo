using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 中文字体资产的字符集收集：只收集界面真正会显示的字符。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要预烘焙字符集：</b>TMP 的动态字体资产在图集满时才尝试新建图集页；
    /// 本工程踩过一次“扩页失败、字符表有记录但字形表没有 glyph”的缺字问题。
    /// 做法改为在编辑器构建字体资产时扫描项目内的文案，把用到的字符一次性写进图集，
    /// 运行时只做命中查询，不再依赖扩页。</para>
    /// <para>扫描 C# 时只取字符串/字符字面量，刻意跳过注释：注释里的汉字不会出现在界面上，
    /// 如果也收进来，图集页数会为了几百个永远不会显示的字白白膨胀。</para>
    /// </remarks>
    public static partial class UiAssetTool
    {
        /// <summary>会直接产生玩家可见文案的源码目录。</summary>
        private static readonly string[] CodeScanFolders =
        {
            "Assets/Game/Modules/UI",
            "Assets/Game/Modules/Meta",
            "Assets/Game/Modules/Data/Content",
        };

        /// <summary>包含物品/角色显示名的编辑器构建器（只取这两个文件，避免把工具报错文案也烘进图集）。</summary>
        private static readonly string[] CodeScanFiles =
        {
            "Assets/Game/Bootstrap/Editor/ItemContentBuilder.cs",
            "Assets/Game/Bootstrap/Editor/PlayerCharacterBuilder.Characters.cs",
        };

        /// <summary>顶层 Bootstrap 文件里有少量玩家可见提示（例如强退惩罚）。</summary>
        private const string BootstrapFolder = "Assets/Game/Bootstrap";

        /// <summary>需要额外扫描的 YAML 内容目录。</summary>
        private static readonly string[] ContentScanFolders =
        {
            "Assets/Game/Content/Items",
            "Assets/Game/Content/Presentation",
        };

        /// <summary>需要额外扫描的场景文件。</summary>
        private static readonly string[] SceneScanFiles =
        {
            "Assets/Game/Content/Scenes/SafeHouse.unity",
            "Assets/Game/Content/Scenes/GreyboxRaid.unity",
        };

        /// <summary>
        /// 收集全部界面会显示的中文字符。
        /// </summary>
        /// <returns>按 Unicode 排序后的字符集，可直接喂给 TMP 的 TryAddCharacters。</returns>
        public static string CollectUiCharacters()
        {
            var characters = new HashSet<char>();
            AddAsciiAndCommonSymbols(characters);
            for (var i = 0; i < CodeScanFolders.Length; i++)
            {
                CollectFromCodeFolder(characters, CodeScanFolders[i]);
            }

            for (var i = 0; i < CodeScanFiles.Length; i++)
            {
                CollectFromCodeFile(characters, CodeScanFiles[i]);
            }

            CollectFromCodeFolder(characters, BootstrapFolder, SearchOption.TopDirectoryOnly);

            for (var i = 0; i < ContentScanFolders.Length; i++)
            {
                CollectFromAssetFolder(characters, ContentScanFolders[i]);
            }

            for (var i = 0; i < SceneScanFiles.Length; i++)
            {
                CollectFromTextFile(characters, SceneScanFiles[i]);
            }

            var ordered = new List<char>(characters);
            ordered.Sort();
            return new string(ordered.ToArray());
        }

        /// <summary>把缺失字符转成便于排查的短文本。</summary>
        private static string DescribeCharacters(string characters)
        {
            if (string.IsNullOrEmpty(characters))
            {
                return "无";
            }

            var builder = new StringBuilder();
            var count = Mathf.Min(characters.Length, 40);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(characters[i]).Append("(U+")
                    .Append(((int)characters[i]).ToString("X4")).Append(')');
            }

            if (characters.Length > count)
            {
                builder.Append(" …");
            }

            return builder.ToString();
        }

        /// <summary>加入 ASCII 与界面常用的中文标点/符号。</summary>
        private static void AddAsciiAndCommonSymbols(HashSet<char> characters)
        {
            for (var c = 32; c <= 126; c++)
            {
                characters.Add((char)c);
            }

            var symbols = "…—·✓×÷±｜＋－　【】「」『』“”‘’";
            for (var i = 0; i < symbols.Length; i++)
            {
                characters.Add(symbols[i]);
            }
        }

        /// <summary>扫描一个源码目录下的 C# 字符串与字符字面量。</summary>
        private static void CollectFromCodeFolder(
            HashSet<char> characters,
            string folder,
            SearchOption searchOption = SearchOption.AllDirectories)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var files = Directory.GetFiles(folder, "*.cs", searchOption);
            for (var i = 0; i < files.Length; i++)
            {
                CollectStringLiterals(File.ReadAllText(files[i]), characters);
            }
        }

        /// <summary>扫描单个 C# 文件。</summary>
        private static void CollectFromCodeFile(HashSet<char> characters, string path)
        {
            if (File.Exists(path))
            {
                CollectStringLiterals(File.ReadAllText(path), characters);
            }
        }

        /// <summary>扫描一个目录下的小型 YAML 内容资产。</summary>
        private static void CollectFromAssetFolder(HashSet<char> characters, string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var files = Directory.GetFiles(folder, "*.asset", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                CollectAllUiCharacters(File.ReadAllText(files[i]), characters);
            }
        }

        /// <summary>扫描单个文本文件（场景 YAML）里的界面字符。</summary>
        private static void CollectFromTextFile(HashSet<char> characters, string path)
        {
            if (File.Exists(path))
            {
                CollectAllUiCharacters(File.ReadAllText(path), characters);
            }
        }

        /// <summary>把文本里所有“可能显示”的字符加入集合。</summary>
        private static void CollectAllUiCharacters(string text, HashSet<char> characters)
        {
            for (var i = 0; i < text.Length; i++)
            {
                AddIfUiCharacter(characters, text[i]);
            }
        }

        /// <summary>
        /// 从 C# 源码里提取字符串/字符字面量。
        /// </summary>
        /// <remarks>
        /// 这是一个小型的词法扫描器：跳过 // 与 /* */ 注释，只处理普通字符串、逐字字符串、
        /// 插值字符串与字符字面量。它不追求完整 C# 语法解析，只服务于“收集界面文案”这一个目标。
        /// </remarks>
        private static void CollectStringLiterals(string text, HashSet<char> characters)
        {
            var i = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        i++;
                    }

                    i = Mathf.Min(i + 2, text.Length);
                    continue;
                }

                if (TryEnterVerbatimString(text, ref i))
                {
                    while (i < text.Length)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        AddIfUiCharacter(characters, text[i]);
                        i++;
                    }

                    continue;
                }

                if (TryEnterNormalString(text, ref i))
                {
                    CollectNormalString(text, ref i, characters);
                    continue;
                }

                if (text[i] == '\'')
                {
                    CollectCharLiteral(text, ref i, characters);
                    continue;
                }

                i++;
            }
        }

        /// <summary>尝试进入 @" 或 @$" 逐字字符串；成功时把 i 移到内容起点。</summary>
        private static bool TryEnterVerbatimString(string text, ref int i)
        {
            if (text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2;
                return true;
            }

            if (text[i] == '@' && i + 2 < text.Length && text[i + 1] == '$' && text[i + 2] == '"')
            {
                i += 3;
                return true;
            }

            if (text[i] == '$' && i + 2 < text.Length && text[i + 1] == '@' && text[i + 2] == '"')
            {
                i += 3;
                return true;
            }

            return false;
        }

        /// <summary>尝试进入普通字符串或 $" 插值字符串；成功时把 i 移到内容起点。</summary>
        private static bool TryEnterNormalString(string text, ref int i)
        {
            if (text[i] == '"')
            {
                i++;
                return true;
            }

            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2;
                return true;
            }

            return false;
        }

        /// <summary>读取普通字符串，处理反斜杠转义。</summary>
        private static void CollectNormalString(string text, ref int i, HashSet<char> characters)
        {
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    break;
                }

                AddIfUiCharacter(characters, c);
                i++;
            }
        }

        /// <summary>读取字符字面量。</summary>
        private static void CollectCharLiteral(string text, ref int i, HashSet<char> characters)
        {
            i++;
            if (i >= text.Length)
            {
                return;
            }

            if (text[i] == '\\')
            {
                i += 2;
            }
            else
            {
                AddIfUiCharacter(characters, text[i]);
                i++;
            }

            while (i < text.Length && text[i] != '\'')
            {
                i++;
            }

            if (i < text.Length)
            {
                i++;
            }
        }

        /// <summary>只收入会出现在界面上的字符范围。</summary>
        private static void AddIfUiCharacter(HashSet<char> characters, char c)
        {
            if (c >= 32 && c <= 126)
            {
                characters.Add(c);
                return;
            }

            if ((c >= 0x4E00 && c <= 0x9FFF) ||
                (c >= 0x3000 && c <= 0x303F) ||
                (c >= 0xFF00 && c <= 0xFFEF) ||
                (c >= 0x2000 && c <= 0x206F) ||
                c == '✓' || c == '×' || c == '÷' || c == '±')
            {
                characters.Add(c);
            }
        }
    }
}
