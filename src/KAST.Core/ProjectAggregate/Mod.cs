using KAST.SharedKernel;
using KAST.SharedKernel.Interfaces;

namespace KAST.Core.ProjectAggregate
{
    internal class Mod : EntityBase, IAggregateRoot
    {
        public ulong ID { get; private set; }
        public string Name { get; private set; }
        public string Url { get; set; }
        //public Author Author { get; set; }
        public string Path { get; set; }
        public DateTime SteamLastUpdated { get; set; }
        public DateTime LocalLastUpdated { get; set; }
    }
}
