using System;
using RaidDemo.Meta;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务端进度文档（P5）：一间屋子的共享仓库 + 每名玩家的账号进度。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么共享仓库在文档级、账号进度在数组里：</b>仓库是**房间级**的——
    /// 同一间安全屋里的所有人看的是同一份；金币、任务、随身装备是**账号级**的，各算各的。
    /// 把这两类数据放在同一个文档里，是为了让"撤离结算"这个动作只有一次写入：
    /// 物品进了仓库、钱进了钱包，两件事必须一起落盘，否则服务器崩溃会留下
    /// "装备没了、仓库也没有"的中间状态。</para>
    ///
    /// <para><b>账号记录复用单机那套 DTO（<see cref="MetaSaveData"/>）：</b>服务端与单机
    /// 用同一份物品记录格式，工具与测试都能共用；差别只在仓库字段（服务端写空，
    /// 仓库走文档级那一份）。</para>
    ///
    /// <para>字段名进了玩家服务器上的存档文件，只加不改：改名等于让旧档读不出来。</para>
    /// </remarks>
    [Serializable]
    public sealed class ServerProfilesDocument
    {
        /// <summary>当前文档结构版本。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>文档结构版本，用于将来迁移。</summary>
        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>写入时间（UTC，ISO 8601）。只用于显示与排查。</summary>
        public string savedAtUtc = string.Empty;

        /// <summary>房间共享仓库的物品（房间级）。</summary>
        public ItemStackSave[] sharedStash = new ItemStackSave[0];

        /// <summary>各账号的进度（账号级）。</summary>
        public ServerProfileRecord[] profiles = new ServerProfileRecord[0];
    }

    /// <summary>
    /// 一名账号的服务端进度记录。
    /// </summary>
    /// <remarks>
    /// 昵称是唯一的键，与账号库（<c>ServerIdentityStore</c>）保持一致：
    /// 那份文件回答"你是谁"，这份回答"你有什么"。
    /// </remarks>
    [Serializable]
    public sealed class ServerProfileRecord
    {
        /// <summary>账号昵称（唯一键）。</summary>
        public string nickname = string.Empty;

        /// <summary>该账号的局外进度（金币、任务、随身装备、图鉴、角色选择）。</summary>
        public MetaSaveData progress;

        /// <summary>
        /// 该账号是否已经领过基础装备（AR-08）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要它：</b>基础装备（AK74 + 弹药）是"从零开始"的兜底配发，
        /// 每个账号只该领一次。没有这个标记时，任何"进度被重建"的路径都会再发一套，
        /// 而撤离会把随身装备全部入库——等于凭空多出一套装备。</para>
        /// <para>老档没有这个字段时按 false 处理：至多再补领一次，属可接受。</para>
        /// </remarks>
        public bool starterKitIssued;
    }
}
