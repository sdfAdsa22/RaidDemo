using System.Collections.Generic;
using RaidDemo.Data;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 物品资产生成器的战斗参数部分。
    /// </summary>
    /// <remarks>
    /// <para>拆成 partial 文件的原因只有一个：单文件超过了项目规定的 400 行上限。
    /// 按职责切分之后，这里放的是"武器、弹药、护甲各自有哪些数值"，
    /// 主文件放的是"游戏里有哪些物品、资产怎么创建"。</para>
    /// <para>两张表分开还有一个实际好处：调射击手感时只需要看这一个文件，
    /// 不必在整张物品清单里翻找。</para>
    /// </remarks>
    public static partial class ItemContentBuilder
    {
        /// <summary>武器战斗参数的生成数据。</summary>
        private readonly struct WeaponSpec
        {
            public WeaponSpec(
                string caliberId,
                float damage,
                float roundsPerMinute,
                WeaponFireMode fireMode,
                int burstCount,
                int magazineCapacity,
                float reloadSeconds,
                float baseSpread,
                float spreadPerShot,
                float maxSpread,
                float spreadRecovery,
                float rangeMeters)
            {
                CaliberId = caliberId;
                Damage = damage;
                RoundsPerMinute = roundsPerMinute;
                FireMode = fireMode;
                BurstCount = burstCount;
                MagazineCapacity = magazineCapacity;
                ReloadSeconds = reloadSeconds;
                BaseSpread = baseSpread;
                SpreadPerShot = spreadPerShot;
                MaxSpread = maxSpread;
                SpreadRecovery = spreadRecovery;
                RangeMeters = rangeMeters;
            }

            public string CaliberId { get; }

            public float Damage { get; }

            public float RoundsPerMinute { get; }

            public WeaponFireMode FireMode { get; }

            public int BurstCount { get; }

            public int MagazineCapacity { get; }

            public float ReloadSeconds { get; }

            public float BaseSpread { get; }

            public float SpreadPerShot { get; }

            public float MaxSpread { get; }

            public float SpreadRecovery { get; }

            public float RangeMeters { get; }
        }

        /// <summary>弹药战斗参数的生成数据。</summary>
        private readonly struct AmmoSpec
        {
            public AmmoSpec(string caliberId, float penetration)
            {
                CaliberId = caliberId;
                Penetration = penetration;
            }

            public string CaliberId { get; }

            public float Penetration { get; }
        }

        /// <summary>护甲战斗参数的生成数据。</summary>
        private readonly struct ArmorSpec
        {
            public ArmorSpec(int protectionLevel, float maxDurability, float wearFactor)
            {
                ProtectionLevel = protectionLevel;
                MaxDurability = maxDurability;
                WearFactor = wearFactor;
            }

            public int ProtectionLevel { get; }

            public float MaxDurability { get; }

            public float WearFactor { get; }
        }

        /// <summary>
        /// 武器参数表：物品 ID 到武器参数的映射。
        /// </summary>
        /// <remarks>
        /// <para>与物品表分开列的原因：物品表回答"游戏里有哪些东西"，
        /// 这张表回答"这些东西打起来是什么手感"。两者的修改频率与审阅角度都不同，
        /// 混在一张表里会让每次调数值都要滚动整个物品清单。</para>
        /// <para>射速与散布的取值参照：600 发/分 = 每 0.1 秒一发；
        /// 散布是锥角全宽，1.5 度在 20 米处大约偏 0.26 米。</para>
        /// </remarks>
        private static readonly Dictionary<string, WeaponSpec> s_WeaponSpecs =
            new Dictionary<string, WeaponSpec>
            {
                ["weapon.pistol.pm"] = new WeaponSpec(
                    "9x19", damage: 18f, roundsPerMinute: 400f, fireMode: WeaponFireMode.Single,
                    burstCount: 1, magazineCapacity: 8, reloadSeconds: 1.8f,
                    baseSpread: 2.5f, spreadPerShot: 0.8f, maxSpread: 6f, spreadRecovery: 5f,
                    rangeMeters: 25f),
                ["weapon.rifle.ak74"] = new WeaponSpec(
                    "5.45", damage: 25f, roundsPerMinute: 600f, fireMode: WeaponFireMode.Auto,
                    burstCount: 3, magazineCapacity: 30, reloadSeconds: 2.2f,
                    baseSpread: 1.5f, spreadPerShot: 0.5f, maxSpread: 6f, spreadRecovery: 4f,
                    rangeMeters: 40f),
            };

        /// <summary>
        /// 弹药参数表。
        /// </summary>
        /// <remarks>
        /// 穿透力的参照系是护甲值（每级 10）：标准弹打 2 级甲刚好只能部分穿透，
        /// 打 3 级甲则完全被挡下。这样"带什么子弹"才有明确的选择意义。
        /// </remarks>
        private static readonly Dictionary<string, AmmoSpec> s_AmmoSpecs =
            new Dictionary<string, AmmoSpec>
            {
                ["ammo.9x19.standard"] = new AmmoSpec("9x19", 15f),
                ["ammo.5.45.standard"] = new AmmoSpec("5.45", 22f),
            };

        /// <summary>护甲参数表。</summary>
        private static readonly Dictionary<string, ArmorSpec> s_ArmorSpecs =
            new Dictionary<string, ArmorSpec>
            {
                ["armor.helmet.steel"] = new ArmorSpec(protectionLevel: 2, maxDurability: 40f, wearFactor: 0.3f),
                ["armor.vest.plate"] = new ArmorSpec(protectionLevel: 3, maxDurability: 60f, wearFactor: 0.35f),
            };

        /// <summary>
        /// 给物品挂上战斗参数资产。
        /// </summary>
        /// <param name="definition">要挂接的物品定义。</param>
        /// <remarks>
        /// 战斗参数以 <see cref="ItemBehavior"/> 子资产的形式挂在物品上，
        /// 因此 <see cref="ItemDefinition"/> 本身不需要任何战斗专用字段——
        /// 新增一个品类只需要新增一个行为子类，物品表一行都不用改。
        /// </remarks>
        private static void AttachCombatStats(ItemDefinition definition)
        {
            var id = definition.Id;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (s_WeaponSpecs.TryGetValue(id, out var weapon))
            {
                var asset = LoadOrCreate<WeaponStats>($"{ItemFolder}/Stats_{id}.asset");
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("m_BaseDamage").floatValue = weapon.Damage;
                serialized.FindProperty("m_RoundsPerMinute").floatValue = weapon.RoundsPerMinute;
                serialized.FindProperty("m_FireMode").enumValueIndex = (int)weapon.FireMode;
                serialized.FindProperty("m_BurstCount").intValue = weapon.BurstCount;
                serialized.FindProperty("m_MagazineCapacity").intValue = weapon.MagazineCapacity;
                serialized.FindProperty("m_CaliberId").stringValue = weapon.CaliberId;
                serialized.FindProperty("m_ReloadSeconds").floatValue = weapon.ReloadSeconds;
                serialized.FindProperty("m_BaseSpreadDegrees").floatValue = weapon.BaseSpread;
                serialized.FindProperty("m_SpreadPerShotDegrees").floatValue = weapon.SpreadPerShot;
                serialized.FindProperty("m_MaxSpreadDegrees").floatValue = weapon.MaxSpread;
                serialized.FindProperty("m_SpreadRecoveryPerSecond").floatValue = weapon.SpreadRecovery;
                serialized.FindProperty("m_RangeMeters").floatValue = weapon.RangeMeters;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssignBehavior(definition, asset);
                return;
            }

            if (s_AmmoSpecs.TryGetValue(id, out var ammo))
            {
                var asset = LoadOrCreate<AmmoStats>($"{ItemFolder}/Stats_{id}.asset");
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("m_CaliberId").stringValue = ammo.CaliberId;
                serialized.FindProperty("m_Penetration").floatValue = ammo.Penetration;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssignBehavior(definition, asset);
                return;
            }

            if (s_ArmorSpecs.TryGetValue(id, out var armor))
            {
                var asset = LoadOrCreate<ArmorStats>($"{ItemFolder}/Stats_{id}.asset");
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("m_ProtectionLevel").intValue = armor.ProtectionLevel;
                serialized.FindProperty("m_MaxDurability").floatValue = armor.MaxDurability;
                serialized.FindProperty("m_WearFactor").floatValue = armor.WearFactor;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssignBehavior(definition, asset);
            }
        }

        /// <summary>把行为资产挂到物品定义上。</summary>
        private static void AssignBehavior(ItemDefinition definition, ItemBehavior behavior)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("m_Behavior").objectReferenceValue = behavior;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }
}
