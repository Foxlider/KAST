using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using KAST.Core.Models;
using KAST.Desktop.Contracts.ViewModels;
using KAST.Desktop.Core.Contracts.Services;

namespace KAST.Desktop.ViewModels;

public class ModsViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISampleDataService _sampleDataService;

    public ObservableCollection<Mod> Source { get; } = new ObservableCollection<Mod>();

    public ModsViewModel(ISampleDataService sampleDataService)
    {
        _sampleDataService = sampleDataService;
    }

    public async void OnNavigatedTo(object parameter)
    {
        Source.Clear();

        // TODO: Replace with real data.
        var data = await _sampleDataService.GetGridDataAsync();

        foreach (var item in data)
        {
            Source.Add(item);
        }
    }

    public void OnNavigatedFrom()
    {
    }
}
