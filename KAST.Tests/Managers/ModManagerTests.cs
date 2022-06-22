using Xunit;
using KAST.Core.Managers;
using KAST.Core.Models;
using System.IO;
using System;

namespace KAST.Core.Managers.Tests
{
    public class ModManagerTests : IDisposable
    {
        private readonly string tempDbPath = Path.Combine("./", "KAST.db");
        const string templateDbPath = "./template.db";
        readonly ModManager Manager;

        public ModManagerTests()
        {
            //Set up db file
            if (File.Exists(tempDbPath))
                File.Delete(tempDbPath);

            File.Copy(templateDbPath, tempDbPath);

            //Change DB path for context
            KastContext.Instance.ChangeDbPath(Path.GetDirectoryName(tempDbPath));
            KastContext.Instance.Database.EnsureCreated();

            //Get the manager
            Manager = ModManager.Instance;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ModManagerTests()
        { Dispose(false); }

        protected virtual void Dispose(bool disposing)
        {
            //Closing DB
            Console.WriteLine("Closing DB...");
            if (KastContext.Instance.Database.CurrentTransaction != null)
                KastContext.Instance.Database.RollbackTransaction();
            KastContext.Instance.ChangeTracker.Clear();
            KastContext.Instance.Database.EnsureCreated();
            KastContext.Instance.Database.EnsureDeleted();
            Console.WriteLine("Done...");



            //Deleting temp file
            Console.WriteLine("Deleting DB File...");
            Utilities.WrapSharingViolations(() => File.Delete(tempDbPath));
            
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