using KAST.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Data.Interfaces
{
    public interface IConfigService
    {
        Task<KastSettings> GetConfigAsync();
        Task UpdateConfigAsync(KastSettings config);
    }
}
