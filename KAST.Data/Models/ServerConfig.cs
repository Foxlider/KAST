namespace KAST.Data.Models
{
    public class ServerConfig
    {
        public string Hostname { get; set; } = "My Arma 3 Server";
        public string Password { get; set; } = "";
        public string PasswordAdmin { get; set; } = "adminpass";
        public int MaxPlayers { get; set; } = 40;
        public bool Persistent { get; set; } = true;
        public string TimeStampFormat { get; set; } = "short";
        public bool BattlEye { get; set; } = true;
    }
}