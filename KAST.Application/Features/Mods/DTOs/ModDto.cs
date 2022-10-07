namespace KAST.Application.Features.Mods.DTOs
{
    public partial class ModDto : IMapFrom<Mod>
    {
        public void Mapping(Profile profile)
        {
            profile.CreateMap<Mod, ModDto>(MemberList.None)
               .ForMember(x => x.AuthorName, s => s.MapFrom(y => y.Author!.Name));
            profile.CreateMap<ModDto, Mod>(MemberList.None)
               .ForMember(x => x.Author, s => s.Ignore());
        }
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public string? URL { get; set; }
        public DocumentType DocumentType { get; set; } = DocumentType.Document;
        public string? AuthorId { get; set; }
        public string? AuthorName { get; set; }
    }
}
