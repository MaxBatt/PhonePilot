namespace PhonePilot.Desktop;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception> _onError;
    private bool _running;
    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute, Action<Exception> onError)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), onError) { }
    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute, Action<Exception> onError)
    { _execute = execute; _canExecute = canExecute; _onError = onError; }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true; CommandManager.InvalidateRequerySuggested();
        try { await _execute(parameter); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _onError(ex); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}

public sealed record Choice(string Key, string Label);
