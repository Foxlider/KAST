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
        { 
            get => armaPath;
            set { armaPath = value; OnPropertyChanged(); }
        }

        public string? ModStagingDir
        { 
            get => modStagingDir;
            set { modStagingDir = value; OnPropertyChanged(); }
        }

        public bool UsingContactDlc
        {
            get => usingContactDlc;
            set { usingContactDlc = value; OnPropertyChanged(); }
        }

        public bool UsingGmDlc
        {
            get => usingGmDlc;
            set { usingGmDlc = value; OnPropertyChanged(); }
        }

        public bool UsingPfDlc
        {
            get => usingPfDlc;
            set { usingPfDlc = value; OnPropertyChanged(); }
        }

        public bool UsingClsaDlc
        {
            get => usingClsaDlc;
            set { usingClsaDlc = value; OnPropertyChanged(); }
        }

        public bool UseWsDlc
        {
            get => usingWsDlc;
            set { usingWsDlc = value; OnPropertyChanged(); }
        }

        public string? ApiKey
        {
            get => apiKey;
            set { apiKey = value; OnPropertyChanged(); }
        }

        public int? CliWorkers
        {
            get => cliWorkers;
            set { cliWorkers = value; OnPropertyChanged(); }
        }

    }
}
