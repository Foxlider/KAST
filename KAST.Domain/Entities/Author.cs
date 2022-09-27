namespace KAST.Domain.Entities
{
    public class Author : AuditableEntity
    {
        public ulong AuthorID { get; internal set; }
        public string? Name { get; internal set; }
        public string? URL { get; internal set; }
    }
}
