using System;
using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器当前托管的世界种类（P4.5-b）。
    /// </summary>
    /// <remarks>
    /// <para>它决定"哪些系统该在这个世界里活着"：安全屋里没有 AI、没有战利品、没有撤离点，
    /// 只有移动碰撞；战局才有完整的权威内容。</para>
    /// </remarks>
    public enum ServerWorldKind : byte
    {
        /// <summary>还没有托管任何世界（启动后的极短瞬间）。</summary>
        None = 0,

        /// <summary>共享安全屋：玩家整备、等队友、房主在出口选图。</summary>
        SafeHouse = 1,

        /// <summary>战局：搜刮、对抗 AI、撤离。</summary>
        Raid = 2,
    }

    /// <summary>
    /// 服务器运行时的"世界"部分：在安全屋与战局两张场景之间切换，并重建该世界的权威内容。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么服务器要托管安全屋（P4.5-b 的核心）：</b>联机首站是共享安全屋，
    /// 玩家在这里走动的每一步都必须由服务器裁定——否则"我在安全屋等你"就只是各自屏幕上的幻觉，
    /// 队友的位置、以及"全员是否都回了屋"这个门禁条件都无从谈起。</para>
    ///
    /// <para><b>为什么必须显式重建：</b>服务器的多个子系统都是"第一次看到地图就建好、之后再不动"
    /// 的惰性初始化（导航网格、战利品容器、撤离点、AI）。它们在"一张图一整局"的前提下成立，
    /// 而现在服务器会切图：切图时必须把旧世界的产物全部作废，否则新世界里会留着旧世界的
    /// 导航网格与箱子内容，症状是"第二局搜到的还是上一局的箱子"。</para>
    ///
    /// <para><b>时序约定：</b><c>SceneManager.LoadScene</c> 要到本帧稍后才真正生效，
    /// 因此切世界是两阶段的：先作废旧世界并登记目标场景，等目标场景成为活动场景之后
    /// （<see cref="TickWorld"/>）再执行世界就绪回调（开局 / 回屋）。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>当前托管的世界种类。</summary>
        private ServerWorldKind m_WorldKind = ServerWorldKind.None;

        /// <summary>当前（或正在切换到的）世界场景名。</summary>
        private string m_WorldSceneName = string.Empty;

        /// <summary>世界就绪后的收尾动作（开局 / 回屋）；为空表示本次切换没有后续。</summary>
        private Action m_OnWorldReady;

        /// <summary>是否正在等待目标场景生效。</summary>
        private bool m_WorldPending;

        /// <summary>服务器正在托管的世界种类。</summary>
        public ServerWorldKind WorldKind
        {
            get { return m_WorldKind; }
        }

        /// <summary>当前世界的场景名（切换过程中是目标场景名）。</summary>
        public string WorldSceneName
        {
            get { return m_WorldSceneName; }
        }

        /// <summary>安全屋是否已经就绪（在屋里且场景已生效）——门禁与状态页据此判断。</summary>
        public bool SafeHouseReady
        {
            get { return m_WorldKind == ServerWorldKind.SafeHouse && !m_WorldPending; }
        }

        /// <summary>
        /// 切到安全屋世界（服务器启动时、以及每一次战局结束后）。
        /// </summary>
        /// <param name="onReady">安全屋生效之后要做的事（例如把全员放回屋里）。</param>
        internal void EnterSafeHouseWorld(Action onReady = null)
        {
            BeginWorldSwitch(ServerWorldKind.SafeHouse, GameScenes.SafeHouse, onReady);
        }

        /// <summary>
        /// 切到战局世界（房主确认出击时）。
        /// </summary>
        /// <param name="mapSceneName">战局地图场景名；为空时用服务器参数里的地图名。</param>
        /// <param name="onReady">战局世界生效之后要做的事（把人放进图、通知客户端加载地图）。</param>
        internal void EnterRaidWorld(string mapSceneName, Action onReady)
        {
            BeginWorldSwitch(ServerWorldKind.Raid, ResolveRaidSceneName(mapSceneName), onReady);
        }

        /// <summary>
        /// 解析这一局要用的地图名：房主选择的优先，其次服务器参数，最后是默认地图。
        /// </summary>
        /// <remarks>
        /// 三层兜底而不是"必须传参"：自动化脚本可能不带 <c>-map</c>，
        /// 而"没有地图可打"应该退化成默认地图，而不是把玩家关在安全屋里没有任何解释。
        /// </remarks>
        internal string ResolveRaidSceneName(string mapSceneName)
        {
            if (!string.IsNullOrWhiteSpace(mapSceneName))
            {
                return mapSceneName.Trim();
            }

            if (m_Options != null && !string.IsNullOrWhiteSpace(m_Options.MapSceneName))
            {
                return m_Options.MapSceneName;
            }

            return GameScenes.DefaultRaid;
        }

        /// <summary>
        /// 开始一次世界切换：先作废旧世界，再登记目标场景。
        /// </summary>
        /// <remarks>
        /// 目标世界种类与场景名**立刻**写入（而不是等场景生效）：所有惰性初始化都按
        /// <see cref="m_WorldKind"/> 判断"这个系统该不该活"，提前写入可以让它们在切换窗口期内
        /// 保持沉默，而不是照着旧世界的数据继续跑。
        /// </remarks>
        private void BeginWorldSwitch(ServerWorldKind kind, string sceneName, Action onReady)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                m_Session?.Log.Error("[服务器] 世界切换被忽略：没有给出场景名。");
                return;
            }

            LeaveCurrentWorld();

            m_WorldKind = kind;
            m_WorldSceneName = sceneName;
            m_OnWorldReady = onReady;
            m_WorldPending = true;

            var active = SceneManager.GetActiveScene().name;
            if (active == sceneName)
            {
                // 目标场景就是当前场景（例如服务器启动时本来就在安全屋）：
                // 仍然要走一遍"世界就绪"，否则这次切换的收尾动作（放人、通知）永远不会执行。
                CompleteWorldSwitch();
                return;
            }

            m_Session?.Log.Info($"[服务器] 切换世界：{DescribeWorld(m_WorldKind)} → 场景「{sceneName}」。");
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>每帧检查：目标场景是否已经成为活动场景。</summary>
        private void TickWorld()
        {
            if (!m_WorldPending)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != m_WorldSceneName)
            {
                return;
            }

            CompleteWorldSwitch();
        }

        /// <summary>目标场景已生效：执行世界就绪回调。</summary>
        private void CompleteWorldSwitch()
        {
            m_WorldPending = false;

            // 战局世界的导航网格只能从刚生效的这张地图烘出来；安全屋世界则在切换时已经作废。
            BeginNavigation();

            var onReady = m_OnWorldReady;
            m_OnWorldReady = null;

            m_Session?.Log.Info($"[服务器] 世界就绪：{DescribeWorld(m_WorldKind)}（{m_WorldSceneName}）。");
            onReady?.Invoke();
        }

        /// <summary>
        /// 离开当前世界：把这个世界产生的一切权威内容作废。
        /// </summary>
        /// <remarks>
        /// <para><b>顺序有讲究：</b>先把玩家撤出——玩家载体是场景对象，会随旧场景一起卸载，
        /// 而模拟世界与几个字典里还留着他们的记录；不先清掉，进入新世界时就会出现
        /// "编号查得到、Transform 已经销毁"的幽灵玩家，表现为移动输入石沉大海。</para>
        ///
        /// <para>随后依次作废：战利品容器（编号对应旧地图的生成点顺序）、撤离点、
        /// 导航网格与寻路服务（旧地图烘出来的）、AI（旧地图的敌人）。</para>
        /// </remarks>
        private void LeaveCurrentWorld()
        {
            var playerIds = new List<int>();
            m_World?.GetPlayerIds(playerIds);

            for (var i = 0; i < playerIds.Count; i++)
            {
                RemovePlayerFromWorld(playerIds[i]);
            }

            m_RaidProgress.Clear();
            m_RaidStartedAt = -1f;

            // 撤离点：编号与位置都来自旧地图，必须重新收集。
            m_ExtractionZones.Clear();
            m_RaidZonesReady = false;

            // 场景容器：编号是"地图里第 i 个生成点"，换图后同一编号指向别的东西（或什么都没有）。
            UnregisterSceneContainers();
            m_ContainerDefinitions.Clear();
            m_ContainersReady = false;

            // AI 与导航：只属于战局世界，切换期间必须先停掉，避免它们拿着旧网格继续跑。
            ShutdownAi();
            ResetNavigationForWorldSwitch();
        }

        /// <summary>注销全部场景容器（玩家自己的容器不在其中）。</summary>
        private void UnregisterSceneContainers()
        {
            var sceneIds = new List<int>();
            var ids = m_Containers.ContainerIds;

            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] >= ContainerIds.SceneBase && ids[i] < ContainerIds.ServerPlayerBase)
                {
                    sceneIds.Add(ids[i]);
                }
            }

            for (var i = 0; i < sceneIds.Count; i++)
            {
                m_Containers.Unregister(sceneIds[i]);
            }
        }

        /// <summary>作废服务器侧的导航数据（换图后要重新烘焙）。</summary>
        private void ResetNavigationForWorldSwitch()
        {
            m_NavMeshSurface = null;
            m_Pathfinding = null;
            m_NavigationPending = false;
            m_NavigationWaitingReported = false;
        }

        /// <summary>
        /// 门禁：全员是否都在安全屋里（房主在出口确认出击的前提）。
        /// </summary>
        /// <param name="reason">不满足时的中文原因（可直接显示给玩家）。</param>
        /// <remarks>
        /// <para>判据是"服务器的权威世界里有没有这个人"：进房即被放进安全屋世界，
        /// 战局开始时才被移出去，因此这个判断同时覆盖了"还在战局里"与"刚加入、还没进屋"两种情况。</para>
        ///
        /// <para>它只回答"能不能出发"，不改变任何状态——拒绝的路径由调用方决定怎么回退。</para>
        /// </remarks>
        internal bool AreAllMembersInSafeHouse(out string reason)
        {
            reason = string.Empty;

            if (m_WorldKind != ServerWorldKind.SafeHouse || m_WorldPending)
            {
                reason = "服务器还在准备安全屋，请稍候。";
                return false;
            }

            for (var i = 0; i < m_Room.MemberCount; i++)
            {
                var member = m_Room.Members[i];
                if (m_PlayerBodies.ContainsKey(member.ClientId) && m_PlayerBodies[member.ClientId] != null)
                {
                    continue;
                }

                reason = $"队友「{member.Nickname}」还没有回到安全屋。";
                return false;
            }

            return true;
        }

        /// <summary>把当前房间里的所有人放进安全屋世界（世界就绪后调用）。</summary>
        private void SpawnMembersIntoSafeHouse()
        {
            for (var i = 0; i < m_Room.MemberCount; i++)
            {
                SpawnPlayerIntoWorld(m_Room.Members[i].ClientId);
            }

            m_Session?.Log.Info($"[服务器] 共享安全屋已就绪：{m_Room.MemberCount} 名玩家在屋内。");
        }

        /// <summary>
        /// 玩家进入房间：把他放进共享安全屋。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 世界正处在切换窗口期时不放人：那一刻场景还没生效，载体对象会建在旧场景里，
        /// 随即被场景卸载销毁，留下的是一条"查得到编号、拿不到 Transform"的幽灵记录。
        /// 这种情况由世界就绪回调统一补上（它会遍历房间成员）。
        /// </remarks>
        internal void SpawnMemberIntoSafeHouse(int playerId)
        {
            if (!SafeHouseReady)
            {
                m_Session?.Log.Info($"[服务器] 玩家 {playerId} 进房时安全屋尚未就绪，稍后随世界就绪统一入屋。");
                return;
            }

            SpawnPlayerIntoWorld(playerId);
        }

        /// <summary>
        /// 安全屋里的出生位置：以场景里的出生点为中轴，按编号横向排开。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// <para>出生点来自安全屋场景自己的序列化字段（<c>SafeHouseBootstrap.m_PlayerSpawnPosition</c>），
        /// 由装配根在服务器模式下交接给 <see cref="ServerMode.SafeHouseSpawnPosition"/>——
        /// 这样"客户端在哪出生"与"服务器认为他在哪出生"永远出自同一份数据，
        /// 不需要在这里抄一个坐标常量（抄一次就会在下次改场景时对不上）。</para>
        ///
        /// <para>横向排开而不是叠在同一点：四个人叠在一起时，移动扫掠会把彼此挤开，
        /// 表现是"一进屋就被弹到墙角"。规则在 <see cref="PlayerSpawnLayout"/>，
        /// 客户端接管连接前会用同一份规则预置自己的位置——两边分头算会导致进屋瞬间被硬拉一次（M13-22）。</para>
        /// </remarks>
        private static Vector2F SafeHouseSpawnPositionFor(int playerId)
        {
            return PlayerSpawnLayout.SafeHousePosition(
                ServerMode.SafeHouseSpawnPosition,
                playerId,
                LobbyLimits.MaxPlayers);
        }

        /// <summary>世界的中文名（日志与状态页共用）。</summary>
        internal static string DescribeWorld(ServerWorldKind kind)
        {
            switch (kind)
            {
                case ServerWorldKind.SafeHouse:
                    return "共享安全屋";
                case ServerWorldKind.Raid:
                    return "战局";
                default:
                    return "未托管";
            }
        }
    }
}
