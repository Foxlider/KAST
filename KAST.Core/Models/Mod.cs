namespace KAST.Core.Models
{
    public class Mod : BaseObject
    {
        /// <summary>
        /// Constructor for mods with an ID (Steam Workshop mods)
        /// </summary>
        /// <param name="id"></param>
        public Mod(ulong id)
        {
            ModID = id;
            ModStatus = ArmaModStatus.Unknown;
        }


        /// <summary>
        /// Constructor for Local Mods.
        /// </summary>
        /// <remarks>
        /// The <see cref="ModID">mod's ID</see> is automatically calculated from a random number between <c><see cref="ulong.MaxValue"/></c> and <c><see cref="ulong.MaxValue"/> - 65535</c>
        /// </remarks>
        public Mod()
        {
            ModID = ulong.MaxValue - (ulong)Random.Shared.Next(ushort.MaxValue);
            ModStatus = ArmaModStatus.Local;
        }
        

        private ulong       _ID;
        private string?     name;
        private string?     url;
        private string?     path;
        private DateTime?   steamLastUpdated;
        private DateTime?   localLastUpdated;
        private string?     status;
        private ulong?      expectedSize;
        private ulong?      actualSize;
        private bool        isLoading;

        /// <summary>
        /// The mod's ID
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     If the Mod is a Workshop mod, the mod ID will be the mod's ID on the workshop
        ///     </para>
        ///     <para>
        ///     If the mod is a Local mod, the ID will be randomly generated
        ///     </para>
        /// </remarks>
        public ulong ModID
        {
            get => _ID;
            private init { _ID = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// THe mod's name on the Workshop
        /// </summary>
        public string? Name
        {
            get => name;
            set { name = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Steam Workshop of the Mod
        /// </summary>
        public string? Url
        {
            get => url;
            set { url = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Author of the mod
        /// </summary>
        public virtual Author? Author
        {
            get;
            set;
        }

        /// <summary>
        /// Current path of the Mod
        /// </summary>
        public string? Path
        {
            get => path;
            set { path = value; OnPropertyChanged(); }
        }


        /// <summary>
        /// When was the mod updater on the Steam Workshop
        /// </summary>
        public DateTime? SteamLastUpdated
        {
            get => steamLastUpdated;
            internal set { steamLastUpdated = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When was the mod updated on the server
        /// </summary>
        public DateTime? LocalLastUpdated
        {
            get => localLastUpdated;
            internal set { localLastUpdated = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Checks if the mod is local or not
        /// </summary>
        public bool IsLocal
        { 
            get => ModStatus == ArmaModStatus.Local;
        }

        /// <summary>
        /// Current status of the mod
        /// </summary>
        public string? ModStatus
        { 
            get => status;
            internal set { status = value; OnPropertyChanged(); } 
        }

        /// <summary>
        /// Expected size of the mod on the Disk (Filled from Steam Workshop)
        /// </summary>
        public ulong? ExpectedSize
        { 
            get => expectedSize;
            internal set { expectedSize = value; OnPropertyChanged(); } 
        }

        /// <summary>
        /// Actual size of the mod on the Disk
        /// </summary>
        public ulong? ActualSize
        { 
            get => actualSize;
            set { actualSize = value; OnPropertyChanged(); } 
        }



        public bool IsLoading
        {
            get => isLoading;
            set { isLoading = value; OnPropertyChanged(); }
        }
    }
}
