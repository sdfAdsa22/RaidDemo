namespace RaidDemo.HotUpdate
{
    /// <summary>
    /// 运行期会按地址加载的资产地址（与编辑器入组工具生成的规则一一对应）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把它们集中成常量：</b>地址是"代码与内容之间唯一的契约"，
    /// 一旦发布就不能悄悄改（改了必须同步改代码，等于一次强制发版）。
    /// 集中放一处，配合入组工具的派生规则（<c>&lt;前缀&gt;/&lt;相对路径小写&gt;</c>），
    /// 任何人改动时都能立刻看到"这个字符串是被代码依赖的"。</para>
    ///
    /// <para>命名规则：<c>data/&lt;文件名小写&gt;</c>（见 <c>M10AddressablesSetup.BuildAddress</c>）。</para>
    /// </remarks>
    public static class HotUpdateAddresses
    {
        /// <summary>物品目录（物品定义、武器参数、掉落表的总入口）。</summary>
        public const string ItemCatalog = "data/itemcatalog";

        /// <summary>表现层目录（武器模型、音效、角色预制体索引）。</summary>
        public const string PresentationCatalog = "data/presentationcatalog";

        /// <summary>音频目录。</summary>
        public const string AudioCatalog = "data/audiocatalog";
    }
}
