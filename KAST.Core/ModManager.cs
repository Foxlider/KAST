using KAST.Core.Models;

namespace KAST.Core
{
    public sealed class ModManager
    {
        private static readonly Lazy<ModManager> lazy = new Lazy<ModManager>(() => new ModManager());

        public static ModManager Instance => lazy.Value;

        private ModManager()
        {}

        public Mod AddMod(Mod mod)
        {
            using var c = new KastContext();
            if (mod.Exists())
                throw new KastDataDuplicateException($"Mod {mod.ModID} already exists");
            c.Mods.Add(mod);
            c.SaveChanges();
            return mod;
        }

        public Mod UpdateMod(Mod mod)
        {
            using var c = new KastContext();
            if (!mod.Exists())
                throw new KastDataNotFoundException($"Mod {mod.ModID} was not found");

            var newMod = c.Mods.FirstOrDefault(m => m.ModID == mod.ModID);
            newMod?.UpdateModInfos();
            c.SaveChanges();
            return newMod;
        }

        public Mod DeleteMod(Mod mod)
        {
            using var c = new KastContext();
            if(!mod.Exists())
                throw new KastDataNotFoundException($"Mod {mod.ModID} was not found");

            mod = c.Mods.Remove(mod).Entity;
            c.SaveChanges();
            return mod;
        }
    }
}
