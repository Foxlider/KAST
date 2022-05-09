namespace KAST.Core.Models
{
    public class User
    {
        private Guid _id;
        private string _login;
        private string _name;
        private string _pass;

        public Guid Id
        { get { return _id; } set { _id = value; } }

        public string Login { get { return _login; } set { _login = value; } }
        public string Pass { get { return _pass; } set { _pass = value; } }
        public string Name { get { return _name; } set { _name = value; } }
    }
}
