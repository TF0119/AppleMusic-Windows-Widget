using System.Windows.Input;

namespace AppleMusicWidget.UI;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _run;
    private readonly Func<bool>? _can;

    public RelayCommand(Func<Task> run, Func<bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        try { await _run(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RelayCommand] {ex.Message}"); }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
