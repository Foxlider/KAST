namespace KAST.Core.Models
{
    public class Settings : BaseObject
    {
        private string? armaPath;
        private string? modStagingDir;
        private bool usingContactDlc;
        private bool usingGmDlc;
        private bool usingPfDlc;
        private bool usingClsaDlc;
        private bool usingWsDlc;
        private string? apiKey;
        private int? cliWorkers;

        
        public int id { get; set; }

        public string? ArmaPath
        { get { return armaPath; } set { armaPath = value; OnPropertyChanged(); } }

        public string? ModStagingDir
        { get { return modStagingDir; } set { modStagingDir = value; OnPropertyChanged(); } }

        public bool UsingContactDlc
        { get { return usingContactDlc; } set { usingContactDlc = value; OnPropertyChanged(); } }

        public bool UsingGmDlc
        { get { return usingGmDlc; } set { usingGmDlc = value; OnPropertyChanged(); } }

        public bool UsingPfDlc
        { get { return usingPfDlc; } set { usingPfDlc = value; OnPropertyChanged(); } }

        public bool UsingClsaDlc
        { get { return usingClsaDlc; } set { usingClsaDlc = value; OnPropertyChanged(); } }

        public bool UseWsDlc
        { get { return usingWsDlc; } set { usingWsDlc = value; OnPropertyChanged(); } }

        public string? ApiKey
        { get { return apiKey; } set { apiKey = value; OnPropertyChanged(); } }

        public int? CliWorkers 
        { get { return cliWorkers; } set { cliWorkers = value; OnPropertyChanged(); } }

    }
}
