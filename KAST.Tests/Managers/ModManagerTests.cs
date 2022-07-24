using KAST.Core.Managers;
using KAST.Core.Models;

using Microsoft.EntityFrameworkCore;

using System;

using Xunit;

namespace KAST.Tests.Managers
{
    public class ModManagerTests : IDisposable
    {
        private KastContextFactory factory;
        private KastContext context => factory.CreateContext();
        private ModManager Manager => new(context);

        public ModManagerTests()
        {
            factory = new KastContextFactory();
            Assert.Contains(":memory:", context.Database.GetConnectionString());
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            factory.Dispose();
        }


        [Fact]
        public void AddModTest()
        {
            var m = new Mod(1);
            Assert.Equal(m, Manager.AddMod(m));

            Assert.Throws<KastDataDuplicateException>(() => Manager.AddMod(new Mod(1)));
        }


        [Fact]
        public void GetModsTests()
        {
            Assert.Single(Manager.GetMods());
            Manager.AddMod(new Mod(1));

            Assert.NotEmpty(Manager.GetMods());
            Assert.Equal(2, Manager.GetMods().Count);

            Manager.DeleteMod(1);
            Assert.Single(Manager.GetMods());
        }


        [Fact()]
        public void ModExistsTest()
        {
            Assert.True(Manager.ModExists(463939058));
            Assert.False(Manager.ModExists(2));

            Assert.True(Manager.ModExists(new Mod(463939058)));
            Assert.False(Manager.ModExists(new Mod(1)));
        }

        [Fact()]
        public void GetModTest()
        {
            Assert.NotNull(Manager.GetMod(463939058));
            Assert.Throws<KastDataNotFoundException>(() => Manager.GetMod(1));
        }

        [Fact()]
        public void GetObservableModsTest()
        {
            Assert.Single(Manager.GetObservableMods());
            Manager.AddMod(new Mod(1));

            Assert.NotEmpty(Manager.GetMods());
            Assert.Equal(2, Manager.GetObservableMods().Count);

            Manager.DeleteMod(1);
            Assert.Single(Manager.GetObservableMods());
        }

        [Fact()]
        public void DeleteModIDTest()
        {
            Assert.Throws<KastDataNotFoundException>(() => Manager.DeleteMod(1));
            Assert.NotNull(Manager.DeleteMod(463939058));
        }

        [Fact()]
        public void DeleteModObjTest()
        {
            Assert.Throws<KastDataNotFoundException>(() => Manager.DeleteMod(new Mod(1)));
            Assert.NotNull(Manager.DeleteMod(new Mod(463939058)));
        }
    }
}