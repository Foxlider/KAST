using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KAST.Core.Models;
using KAST.Desktop.Core.Contracts.Services;

namespace KAST.Desktop.Core.Services;

// This class holds sample data used by some generated pages to show how they can be used.
// TODO: The following classes have been created to display sample data. Delete these files once your app is using real data.
// 1. Contracts/Services/ISampleDataService.cs
// 2. Services/SampleDataService.cs
// 3. Models/SampleCompany.cs
// 4. Models/SampleOrder.cs
// 5. Models/SampleOrderDetail.cs
public class SampleDataService : ISampleDataService
{
    private List<Mod> _allOrders;

    public SampleDataService()
    {
    }


    private static IEnumerable<Mod> AllMods()
    {
        return new List<Mod>
        {
            new Mod()
            {
                Name = "ace",
                Author = new Author(){ AuthorID = 123456, Name="Keelah", URL="http://author"},
                Url = "http://steam/mods/ace",
                Path = "C://ace"
            },
            new Mod(1234)
            {
                Name = "tfar",
                Author = new Author(){ AuthorID = 123456, Name="Keelah", URL="http://author"},
                Url = "http://steam/mod/tfar",
                Path = "C://tfar"
            },
            new Mod()
            {
                Name = "yeet",
                Author = new Author(){ AuthorID = 123457, Name="Not Keelah", URL="http://author"},
                Url = "http://steam/mod/yeet",
                Path = "C://mod"
            },
        };
    }

    public async Task<IEnumerable<Mod>> GetGridDataAsync()
    {
        _allOrders ??= new List<Mod>(AllMods());

        await Task.CompletedTask;
        return _allOrders;
    }
}
