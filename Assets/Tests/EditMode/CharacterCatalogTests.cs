using System.Collections.Generic;
using UI.Presenter;
using NUnit.Framework;

namespace Dynasty.Tests
{
    /// <summary>16 角色 Def 目录：id 唯一性、工厂可用性、技能例外与名称顺序。</summary>
    public class CharacterCatalogTests
    {
        [Test]
        public void Catalog_HasSixteenDistinctIds()
        {
            int[] ids = CharacterCatalog.GetAllIds();

            Assert.AreEqual(16, ids.Length);
            Assert.AreEqual(16, new HashSet<int>(ids).Count);
        }

        [Test]
        public void Catalog_EveryId_CreatesNamedDef()
        {
            foreach (int id in CharacterCatalog.GetAllIds())
            {
                var def = CharacterCatalog.Create(id);

                Assert.IsNotNull(def, $"id={id} Create 返回 null");
                Assert.AreEqual(id, def.Id);
                Assert.IsNotEmpty(def.Name, $"id={id} Name 为空");
            }
        }

        [Test]
        public void Catalog_NormalPersonHasNoSkills_OthersHaveAtLeastOne()
        {
            foreach (int id in CharacterCatalog.GetAllIds())
            {
                var def = CharacterCatalog.Create(id);

                if (id == 3)
                    Assert.IsEmpty(def.Skills); // 普通人：Array.Empty（空数组而非 null）
                else
                    Assert.IsNotEmpty(def.Skills, $"id={id} 应至少 1 个技能");
            }
        }

        [Test]
        public void Catalog_GetNames_MatchesIdOrder()
        {
            string[] names = CharacterCatalog.GetNames();

            Assert.AreEqual(16, names.Length);
            Assert.AreEqual("守护者", names[0]);
            Assert.AreEqual("普通人", names[2]);
            Assert.AreEqual("混沌法师", names[13]);
            Assert.AreEqual("双子", names[14]);
            Assert.AreEqual("数据幽灵", names[15]);
        }
    }
}
