using System.Text;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 撤离点可达性校验：在编辑期把「玩家能不能真的走到撤离点」跑一遍。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>导航网格连通不等于玩家走得过去。批次 2 就出现过这样的情况——
    /// 四个撤离点的 <c>NavMesh.CalculatePath</c> 全是 <c>PathComplete</c>，
    /// 但东侧坡道上正堵着一个集装箱：导航会绕开它，而 6 米宽的坡道被占掉一半之后，
    /// 玩家的胶囊扫掠根本挤不过去，第四个撤离点实际不可达。</para>
    ///
    /// <para><b>怎么校验：</b>对每个撤离点做两件事。第一件是算一次导航路径，检查规划上通不通；
    /// 第二件是让一个虚拟玩家沿着路径的实际拐点一步步走，每一步都用与 PlayerMotor 相同的
    /// 胶囊扫掠（<see cref="PhysicsMovementCollisionService"/>）与向下射线落地，
    /// 连续多步几乎走不动就判定为卡住，检查物理上通不通。</para>
    ///
    /// <para>校验过程会临时禁用玩家自身的碰撞体（否则虚拟玩家一出生就被玩家自己挡住），
    /// 结束时无论成功与否都会恢复。</para>
    /// </remarks>
    public static class M7ExtractionAccessVerifier
    {
        /// <summary>战局场景路径。</summary>
        private const string RaidScenePath = "Assets/Game/Content/Scenes/GreyboxRaid.unity";

        /// <summary>单步行走长度（米）。取得小一些，才能发现只有几步宽的缝隙。</summary>
        private const float WalkStepMeters = 0.25f;

        /// <summary>判定这一步被挡住的比例阈值：实际位移不足期望的四分之一。</summary>
        private const float StuckStepRatio = 0.25f;

        /// <summary>连续多少步被挡住就判定卡住。</summary>
        private const int StuckStepLimit = 24;

        /// <summary>走到离撤离点中心多近算成功（米）。</summary>
        private const float ReachedRadiusMeters = 2.5f;

        /// <summary>玩家胶囊参数，与 PlayerMotor / PhysicsMovementCollisionService 保持一致。</summary>
        private const float BodyRadius = 0.4f;
        private const float BodyHeight = 1.8f;

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/校验四个撤离点可达性")]
        public static void VerifyFromMenu()
        {
            Debug.Log(VerifyAll());
        }

        /// <summary>校验当前地图全部撤离点的可达性，返回中文报告。</summary>
        public static string VerifyAll()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != RaidScenePath)
            {
                EditorSceneManager.OpenScene(RaidScenePath, OpenSceneMode.Single);
            }

            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface != null)
            {
                surface.BuildNavMesh();
            }

            var report = new StringBuilder("[RaidDemo] 撤离点可达性校验");
            var player = GameObject.Find("Player");
            if (player == null)
            {
                return report.Append("：场景里没有 Player，无法校验。").ToString();
            }

            var markers = Object.FindObjectsByType<ExtractionZoneMarker>(FindObjectsSortMode.None);
            var spawn = player.transform.position;
            ToggleColliders(player, false);
            try
            {
                foreach (var marker in markers)
                {
                    report.Append('\n').Append(VerifyOne(spawn, marker));
                }

                report.Append('\n').Append(VerifyGroundSampling());
            }
            finally
            {
                ToggleColliders(player, true);
            }

            return report.ToString();
        }

        /// <summary>
        /// 抽检地面采样：确认「站在谷底的单位不会被告到平台或坡道的高度」。
        /// </summary>
        /// <remarks>
        /// 这是批次 2 那个「小兵走到箱子旁边就瞬移到上面」的问题的回归检查：
        /// 装卸平台顶面在 -4.8，谷底在 -6，两者只差 1.2 米，早期实现只要靠得够近就会选错。
        /// </remarks>
        private static string VerifyGroundSampling()
        {
            var report = new StringBuilder("  地面采样抽检：");
            report.Append('\n').Append("    谷底（15, 0）→ ").Append(SampleDescription(new Vector2F(15f, 0f), -6f));
            report.Append('\n').Append("    平台顶（2, -23）→ ").Append(SampleDescription(new Vector2F(2f, -23f), -4.8f));
            report.Append('\n').Append("    北坡道中段（0, 25）→ ").Append(SampleDescription(new Vector2F(0f, 25f), -3.2f));
            return report.ToString();
        }

        /// <summary>输出一个采样点的实测高度与期望高度。</summary>
        private static string SampleDescription(Vector2F position, float expected)
        {
            if (!NavMeshGroundSampler.TrySample(position, out var height))
            {
                return "采样失败";
            }

            var ok = Mathf.Abs(height - expected) <= 0.75f;
            return $"实测 {height:F2}（期望 {expected:F2}）{(ok ? " OK" : " 不符")}";
        }

        /// <summary>校验单个撤离点。</summary>
        private static string VerifyOne(Vector3 spawn, ExtractionZoneMarker marker)
        {
            var target = marker.transform.position;
            var start = SnapToNavMesh(spawn);
            var goal = SnapToNavMesh(target);

            var path = new NavMeshPath();
            var planned = NavMesh.CalculatePath(start, goal, NavMesh.AllAreas, path);
            if (!planned || path.status != NavMeshPathStatus.PathComplete)
            {
                return $"  #{marker.ZoneId} {marker.DisplayName}：导航不可达";
            }

            var walk = SimulateWalk(spawn, target, path);
            return $"  #{marker.ZoneId} {marker.DisplayName}：导航 OK（{path.corners.Length} 个拐点），行走{walk}";
        }

        /// <summary>让虚拟玩家沿路径走一遍。</summary>
        private static string SimulateWalk(Vector3 spawn, Vector3 target, NavMeshPath path)
        {
            var walker = new GameObject("M7VerifyWalker");
            try
            {
                var current = new Vector2F(spawn.x, spawn.z);
                var height = SampleGround(current, spawn.y);
                var collision = new PhysicsMovementCollisionService(walker.transform, BodyHeight, 0.02f);
                var stuckSteps = 0;

                var corners = path.corners;
                for (var i = 1; i < corners.Length; i++)
                {
                    var corner = new Vector2F(corners[i].x, corners[i].z);
                    var guard = 0;
                    while (Vector2F.Distance(current, corner) > WalkStepMeters && guard++ < 4000)
                    {
                        var toCorner = corner - current;
                        var distance = toCorner.Magnitude;
                        var desired = toCorner * (Mathf.Min(WalkStepMeters, distance) / distance);

                        collision.TryResolveMove(current, desired, BodyRadius, out var resolved);
                        current += resolved;
                        height = SampleGround(current, height);
                        walker.transform.position = new Vector3(current.X, height, current.Y);

                        if (resolved.Magnitude < desired.Magnitude * StuckStepRatio)
                        {
                            stuckSteps++;
                            if (stuckSteps >= StuckStepLimit)
                            {
                                return $"卡住（停在 {current.X:F1}, {current.Y:F1}）";
                            }
                        }
                        else
                        {
                            stuckSteps = 0;
                        }
                    }
                }

                var reached = Vector2F.Distance(current, new Vector2F(target.x, target.z)) <= ReachedRadiusMeters;
                return reached
                    ? $"成功（终点 {current.X:F1}, {current.Y:F1}，高度 {height:F2}）"
                    : $"未到达（停在 {current.X:F1}, {current.Y:F1}）";
            }
            finally
            {
                Object.DestroyImmediate(walker);
            }
        }

        /// <summary>把一个世界坐标吸附到最近的导航网格上（找不到时原样返回）。</summary>
        private static Vector3 SnapToNavMesh(Vector3 position)
        {
            return NavMesh.SamplePosition(position, out var hit, 12f, NavMesh.AllAreas)
                ? hit.position
                : position;
        }

        /// <summary>
        /// 向下探地面，规则与 PlayerMotor.SampleGroundHeight 一致：
        /// 只接受朝上的面，取不超过上一高度加 0.35 米的最高命中，探不到就保持原高度。
        /// </summary>
        private static float SampleGround(Vector2F position, float referenceHeight)
        {
            var maxHeight = referenceHeight + 0.35f;
            var origin = new Vector3(position.X, referenceHeight + 8f, position.Y);
            var hits = Physics.RaycastAll(origin, Vector3.down, 16f, ~0, QueryTriggerInteraction.Ignore);

            var best = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.normal.y < 0.5f || hit.point.y > maxHeight)
                {
                    continue;
                }

                if (hit.point.y > best)
                {
                    best = hit.point.y;
                }
            }

            return best > float.NegativeInfinity ? best : referenceHeight;
        }

        /// <summary>临时开关一个对象（含子物体）的全部碰撞体。</summary>
        private static void ToggleColliders(GameObject root, bool enabled)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                collider.enabled = enabled;
            }
        }
    }
}
