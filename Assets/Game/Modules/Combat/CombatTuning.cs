using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 战斗全局调参。集中放置而不是散落在各处，便于按体感统一调整。
    /// </summary>
    /// <remarks>
    /// 做成普通类而不是 ScriptableObject：本层是纯逻辑，需要能在测试里直接构造。
    /// 若将来需要策划在 Inspector 里调，再做一层资产到配置的转换即可，规则代码不用动。
    /// </remarks>
    public sealed class CombatTuning
    {
        /// <summary>暴击倍率。初始值 2.0——打中上半身伤害翻倍，幅度明显但不至于秒杀。</summary>
        public float CriticalMultiplier = 2f;

        /// <summary>
        /// 暴击轴：屏幕上"向上"这个方向在世界地面上的对应向量。
        /// </summary>
        /// <remarks>
        /// <para>斜俯视下玩家看到的是地面，所以"打上半身"在几何上等价于
        /// **瞄准目标远离相机的那一侧**。把相机朝向投影到地面得到这个轴之后，
        /// 暴击判定就变成一次点乘。</para>
        /// <para>由启动层按实际相机设置写入，而不是写死方向——
        /// 相机布局一旦调整，暴击方向会跟着变，写死就会悄悄失效。</para>
        /// </remarks>
        public Vector3 CriticalAxis = new Vector3(0f, 0f, 1f);

        /// <summary>
        /// 暴击偏移阈值（米）。
        /// </summary>
        /// <remarks>
        /// 命中点在暴击轴上的投影超过该值即判定为暴击。
        /// 取值应当明显小于目标半径，好让目标身上有一段相当宽的暴击带，
        /// 不要求玩家做像素级瞄准。
        /// </remarks>
        public float CriticalOffsetMeters = 0.25f;

        /// <summary>未指定磨损系数时使用的默认值。</summary>
        public float DefaultWearFactor = 0.35f;

        /// <summary>
        /// 找不到匹配弹药时假定的穿透力。
        /// </summary>
        /// <remarks>
        /// 新装备的武器弹匣是满的，但玩家可能一发子弹都没带。
        /// 此时按这个值结算，而不是按 0——穿透力为 0 会让每一枪都被护甲吃满减伤，
        /// 表现为"打不动人"，玩家会以为是 bug 而不是自己没带子弹。
        /// 取值对应最普通的弹药。
        /// </remarks>
        public float DefaultPenetration = 15f;

        /// <summary>默认调参。</summary>
        public static CombatTuning Default
        {
            get { return new CombatTuning(); }
        }

        /// <summary>校验参数自洽性，供启动阶段尽早发现错误配置。</summary>
        /// <returns>校验通过返回 null，否则返回中文说明。</returns>
        public string Validate()
        {
            if (CriticalMultiplier < 1f)
            {
                return "暴击倍率不应小于 1，否则暴击比普通命中还弱。";
            }

            if (CriticalOffsetMeters < 0f)
            {
                return "暴击偏移阈值不能为负。";
            }

            if (DefaultWearFactor < 0f)
            {
                return "默认磨损系数不能为负。";
            }

            return null;
        }
    }
}
