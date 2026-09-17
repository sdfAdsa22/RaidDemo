using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Inventory;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 装备命令落点路由的回归用例（U-100）。
    /// </summary>
    /// <remarks>
    /// 实机症状：安全屋里把 AK 装备好（界面与镜像都正常），一进图却空手——
    /// 因为安全屋的装备命令落在了"战斗单位的临时默认套"上，而进图配发读的是账号档案。
    /// 这组用例把"哪一份 loadout 归命令 / 镜像用"钉死。
    /// </remarks>
    [TestFixture]
    public sealed class ServerLoadoutRoutingTests
    {
        private static PlayerLoadout MakeLoadout()
        {
            return new PlayerLoadout(new InventoryGrid(4, 4), new EquipmentLoadout());
        }

        /// <summary>安全屋：即使战斗单位那份存在，也必须用账号档案那一份。</summary>
        [Test]
        public void 安全屋一律用账号档案那份()
        {
            var profile = MakeLoadout();
            var combat = MakeLoadout();

            var resolved = ServerLoadoutRouting.ResolveCommandLoadout(
                ServerWorldKind.SafeHouse, profile, combat);

            Assert.AreSame(
                profile,
                resolved,
                "安全屋命令改到临时默认套上，装备就不会进档案、进图必然空手（U-100）。");
        }

        /// <summary>战局：用战斗单位那一份（它与账号档案本来就是同一个对象）。</summary>
        [Test]
        public void 战局用战斗单位那份()
        {
            var profile = MakeLoadout();
            var combat = MakeLoadout();

            var resolved = ServerLoadoutRouting.ResolveCommandLoadout(
                ServerWorldKind.Raid, profile, combat);

            Assert.AreSame(combat, resolved);
        }

        /// <summary>档案缺失时退回战斗单位那份：命令通道仍要能建立。</summary>
        [Test]
        public void 档案缺失时退回战斗单位那份()
        {
            var combat = MakeLoadout();

            var resolved = ServerLoadoutRouting.ResolveCommandLoadout(
                ServerWorldKind.SafeHouse, null, combat);

            Assert.AreSame(combat, resolved);
        }

        /// <summary>战斗单位缺失时退回档案那份。</summary>
        [Test]
        public void 战斗单位缺失时退回档案那份()
        {
            var profile = MakeLoadout();

            var resolved = ServerLoadoutRouting.ResolveCommandLoadout(
                ServerWorldKind.Raid, profile, null);

            Assert.AreSame(profile, resolved);
        }
    }
}
