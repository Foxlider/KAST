using System.Collections.Generic;
using System.Threading.Tasks;
using KAST.Core.Models;

namespace KAST.Desktop.Core.Contracts.Services;

// Remove this class once your pages/features are using your data.
public interface ISampleDataService
{
    Task<IEnumerable<Mod>> GetGridDataAsync();
}
