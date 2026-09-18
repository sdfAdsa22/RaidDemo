using NUnit.Framework;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 医疗使用判据测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-21。联机客户端没有本地战斗世界，
    /// 旧实现读它恒失败、按满血返回，于是医疗请求被静默拦下（读条不出现、服务器零日志）。
    /// 这里钉住三档优先级：服务器权威镜像 &gt; 本地战斗世界 &gt; 未知按满血处理。</para>
    /// </remarks>
    [TestFixture]
    public sealed class MedicalUseRulesTests
    {
        private const float MaxHealth = 100f;

        /// <summary>联机：有服务器镜像且不满血时，缺口按镜像算。</summary>
        [Test]
        public void 服务器权威镜像低于上限时给出缺口()
        {
            var missing = MedicalUseRules.MissingHealth(MaxHealth, true, 60f, false, 0f);
            Assert.AreEqual(40f, missing, 1e-4f);
        }

        /// <summary>联机：镜像满血时缺口为零（不能无意义地消耗医疗品）。</summary>
        [Test]
        public void 服务器权威镜像满血时缺口为零()
        {
            var missing = MedicalUseRules.MissingHealth(MaxHealth, true, MaxHealth, false, 0f);
            Assert.AreEqual(0f, missing);
        }

        /// <summary>单机：没有权威镜像时退回本地战斗世界。</summary>
        [Test]
        public void 没有权威镜像时退回本地战斗世界()
        {
            var missing = MedicalUseRules.MissingHealth(MaxHealth, false, 0f, true, 25f);
            Assert.AreEqual(75f, missing, 1e-4f);
        }

        /// <summary>两份数据都没有：按满血处理，宁可不消耗物品也不凭空回血。</summary>
        [Test]
        public void 两份数据都没有时按满血处理不消耗物品()
        {
            var missing = MedicalUseRules.MissingHealth(MaxHealth, false, 0f, false, 0f);
            Assert.AreEqual(0f, missing);
        }

        /// <summary>联机镜像存在时优先于本地旧状态（防止本地残留值把请求拦下）。</summary>
        [Test]
        public void 权威镜像优先于本地战斗世界()
        {
            var missing = MedicalUseRules.MissingHealth(MaxHealth, true, 90f, true, 10f);
            Assert.AreEqual(10f, missing, 1e-4f);
        }
    }
}
