namespace KAST.Core.Models
{
    public class User
    {
        public Guid Id { get; set; }

        public string Login { get; set; }
        public string Pass { get; set; }
        public string Name { get; set; }
    }
}
