﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KAST.Core.Models
{
    public class Settings
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
        { get { return armaPath; } set { armaPath = value; } }

        public string? ModStagingDir
        { get { return modStagingDir; } set { modStagingDir = value; } }

        public bool UsingContactDlc
        { get { return usingContactDlc; } set { usingContactDlc = value; } }

        public bool UsingGmDlc
        { get { return usingGmDlc; } set { usingGmDlc = value; } }

        public bool UsingPfDlc
        { get { return usingPfDlc; } set { usingPfDlc = value; } }

        public bool UsingClsaDlc
        { get { return usingClsaDlc; } set { usingClsaDlc = value; } }

        public bool UseWsDlc
        { get { return usingWsDlc; } set { usingWsDlc = value; } }

        public string? ApiKey
        { get { return apiKey; } set { apiKey = value; } }

        public int? CliWorkers 
        { get { return cliWorkers; } set { cliWorkers = value; } }

    }
}
