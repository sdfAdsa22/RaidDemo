using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 世界空间调试图形的绘制器：圆、扇形、直线、折线。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用 <see cref="LineRenderer"/> 而不是 OnGUI / Gizmos：</b>
    /// Gizmos 只在编辑器场景视图里可见（进不了构建，也截不到游戏画面）；
    /// OnGUI 是屏幕空间绘制，画不出"扇形贴在地面上"这种效果。
    /// LineRenderer 是游戏内可见的、能被截图与录像正确捕获的常规渲染对象。</para>
    ///
    /// <para><b>为什么用池而不是每次新建：</b>调试图形每帧都要重画，
    /// 每帧创建销毁几十个 GameObject 会产生持续的 GC 压力——恰好是在你最需要看清帧率的时候拖慢游戏。
    /// 池在上限内复用，超过上限时复用最后一条：宁可少画一条线，也不让对象无限增长。</para>
    ///
    /// <para>所有贴地图形都会被抬高一个很小的偏移，避免与地面共面产生闪烁。</para>
    /// </remarks>
    public sealed class AiDebugShapeRenderer : IDisposable
    {
        /// <summary>同时存在的图形上限。3 个 AI 大约用到 20 条，64 足够覆盖更密集的场景。</summary>
        private const int MaxShapes = 64;

        /// <summary>贴地图形的离地高度（米）。与弹道绘制的处理一致。</summary>
        private const float GroundHeight = 0.06f;

        /// <summary>
        /// 采样指定平面位置脚下的场景高度（米）。
        /// </summary>
        /// <remarks>
        /// 调试图形必须与单位实际站立面一致，否则站在装卸平台上的敌人，
        /// 视角锥会画在地面上，看图的人会误以为感知范围算错了。
        /// 用物理射线而不是导航网格采样，是为了让诊断层不依赖额外的运行时服务。
        /// </remarks>
        public static float SampleGroundHeight(float x, float z)
        {
            var origin = new Vector3(x, 50f, z);
            return Physics.Raycast(origin, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }

        private const float CircleWidth = 0.04f;
        private const float OutlineWidth = 0.05f;
        private const float LineWidth = 0.07f;

        /// <summary>圆的默认分段数。取 44 时半径 26 米的圆看起来已经足够平滑。</summary>
        private const int DefaultCircleSegments = 44;

        /// <summary>视野扇形的默认分段数。</summary>
        private const int DefaultFanSegments = 24;

        private readonly Transform m_Parent;
        private readonly List<LineRenderer> m_Pool = new List<LineRenderer>(MaxShapes);
        private Material m_Material;
        private int m_Used;

        /// <summary>创建绘制器。</summary>
        /// <param name="parent">所有调试线段的父节点，便于整体启停与清理。</param>
        public AiDebugShapeRenderer(Transform parent)
        {
            m_Parent = parent;
        }

        /// <summary>池中已创建的对象数量。用于观察"是否真的复用了"。</summary>
        public int PoolSize
        {
            get { return m_Pool.Count; }
        }

        /// <summary>本帧已使用的图形数量。</summary>
        public int ActiveCount
        {
            get { return m_Used; }
        }

        /// <summary>开始一帧：重置游标，不销毁任何对象。</summary>
        public void BeginFrame()
        {
            m_Used = 0;
        }

        /// <summary>结束一帧：把本帧没用到的线段关掉。</summary>
        public void EndFrame()
        {
            for (var i = m_Used; i < m_Pool.Count; i++)
            {
                if (m_Pool[i].gameObject.activeSelf)
                {
                    m_Pool[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>隐藏全部图形。</summary>
        public void HideAll()
        {
            m_Used = 0;
            EndFrame();
        }

        /// <summary>画一个贴地的圆（用于听觉半径）。</summary>
        /// <param name="center">圆心（贴地坐标，y 会被忽略）。</param>
        /// <param name="radius">半径（米）。非正值会被忽略。</param>
        /// <param name="color">颜色。</param>
        /// <param name="segments">分段数。</param>
        public void DrawCircle(Vector3 center, float radius, Color color, int segments = DefaultCircleSegments)
        {
            if (radius <= 0f)
            {
                return;
            }

            var count = Mathf.Max(8, segments);
            var line = Rent(count + 1, color, CircleWidth);
            var basePosition = new Vector3(center.x, center.y + GroundHeight, center.z);

            for (var i = 0; i <= count; i++)
            {
                var degrees = (360f / count) * i;
                line.SetPosition(i, basePosition + (DirectionFromDegrees(degrees) * radius));
            }
        }

        /// <summary>
        /// 画一个贴地的扇形轮廓（用于视野锥）：从圆心出发，沿弧线走一圈再回到圆心。
        /// </summary>
        /// <param name="center">扇形顶点（贴地坐标）。</param>
        /// <param name="centerDegrees">扇形的中心角度（度）。0 度指向世界 +X。</param>
        /// <param name="sweepDegrees">扇形张开角度（度）。</param>
        /// <param name="radius">半径（米）。</param>
        /// <param name="color">颜色。</param>
        /// <param name="segments">弧线分段数。</param>
        public void DrawFanOutline(
            Vector3 center,
            float centerDegrees,
            float sweepDegrees,
            float radius,
            Color color,
            int segments = DefaultFanSegments)
        {
            if (radius <= 0f || sweepDegrees <= 0f)
            {
                return;
            }

            var count = Mathf.Max(4, segments);
            var line = Rent(count + 3, color, OutlineWidth);
            var basePosition = new Vector3(center.x, center.y + GroundHeight, center.z);
            var startDegrees = centerDegrees - (sweepDegrees * 0.5f);

            line.SetPosition(0, basePosition);
            for (var i = 0; i <= count; i++)
            {
                var degrees = startDegrees + ((sweepDegrees / count) * i);
                line.SetPosition(i + 1, basePosition + (DirectionFromDegrees(degrees) * radius));
            }

            // 回到顶点，形成闭合轮廓。用"补一个重合点"而不是 LineRenderer.loop：
            // 池里的线段是共用的，改 loop 会污染下一条复用它画别的形状的线。
            line.SetPosition(count + 2, basePosition);
        }

        /// <summary>画一条贴地的直线（两个端点都会被抬到离地高度）。</summary>
        public void DrawLine(Vector3 from, Vector3 to, Color color)
        {
            var line = Rent(2, color, LineWidth);
            line.SetPosition(0, new Vector3(from.x, GroundHeight, from.z));
            line.SetPosition(1, new Vector3(to.x, GroundHeight, to.z));
        }

        /// <summary>画一条折线（用于巡逻路线与寻路路径）。</summary>
        /// <param name="points">顶点列表（贴地坐标）。少于 2 个点时不绘制。</param>
        /// <param name="loop">是否首尾相连。</param>
        /// <param name="color">颜色。</param>
        public void DrawPolyline(IReadOnlyList<Vector3> points, bool loop, Color color)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            var count = loop ? points.Count + 1 : points.Count;
            var line = Rent(count, color, OutlineWidth);

            for (var i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, new Vector3(points[i].x, GroundHeight, points[i].z));
            }

            if (loop)
            {
                line.SetPosition(points.Count, new Vector3(points[0].x, GroundHeight, points[0].z));
            }
        }

        /// <summary>释放全部线段与材质。</summary>
        public void Dispose()
        {
            for (var i = 0; i < m_Pool.Count; i++)
            {
                if (m_Pool[i] != null)
                {
                    UnityEngine.Object.Destroy(m_Pool[i].gameObject);
                }
            }

            m_Pool.Clear();
            m_Used = 0;

            if (m_Material != null)
            {
                UnityEngine.Object.Destroy(m_Material);
                m_Material = null;
            }
        }

        /// <summary>由一个角度得到水平方向向量。0 度指向 +X，90 度指向 +Z。</summary>
        private static Vector3 DirectionFromDegrees(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
        }

        /// <summary>取一条可用的线段并完成通用设置。</summary>
        private LineRenderer Rent(int positionCount, Color color, float width)
        {
            LineRenderer line;
            if (m_Used < m_Pool.Count)
            {
                line = m_Pool[m_Used];
            }
            else if (m_Pool.Count < MaxShapes)
            {
                line = CreateLine();
                m_Pool.Add(line);
            }
            else
            {
                // 超过上限：复用最后一条。宁可少画一条，也不要让对象数量随场景规模失控。
                line = m_Pool[m_Pool.Count - 1];
            }

            m_Used++;

            if (!line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(true);
            }

            line.positionCount = positionCount;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            return line;
        }

        /// <summary>创建一个新的线段对象。</summary>
        private LineRenderer CreateLine()
        {
            EnsureMaterial();

            var host = new GameObject("DebugShape");
            if (m_Parent != null)
            {
                host.transform.SetParent(m_Parent, worldPositionStays: false);
            }

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = m_Material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            host.SetActive(false);
            return line;
        }

        /// <summary>延迟创建共享材质。全部调试线共用一个材质，避免每个对象各生成一份实例。</summary>
        private void EnsureMaterial()
        {
            if (m_Material != null)
            {
                return;
            }

            var shader = Shader.Find("Sprites/Default");
            m_Material = shader != null ? new Material(shader) : null;
        }
    }
}
