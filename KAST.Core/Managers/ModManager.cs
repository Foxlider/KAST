using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace KAST.Core.Managers
{
    public sealed class ModManager
    {
        private readonly KastContext context;

        public KastContext Context => context != null ? context : new KastContext();

        public ModManager()
        { }

        public ModManager(KastContext context)
        { this.context = context; }

        /// <summary>
        /// Return a Mod from its ID
        /// </summary>
        /// <param name="ID">A mod's ID</param>
        /// <returns>The Mod</returns>
        /// <exception cref="KastDataNotFoundException">Thrown if the mod does not exist in the Database</exception>
        public Mod GetMod(ulong ID)
        {
            var c = Context;
            var mod = c.Mods.FirstOrDefault(m => m.ModID == ID);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            return mod;
        }

        /// <summary>
        /// Adds a mod in the Database
        /// </summary>
        /// <param name="mod">The mod to be added</param>
        /// <returns>Returns the mod's Database Instance</returns>
        /// <exception cref="KastDataDuplicateException">Thrown when the mod already exists in the Database</exception>
        public Mod AddMod(Mod mod)
        {
            var c = Context;

            if (ModExists(mod))
                throw new KastDataDuplicateException($"Mod {mod.ModID} already exists");

            mod = c.Mods.Add(mod).Entity;
            c.SaveChanges();

            return mod;
        }

        /// <summary>
        /// Checks if a mod exists or not in the Database
        /// </summary>
        /// <param name="mod">The mod to check in the database</param>
        /// <returns></returns>
        public bool ModExists(Mod mod)
        { return ModExists(mod.ModID); }

        /// <summary>
        /// Checks if a mod exists or not in the Database
        /// </summary>
        /// <param name="id">The mod's ID to check in the Database</param>
        /// <returns></returns>
        public bool ModExists(ulong id)
        {
            var c = Context;

            return c.Mods.Any(m => m.ModID == id); }


        /// <summary>
        /// Update a mod's informations according to the Steam Workshop
        /// </summary>
        /// <param name="mod">The mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod was not found in the Database</exception>
        public Mod UpdateModInfos(Mod? mod)
        {
            var c = Context;

            if (mod == null)
                throw new ArgumentNullException(nameof(mod));

            if (!ModExists(mod))
                throw new KastDataNotFoundException(typeof(Mod));

            //UpdaterLogic Here

            c.SaveChanges();

            return mod;
        }

        /// <summary>
        /// Update a mod's informations according to the Steam Workshop
        /// </summary>
        /// <param name="id">The ID of the mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod was not found in the Database</exception>
        public Mod UpdateModInfos(ulong id)
        {
            var c = Context;
            var mod = c.Mods.FirstOrDefault(m => m.ModID == id);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            return UpdateModInfos(mod);
        }

        /// <summary>
        /// Async version of <see cref="UpdateModInfos(Mod?)"/>
        /// Update a mod's informations according to the Steam Workshop
        /// </summary>
        /// <param name="mod">The mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown whe nthe mod was not found in the Database</exception>
        public async Task<Mod> UpdateModInfosAsync(Mod? mod)
        {
            var c = Context;

            if(mod == null)
                throw new ArgumentNullException(nameof(mod));

            mod = GetMod(mod.ModID);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            //UpdaterLogic here

            await c.SaveChangesAsync();

            return mod;
        }

        /// <summary>
        /// Async version of <see cref="UpdateModInfos(ulong?)"/>
        /// Update a mod's informations according to the Steam Workshop
        /// </summary>
        /// <param name="id">The ID of the mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod was not found in the Database</exception>
        public async Task<Mod> UpdateModInfosAsync(ulong id)
        {
            var c = Context;
            var mod = c.Mods.FirstOrDefault(m => m.ModID == id);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            return await UpdateModInfosAsync(mod);
        }

        /// <summary>
        /// Launches the Update process for a mod
        /// </summary>
        /// <param name="mod">The Mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod was not found in the Database</exception>
        public Mod UpdateMod(Mod? mod)
        {
            var c = Context;

            if (mod == null)
                throw new ArgumentNullException(nameof(mod));

            mod = GetMod(mod.ModID);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            
            //UpdaterLogic here

            c.SaveChanges();

            return mod;
        }

        /// <summary>
        /// Async version of <see cref="UpdateMod(Mod?)"/>
        /// Launches the Update process for a mod
        /// </summary>
        /// <param name="mod">The Mod to update</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod was not found in the Database</exception>
        public async Task<Mod> UpdateModAsync(Mod? mod)
        {
            var c = Context;

            if (mod == null)
                throw new ArgumentNullException(nameof(mod));

            mod = GetMod(mod.ModID);

            if (mod == null)
                throw new KastDataNotFoundException(typeof(Mod));

            //UpdaterLogic here

            await c.SaveChangesAsync();

            return mod;
        }

        /// <summary>
        /// Delete a mod in the Database
        /// </summary>
        /// <param name="mod">The mod to delete</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod is not found in the Database</exception>
        public Mod DeleteMod(Mod mod)
        {
            var c = Context;
            if (!ModExists(mod.ModID))
                throw new KastDataNotFoundException($"Mod {mod.ModID} was not found");

            mod = c.Mods.Remove(mod).Entity;
            c.SaveChanges();
            return mod;
        }

        /// <summary>
        /// Delete a mod in the Database
        /// </summary>
        /// <param name="mod">The ID of the mod to delete</param>
        /// <returns></returns>
        /// <exception cref="KastDataNotFoundException">Thrown when the mod is not found in the Database</exception>
        public Mod DeleteMod(ulong id)
        {
            var c = Context;
            Mod mod = GetMod(id);

            mod = c.Mods.Remove(mod).Entity;
            c.SaveChanges();
            return mod;
        }

        /// <summary>
        /// Get the current list of Mods
        /// </summary>
        /// <returns>A List of Mods</returns>
        public List<Mod> GetMods()
        {
            var c = Context;
            return c.Mods.ToList();
        }

        /// <summary>
        /// Get the current list of mods as an Observable Collection
        /// </summary>
        /// <returns>An ObservableCollection of Mods</returns>
        public ObservableCollection<Mod> GetObservableMods()
        {
            var c = Context;
            c.Mods.Load();
            return c.Mods.Local.ToObservableCollection();
        }
    }
}
