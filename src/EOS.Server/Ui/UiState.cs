namespace EOS.Server.Ui;

public sealed class UiState
{
    public DateOnly AsOf { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Toast { get; private set; }
    public bool ToastIsError { get; private set; }
    public event Action? Changed;
    public event Action? AsOfChanged;

    public void SetAsOf(DateOnly date) { AsOf = date; AsOfChanged?.Invoke(); Changed?.Invoke(); }
    public void ShowToast(string message, bool error = false) { Toast = message; ToastIsError = error; Changed?.Invoke(); }
    public void ClearToast() { Toast = null; Changed?.Invoke(); }
}
