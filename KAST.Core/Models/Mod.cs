using SteamKit2.Internal;

namespace KAST.Core.Models
{
    public class Mod
    {
        public Mod(ulong id)
        {
            ModID = id;
        }


        public Mod()
        {
            ModID = ulong.MaxValue - (ulong)Random.Shared.Next(ushort.MaxValue);
        }
        

        private ulong       _ID;
        private string?     name;
        private string?     url;
        private string?     path;
        private DateTime?   steamLastUpdated;
        private DateTime?   localLastUpdated;
        private bool?       isLocal;
        private string?     status;
        private ulong?       expectedSize;
        private ulong?       actualSize;

        public ulong ModID
        {
            get { return _ID; }
            set { _ID = value; }
        }

        public string? Name
        {
            get { return name; }
            set { name = value; }
        }

        public string? Url
        {
            get { return url; }
            set { url = value; }
        }

        public virtual Author? Author
        {
            get;
            set;
        }

        public string? Path
        {
            get { return path; }
            set { path = value; }
        }

        public DateTime? SteamLastUpdated
        {
            get { return steamLastUpdated; }
            set { steamLastUpdated = value; }
        }

        public DateTime? LocalLastUpdated
        {
            get { return localLastUpdated; }
            set { localLastUpdated = value; }
        }

        public bool? IsLocal
        { get { return isLocal; } set { isLocal = value; } }

        public string? ModStatus
        { get { return status; } set { status = value; } }

        public ulong? ExpectedSize
        { get { return expectedSize; } set { expectedSize = value; } }

        public ulong? ActualSize
        { get { return actualSize; } set { actualSize = value; } }




        public async void UpdateModInfos()
        {
            Updater updater = new();
            PublishedFileDetails res;
            try
            {
                res = await updater.GetModInfo(ModID);
            }
            catch (KastLogonFailedException)
            { Console.WriteLine("Logon Failed"); return; }
            

            Name = res.title;
            SteamLastUpdated = Utilities.UnixTimeStampToDateTime(res.time_updated);
            ExpectedSize = res.file_size;
        }
    }
}
