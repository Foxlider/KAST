namespace KAST.Data.Models
{
    public class ServerProfile
    {
        public const string FILENAME = "Arma3Profile";
        public CustomDifficulty CustomDifficulty { get; set; } = new();
        public CustomAILevel CustomAILevel { get; set; } = new();
    }
    public class CustomDifficulty
    {
        public Options Options { get; set; } = new();
        public int aiLevelPreset { get; set; } = 3;
    }

    public class Options
    {
        public int reducedDamage { get; set; }
        public int groupIndicators { get; set; }
        public int friendlyTags { get; set; }
        public int enemyTags { get; set; }
        public int detectedMines { get; set; }
        public int commands { get; set; }
        public int waypoints { get; set; }
        public int tacticalPing { get; set; }
        public int weaponInfo { get; set; }
        public int stanceIndicator { get; set; }
        public int staminaBar { get; set; }
        public int weaponCrosshair { get; set; }
        public int visionAid { get; set; }
        public int thirdPersonView { get; set; }
        public int cameraShake { get; set; }
        public int scoreTable { get; set; }
        public int deathMessages { get; set; }
        public int vonID { get; set; }
        public int mapContent { get; set; }
        public int autoReport { get; set; }
        public int multipleSaves { get; set; }
    }

    public class CustomAILevel
    {
        public float skillAI { get; set; }
        public float precisionAI { get; set; }
    }
}
