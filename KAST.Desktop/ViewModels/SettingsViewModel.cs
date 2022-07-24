using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KAST.Desktop.Contracts.Services;
using KAST.Desktop.Helpers;

using Microsoft.UI.Xaml;

namespace KAST.Desktop.ViewModels;

public class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private ElementTheme _elementTheme;

    public ElementTheme ElementTheme
    {
        get => _elementTheme;
        set => SetProperty(ref _elementTheme, value);
    }

    private string _versionDescription;

    public string VersionDescription
    {
        get => _versionDescription;
        set => SetProperty(ref _versionDescription, value);
    }

    private ICommand _switchThemeCommand;

    public ICommand SwitchThemeCommand
    {
        get
        {
            return _switchThemeCommand ??= new RelayCommand<ElementTheme>(
                async (param) =>
                {
                    if (ElementTheme == param)
                        return;
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                });
        }
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
        _elementTheme = _themeSelectorService.Theme;
        VersionDescription = GetVersionDescription();
    }

    private static string GetVersionDescription()
    {
        var appName = "AppDisplayName".GetLocalized();
        var version = Assembly.GetExecutingAssembly().GetName().Version;

        return $"{appName} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
