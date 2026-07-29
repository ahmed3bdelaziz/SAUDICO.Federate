using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SAUDICO.Federate.UI;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> executeAsync;
    private readonly Func<bool> canExecute;
    private bool isRunning;

    public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
    {
        this.executeAsync = executeAsync;
        this.canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool> canExecute)
        : this(() => { execute(); return Task.CompletedTask; }, canExecute)
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !isRunning && canExecute();

    public async void Execute(object? parameter)
    {
        if (isRunning)
        {
            return;
        }

        isRunning = true;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            await executeAsync();
        }
        finally
        {
            isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
