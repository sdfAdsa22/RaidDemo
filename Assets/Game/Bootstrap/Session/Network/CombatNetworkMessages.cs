using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战斗事件的下行消息：把服务器结算出来的结果告诉客户端去播表现。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么一个结构体装四类事件：</b>它们共享"一次性事件"的语义与广播时机，
    /// 用一个带种类字段的结构体传输，比四条通道少三次注册与三处解析；
    /// 字段按种类复用，未用到的字段填默认值，代价是几个字节。</para>
    ///
    /// <para><b>客户端拿到之后做什么：</b>把它还原成对应的本地事件塞进事件总线——
    /// 弹道、枪口火焰、命中反馈、伤害数字、弹药 HUD 就会像单机一样自动亮起来。
    /// 这正是把战斗规则做成事件驱动的好处：联机适配只发生在"事件从哪来"这一层。</para>
    /// </remarks>
    public struct CombatEventMessage : INetworkSerializable
    {
        /// <summary>开火。</summary>
        public const byte KindFired = 1;

        /// <summary>伤害结算。</summary>
        public const byte KindDamaged = 2;

        /// <summary>目标被摧毁。</summary>
        public const byte KindDestroyed = 3;

        /// <summary>换弹状态变化。</summary>
        public const byte KindReload = 4;

        /// <summary>
        /// 治疗结算（联机医疗品由服务器执行时下发）。
        /// </summary>
        /// <remarks>
        /// 只发给被治疗者本人：生命值是私有状态。字段复用 <see cref="RemainingHealth"/>，
        /// 语义与伤害事件一致——"结算之后剩多少血"。
        /// </remarks>
        public const byte KindHealed = 5;

        /// <summary>事件种类（见上面的常量）。</summary>
        public byte Kind;

        /// <summary>射击者 / 攻击者 / 目标所属玩家（含义随种类变化）。</summary>
        public int SourceId;

        /// <summary>受击者 / 被摧毁目标（含义随种类变化）。</summary>
        public int TargetId;

        /// <summary>开火起点。</summary>
        public Vector3 Origin;

        /// <summary>开火终点（未命中时是射程末端）。</summary>
        public Vector3 EndPoint;

        /// <summary>开火是否命中。</summary>
        public bool DidHit;

        /// <summary>弹丸序号（霰弹枪一发多弹丸时用于区分）。</summary>
        public int PelletIndex;

        /// <summary>枪声半径（米）。</summary>
        public float NoiseRadius;

        /// <summary>本次伤害。</summary>
        public float Damage;

        /// <summary>剩余生命。</summary>
        public float RemainingHealth;

        /// <summary>是否击杀。</summary>
        public bool WasKilled;

        /// <summary>是否正在换弹。</summary>
        public bool IsReloading;

        /// <summary>当前弹匣弹药。</summary>
        public int MagazineAmmo;

        /// <summary>事件序号（沿用输入序号，便于两端对齐）。</summary>
        public uint Sequence;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref SourceId);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref EndPoint);
            serializer.SerializeValue(ref DidHit);
            serializer.SerializeValue(ref PelletIndex);
            serializer.SerializeValue(ref NoiseRadius);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref RemainingHealth);
            serializer.SerializeValue(ref WasKilled);
            serializer.SerializeValue(ref IsReloading);
            serializer.SerializeValue(ref MagazineAmmo);
            serializer.SerializeValue(ref Sequence);
        }
    }
}
