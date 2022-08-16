using CommunityToolkit.Mvvm.ComponentModel;

namespace KAST.Desktop.ViewModels;

public class ConsoleViewModel : ObservableRecipient
{
    private string consoleOutput;

    public string ConsoleOutput
    {
        get { return consoleOutput; }
        set { consoleOutput = value; OnPropertyChanged(nameof(ConsoleOutput)); }
    }

    public ConsoleViewModel()
    {
        ConsoleOutput = "Started...";
    }

}
