using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 角色动画"循环开关"的统一读写入口（编辑器专用，只在构建角色资产时使用）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要单独一个工具：</b>"这个动作播完是重播还是停住"写在模型 FBX 的导入设置里
    /// （落在 .meta 的动画剪辑设置中），动画控制器管不着。控制器只决定什么时候切进哪个状态；
    /// 进了状态之后，由剪辑自己决定播完是重播还是定格在最后一帧。</para>
    ///
    /// <para><b>漏勾 Loop Time 的表现：</b>Idle / Walk / Run 这类状态绑了不循环的剪辑，角色会
    /// "先正常走一小段，然后保持最后一帧的姿势在地面上平移"。它不报错、不崩溃、不写日志，
    /// 只能靠肉眼在游戏里发现——M7 批次 1 的士兵就是这么挂的：模型没有 Walk 剪辑，
    /// 走路状态按兜底链取了 Run_Gun，而 Run_Gun 当时不在循环名单里。</para>
    ///
    /// <para><b>两种用法分工：</b><see cref="ApplyLoopTable"/> 按名字表把整套剪辑校准一遍
    /// （构建资产的第一步，代价是一次 FBX 重新导入）；<see cref="EnsureLooping"/> 按"用途"兜底，
    /// 在把某个剪辑绑到循环状态之前再确认一次——它不依赖名字表写得全不全，
    /// 因此将来换模型、换剪辑命名也不会重犯同一个缺陷。</para>
    /// </remarks>
    public static class CharacterAnimationLoopTool
    {
        /// <summary>模型自带的预览剪辑前缀，导入设置的遍历需要跳过它们。</summary>
        private const string PreviewPrefix = "__preview__";

        /// <summary>该剪辑当前是否勾了 Loop Time。空剪辑按"不可用"处理。</summary>
        public static bool IsLooping(AnimationClip clip)
        {
            return clip != null && AnimationUtility.GetAnimationClipSettings(clip).loopTime;
        }

        /// <summary>
        /// 按名字表校准整个模型的循环设置，返回是否真的改动了导入设置。
        /// </summary>
        /// <param name="modelPath">模型资产路径（.fbx）。</param>
        /// <param name="loopingNames">
        /// 需要循环的剪辑短名：形如 <c>CharacterArmature|Walk</c> 的剪辑取 <c>Walk</c>。
        /// </param>
        /// <returns>导入设置发生变化时为 <c>true</c>（此时已重新导入模型）。</returns>
        /// <remarks>
        /// 表里没有的剪辑会被显式关掉循环。这一点很重要：一次性动作（射击、倒地、受击）
        /// 必须保持不循环，否则它们会一直重播，看起来像角色在抽搐。
        /// </remarks>
        public static bool ApplyLoopTable(string modelPath, ICollection<string> loopingNames)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null || loopingNames == null)
            {
                return false;
            }

            var clips = ReadClips(importer);
            var changed = false;
            foreach (var clip in clips)
            {
                if (clip.name.StartsWith(PreviewPrefix))
                {
                    continue;
                }

                var shouldLoop = loopingNames.Contains(ShortName(clip.name));
                if (clip.loopTime == shouldLoop)
                {
                    continue;
                }

                clip.loopTime = shouldLoop;
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            Save(importer, clips);
            return true;
        }

        /// <summary>
        /// 按"用途"保证某个剪辑循环，返回重新导入后可以继续绑定的剪辑对象。
        /// </summary>
        /// <param name="modelPath">模型资产路径（.fbx）。</param>
        /// <param name="clip">当前准备绑到循环状态上的剪辑。</param>
        /// <returns>
        /// 可继续绑定的剪辑；返回 <c>null</c> 表示导入设置写不进去，
        /// 调用方应当把它当成构建失败（例如该剪辑不在模型的导入设置里）。
        /// </returns>
        /// <remarks>
        /// 重新导入会让原来的剪辑对象失效，因此这里返回的是重新取到的新对象。
        /// 调用方必须用返回值替换自己手里的引用，否则动画控制器会指向一个已经过期的对象。
        /// </remarks>
        public static AnimationClip EnsureLooping(string modelPath, AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            if (IsLooping(clip))
            {
                return clip;
            }

            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                return null;
            }

            var clips = ReadClips(importer);
            var changed = false;
            foreach (var entry in clips)
            {
                if (entry.name != clip.name || entry.loopTime)
                {
                    continue;
                }

                entry.loopTime = true;
                changed = true;
            }

            if (!changed)
            {
                return null;
            }

            Save(importer, clips);
            return FindClip(modelPath, clip.name);
        }

        /// <summary>按资产路径与剪辑全名取剪辑。重新导入之后，只能靠这个方式拿回可用的对象。</summary>
        /// <param name="modelPath">模型资产路径（.fbx）。</param>
        /// <param name="clipName">剪辑全名（如 <c>CharacterArmature|Walk</c>）。</param>
        /// <returns>找到的剪辑；找不到时为 <c>null</c>。</returns>
        public static AnimationClip FindClip(string modelPath, string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>读取导入设置里的剪辑列表；模型从未被显式配置过时退回默认列表。</summary>
        private static ModelImporterClipAnimation[] ReadClips(ModelImporter importer)
        {
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            return clips;
        }

        /// <summary>把剪辑设置写回导入器并重新导入模型，让改动真正落到剪辑资产上。</summary>
        private static void Save(ModelImporter importer, ModelImporterClipAnimation[] clips)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>取形如 <c>CharacterArmature|Walk</c> 的短名（<c>Walk</c>）。</summary>
        private static string ShortName(string clipName)
        {
            var separator = clipName.LastIndexOf('|');
            return separator >= 0 ? clipName.Substring(separator + 1) : clipName;
        }
    }
}
