namespace EOS.Server.Ui;

public sealed class UiState
{
    public DateOnly AsOf { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Toast { get; private set; }
    public bool ToastIsError { get; private set; }
    public event Action? Changed;
    public event Action? AsOfChanged;

    public void SetAsOf(DateOnly date) { AsOf = date; AsOfChanged?.Invoke(); Changed?.Invoke(); }
    private int _toastSeq;

    public void ShowToast(string message, bool error = false)
    {
        Toast = message; ToastIsError = error; Changed?.Invoke();
        var seq = Interlocked.Increment(ref _toastSeq);
        _ = Task.Delay(error ? 9000 : 5000).ContinueWith(_ => { if (seq == _toastSeq) ClearToast(); });
    }
    public void ClearToast() { Toast = null; Changed?.Invoke(); }
}
