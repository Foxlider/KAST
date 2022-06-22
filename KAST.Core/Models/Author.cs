namespace KAST.Core.Models
{
    public class Author : BaseObject
    {
        public ulong AuthorID { get; internal set; }
        public string Name { get; internal set; }
        public string URL { get; internal set; }
    }
}
