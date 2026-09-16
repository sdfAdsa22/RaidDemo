using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务端进度存档（P5）：一间屋子的共享仓库 + 每个账号的局外进度。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题：</b>在 P5 之前，联机玩家的仓库与金币只存在于**客户端本地**——
    /// 换台电脑就没了，两个人也永远看不到对方放进仓库的东西。搬上服务器之后：
    /// 仓库是房间级共享的，金币与任务是账号级的，两者都落在服务端的存档目录里，
    /// 服务器重启后仍然在。</para>
    ///
    /// <para><b>与单机存档的隔离：</b>本类只读写的文件位于服务器的 <c>-saveDirectory</c>
    /// （默认 <c>server_saves</c>），与客户端 <c>Application.persistentDataPath</c> 下的
    /// 单机存档毫无交集；联机客户端在会话期间也不会写自己的单机存档
    /// （见 <c>RaidFlowController.SaveNow</c>）。这条隔离是验收项之一：
    /// "联机刷到的装备不能带回单机"。</para>
    ///
    /// <para><b>为什么目录是后注入的：</b>物品要按 ID 还原成定义，而物品目录来自场景
    /// （服务器启动时它可能还没交接过来）。因此构造只建空仓库，等目录到位时再读盘一次；
    /// 在那之前 <see cref="GetOrCreate"/> 会返回 null，调用方（登录）据此重试。</para>
    ///
    /// <para><b>写盘时机：</b>标脏 + 节流刷新（见 <see cref="Tick"/>）而不是每次操作都写。
    /// 玩家在安全屋里拖动一次物品就是一条命令，逐条落盘会把磁盘当内存用；
    /// 而"撤离结算"这类关键状态在结算路径里会立刻写一次。</para>
    ///
    /// <para>线程约束：只在主线程调用（与项目其余部分一致）。</para>
    /// </remarks>
    public sealed class ServerProfileStore
    {
        /// <summary>默认文件名（放在服务器的存档目录下）。</summary>
        public const string DefaultFileName = "profiles.json";

        /// <summary>
        /// 标脏之后多久落盘（秒）。
        /// </summary>
        /// <remarks>
        /// 取 4 秒：短于"玩家拖完东西去按出击"的间隔，长于连续拖动的命令间隔，
        /// 因此既不会丢失刚刚做好的准备，也不会把每一次拖拽都变成一次写盘。
        /// </remarks>
        private const float FlushIntervalSeconds = 4f;

        private readonly SaveFileStore m_File;
        private readonly InventoryGrid m_SharedStash;
        private readonly Dictionary<string, MetaProgress> m_Profiles =
            new Dictionary<string, MetaProgress>(StringComparer.Ordinal);
        /// <summary>已领过基础装备的账号（AR-08；随进度文档落盘）。</summary>
        private readonly Dictionary<string, bool> m_StarterKitIssued =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<string> m_LoadProblems = new List<string>();

        private IItemDefinitionLookup m_Catalog;
        private bool m_Loaded;
        private bool m_Dirty;
        private float m_NextFlushTime;

        /// <summary>创建存档服务（此时还不读盘，等物品目录到位）。</summary>
        /// <param name="directory">服务端存档目录（相对进程工作目录）。</param>
        public ServerProfileStore(string directory)
        {
            m_File = new SaveFileStore(directory, DefaultFileName);
            m_SharedStash = new InventoryGrid(MetaProgress.StashWidth, MetaProgress.StashHeight, "共享仓库");
        }

        /// <summary>房间共享仓库网格（服务器侧权威对象）。</summary>
        public InventoryGrid SharedStash
        {
            get { return m_SharedStash; }
        }

        /// <summary>存档文件路径（日志与状态页用）。</summary>
        public string FilePath
        {
            get { return m_File.FilePath; }
        }

        /// <summary>已载入的账号数。</summary>
        public int ProfileCount
        {
            get { return m_Profiles.Count; }
        }

        /// <summary>是否已经完成读盘（= 物品目录已注入）。</summary>
        public bool IsReady
        {
            get { return m_Loaded; }
        }

        /// <summary>是否有尚未落盘的改动。</summary>
        public bool HasUnsavedChanges
        {
            get { return m_Dirty; }
        }

        /// <summary>读盘时被跳过的条目说明（供日志）。</summary>
        public IReadOnlyList<string> LoadProblems
        {
            get { return m_LoadProblems; }
        }

        /// <summary>
        /// 注入物品目录并读盘（幂等；重复调用只生效一次）。
        /// </summary>
        /// <param name="catalog">场景里的物品目录。</param>
        /// <returns>可以开始使用（读到或确认无档）时返回 true。</returns>
        public bool Configure(IItemDefinitionLookup catalog)
        {
            if (catalog == null)
            {
                return false;
            }

            if (m_Loaded)
            {
                return true;
            }

            m_Catalog = catalog;
            m_Loaded = true;
            LoadFromDisk();
            return true;
        }

        /// <summary>
        /// 取某个账号的进度；没有就建一份新的。
        /// </summary>
        /// <param name="nickname">账号昵称。</param>
        /// <returns>该账号的进度；目录未就绪或昵称非法时返回 null。</returns>
        public MetaProgress GetOrCreate(string nickname)
        {
            if (!m_Loaded || string.IsNullOrWhiteSpace(nickname))
            {
                return null;
            }

            var key = nickname.Trim();
            if (m_Profiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // 新账号：仓库是共享的那一份，随身装备为空，金币用启动资金。
            var created = new MetaProgress(MetaProgress.StartingMoney, m_SharedStash);
            created.AttachCatalog(m_Catalog);

            // 新账号发一套基础装备（P5）：否则在"商店尚未服务端权威化"的阶段，
            // 联机新玩家手里什么都没有、也买不到东西，连一局都打不了。
            // 装备填进"随身装备"而不是仓库：玩家进图就能用，撤离时又会按规则入共享仓库。
            //
            // AR-08：每个账号只发一次。标记随进度文档落盘——撤离把装备搬进仓库之后，
            // 进度文档仍在，"再建一份空进度"的路径不会又发一套（那正是"空手进图→撤离入库"
            // 反复刷装备的入口）。
            var granted = 0;
            if (!WasStarterKitIssued(key))
            {
                granted = ServerStarterKit.Apply(created.Loadout, m_Catalog, new ItemFactory());
                if (granted > 0)
                {
                    m_StarterKitIssued[key] = true;
                }
            }

            m_Profiles[key] = created;
            MarkDirty();

            if (granted > 0)
            {
                // 一条日志：验收脚本与运维都能看到"这套装备是什么时候发的"。
                UnityEngine.Debug.Log($"[服务器] 新账号「{key}」已配发基础装备（{granted} 件）。");
            }

            return created;
        }

        /// <summary>按昵称查进度；没有时返回 null（不创建）。</summary>
        /// <param name="nickname">账号昵称。</param>
        public MetaProgress Find(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return null;
            }

            return m_Profiles.TryGetValue(nickname.Trim(), out var progress) ? progress : null;
        }

        /// <summary>该账号是否已领过基础装备（AR-08）。</summary>
        public bool WasStarterKitIssued(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return false;
            }

            return m_StarterKitIssued.TryGetValue(nickname.Trim(), out var issued) && issued;
        }

        /// <summary>标记"有改动待落盘"。</summary>
        public void MarkDirty()
        {
            m_Dirty = true;
            m_NextFlushTime = FlushIntervalSeconds;
        }

        /// <summary>按节拍落盘（由服务器主循环每帧调用）。</summary>
        /// <param name="deltaTime">帧时间（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (!m_Dirty)
            {
                return;
            }

            m_NextFlushTime -= deltaTime;
            if (m_NextFlushTime > 0f)
            {
                return;
            }

            SaveAll();
        }

        /// <summary>
        /// 立即落盘（结算、关服等重要时刻调用）。
        /// </summary>
        /// <returns>写入成功返回 true。</returns>
        public bool SaveAll()
        {
            m_Dirty = false;
            m_NextFlushTime = 0f;

            if (!m_Loaded)
            {
                return false;
            }

            var document = new ServerProfilesDocument
            {
                schemaVersion = ServerProfilesDocument.CurrentSchemaVersion,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                sharedStash = MetaSaveMapper.CaptureSharedStash(m_SharedStash),
                profiles = CaptureProfiles(),
            };

            if (!m_File.Save(document, out var error))
            {
                // 落盘失败要重新标脏：下一次节拍再试，而不是把改动丢掉。
                m_Dirty = true;
                return false;
            }

            return true;
        }

        /// <summary>把内存里的账号整理成存档记录。</summary>
        private ServerProfileRecord[] CaptureProfiles()
        {
            var records = new List<ServerProfileRecord>(m_Profiles.Count);

            foreach (var pair in m_Profiles)
            {
                var data = MetaSaveMapper.Capture(pair.Value, raidInProgress: false);
                if (data == null)
                {
                    continue;
                }

                // 仓库是房间级的那一份：账号记录里不带它，避免同一批物品写两遍
                // （更要紧的是避免读档时旧副本把共享仓库覆盖回去）。
                data.stash = new ItemStackSave[0];

                records.Add(new ServerProfileRecord
                {
                    nickname = pair.Key,
                    progress = data,
                    starterKitIssued = WasStarterKitIssued(pair.Key),
                });
            }

            return records.ToArray();
        }

        /// <summary>读盘：共享仓库 + 各账号进度。</summary>
        private void LoadFromDisk()
        {
            m_LoadProblems.Clear();

            if (!m_File.TryLoad<ServerProfilesDocument>(out var document, out var error) || document == null)
            {
                // 没有档（首次启动）不是错误；读到坏档也不该让服务器起不来——
                // 只是这一轮的仓库从空开始，日志里留一条。
                if (m_File.Exists && !string.IsNullOrEmpty(error))
                {
                    m_LoadProblems.Add($"进度文档读取失败（按空档继续）：{error}");
                }

                return;
            }

            MetaSaveMapper.RestoreSharedStash(
                m_SharedStash, document.sharedStash, m_Catalog, out var stashProblems);
            m_LoadProblems.AddRange(stashProblems);

            var records = document.profiles;
            if (records == null)
            {
                return;
            }

            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null || string.IsNullOrWhiteSpace(record.nickname) || record.progress == null)
                {
                    continue;
                }

                var progress = MetaSaveMapper.Restore(
                    record.progress, m_Catalog, out var problems, m_SharedStash);
                if (progress == null)
                {
                    m_LoadProblems.Add($"账号「{record.nickname}」的进度读取失败，已跳过。");
                    continue;
                }

                m_Profiles[record.nickname.Trim()] = progress;
                if (record.starterKitIssued)
                {
                    m_StarterKitIssued[record.nickname.Trim()] = true;
                }

                for (var p = 0; p < problems.Count; p++)
                {
                    m_LoadProblems.Add($"账号「{record.nickname}」：{problems[p]}");
                }
            }
        }
    }
}
