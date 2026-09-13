using RaidDemo.Data;
using RaidDemo.Meta;
using TMPro;
using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 安全屋「图鉴展示板」上的进度文字。
    /// </summary>
    /// <remarks>
    /// <para>它挂在进度标签自身，由装配层在局外数据变化时调用 <see cref="Refresh"/>。
    /// 文本格式与图鉴界面标题共用 <see cref="CodexScreenController.CountDiscovered"/> 的统计，
    /// 两处永远显示同一个数字。</para>
    ///
    /// <para>之所以做成界面层的组件而不是让启动对象直接持有 TMP 引用：
    /// 启动层的程序集不引用 TextMeshPro，把"怎么显示"留在界面层可以保持这条依赖边界。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CodexBoardView : MonoBehaviour
    {
        /// <summary>显示收集进度的文字组件。由场景生成器写入。</summary>
        [SerializeField] private TextMeshProUGUI m_ProgressText;

        /// <summary>按最新进度重写牌面文字。</summary>
        /// <param name="catalog">物品目录；为空时总数为 0。</param>
        /// <param name="codex">收集进度；为空时已收集数为 0。</param>
        public void Refresh(ItemCatalog catalog, MetaCodex codex)
        {
            if (m_ProgressText == null)
            {
                return;
            }

            var discovered = CodexScreenController.CountDiscovered(catalog, codex);
            var total = catalog != null ? catalog.All.Count : 0;
            m_ProgressText.text = $"已收集 {discovered} / {total}";
        }
    }
}
