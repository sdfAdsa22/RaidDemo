using System.Collections.Generic;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 玩家角色构建器的角色清单。
    /// </summary>
    /// <remarks>
    /// <para>12 个 Kenney Mini Characters 角色共用同一套骨架与动画控制器；
    /// 这里只声明“有哪 12 个角色、模型在哪、预制体存到哪”，真正的构建逻辑仍在主文件。</para>
    /// <para>id 使用 <c>female-a</c> / <c>male-a</c> 这类稳定字符串：存档只写 id，
    /// 将来换显示名或换模型不会让旧存档失效。</para>
    /// </remarks>
    public static partial class PlayerCharacterBuilder
    {
        /// <summary>Kenney Mini Characters 正式素材目录。</summary>
        private const string CharacterFolder =
            "Assets/Game/Content/External/Kenney/MiniCharacters";

        /// <summary>一个可选角色的静态描述。</summary>
        public readonly struct PlayerCharacterOption
        {
            /// <summary>创建一条角色描述。</summary>
            public PlayerCharacterOption(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
                ModelPath = $"{CharacterFolder}/character-{id}.fbx";

                // male-a 继续使用旧的 PlayerCharacter.prefab 路径：
                // 安全屋与战局场景已经按这个 GUID 引用默认角色，换路径会让旧引用断掉。
                PrefabPath = id == "male-a"
                    ? $"{ArtFolder}/PlayerCharacter.prefab"
                    : $"{ArtFolder}/PlayerCharacter_{id.Replace('-', '_')}.prefab";
            }

            /// <summary>稳定 id，例如 <c>female-a</c>。</summary>
            public string Id { get; }

            /// <summary>界面显示名。</summary>
            public string DisplayName { get; }

            /// <summary>FBX 模型路径。</summary>
            public string ModelPath { get; }

            /// <summary>生成的预制体路径。</summary>
            public string PrefabPath { get; }
        }

        private static readonly PlayerCharacterOption[] s_CharacterOptions =
        {
            new PlayerCharacterOption("female-a", "女队员 A"),
            new PlayerCharacterOption("female-b", "女队员 B"),
            new PlayerCharacterOption("female-c", "女队员 C"),
            new PlayerCharacterOption("female-d", "女队员 D"),
            new PlayerCharacterOption("female-e", "女队员 E"),
            new PlayerCharacterOption("female-f", "女队员 F"),
            new PlayerCharacterOption("male-a", "男队员 A"),
            new PlayerCharacterOption("male-b", "男队员 B"),
            new PlayerCharacterOption("male-c", "男队员 C"),
            new PlayerCharacterOption("male-d", "男队员 D"),
            new PlayerCharacterOption("male-e", "男队员 E"),
            new PlayerCharacterOption("male-f", "男队员 F"),
        };

        /// <summary>全部可选角色。</summary>
        public static IReadOnlyList<PlayerCharacterOption> CharacterOptions
        {
            get { return s_CharacterOptions; }
        }
    }
}
