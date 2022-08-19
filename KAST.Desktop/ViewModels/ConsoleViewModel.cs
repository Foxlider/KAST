using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace KAST.Desktop.ViewModels;

public class ConsoleViewModel : ObservableRecipient
{
    private string consoleOutput;
    private string path;
    private uint progress;
    private uint maxProgress;
    private bool isIndeterminate;
    private bool isError;

    public string ConsoleOutput
    {
        get { return consoleOutput; }
        set => SetProperty(ref consoleOutput, value);
    }

    public string Path
    {
        get => path;
        set => SetProperty(ref path, value);
    }

    public uint Progress
    {
        get => progress;
        set => SetProperty(ref progress, value);
    }

    public uint MaxProgress
    {
        get => maxProgress;
        set => SetProperty(ref maxProgress, value);
    }

    public bool IsIndeterminate
    {
        get => isIndeterminate;
        set => SetProperty(ref isIndeterminate, value);
    }

    public bool IsError
    {
        get => isError;
        set => SetProperty(ref isError, value);
    }

    public void ProgressStartIndeterminate()
    {
        IsError = false;
        IsIndeterminate = true; 
    }

    public void ProgressStop()
    {
        IsIndeterminate = false;
        Progress = 0;
        MaxProgress = 100;
    }

    public void ProgressSetMaxProgress(uint max)
    { MaxProgress = max; }

    public void ProgressSet(uint progress)
    {
        IsError = false; ;
        Progress = progress;
    }

    public void ProgressError()
    { IsError = true; }

    public ConsoleViewModel()
    {
        ConsoleOutput = "Started...\n";
    }

}
