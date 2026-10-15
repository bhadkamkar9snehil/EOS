using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Skills;
using EOS.Infrastructure.Training;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace EOS.Server.Ui;

public abstract class EosPage : ComponentBase, IDisposable
{
    [Inject] protected IServiceScopeFactory Scopes { get; set; } = null!;
    [Inject] protected UiState State { get; set; } = null!;
    [Inject] protected AuthenticationStateProvider Auth { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    [Inject] protected IJSRuntime JS { get; set; } = null!;

    protected string Actor { get; private set; } = "unknown";
    protected DateOnly AsOf => State.AsOf;
    protected bool Loading { get; set; } = true;
    protected string? LoadError { get; set; }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    protected override async Task OnInitializedAsync()
    {
        State.AsOfChanged += OnAsOfChanged;
        Actor = (await Auth.GetAuthenticationStateAsync()).User.Identity?.Name ?? "unknown";
        await Reload();
    }

    private void OnAsOfChanged() => _ = InvokeAsync(Reload);

    protected async Task Reload()
    {
        Loading = true; LoadError = null;
        try { await LoadAsync(); }
        catch (Exception ex) when (IsUserError(ex)) { LoadError = ex.Message; }
        Loading = false;
        StateHasChanged();
    }

    protected async Task<T> Q<T>(Func<IServiceProvider, Task<T>> query)
    {
        await using var scope = Scopes.CreateAsyncScope();
        return await query(scope.ServiceProvider);
    }

    protected async Task Act(Func<IServiceProvider, string, Task> action, string? success = null, bool reload = true)
    {
        try
        {
            await using var scope = Scopes.CreateAsyncScope();
            await action(scope.ServiceProvider, Actor);
            if (success is not null) State.ShowToast(success);
            if (reload) await Reload();
        }
        catch (Exception ex) when (IsUserError(ex))
        {
            State.ShowToast(ex.Message, error: true);
        }
    }

    protected async Task<bool> Confirm(string message) => await JS.InvokeAsync<bool>("confirm", message);

    protected async Task Download(string fileName, string mime, byte[] bytes) =>
        await JS.InvokeVoidAsync("eosDownload", fileName, mime, Convert.ToBase64String(bytes));

    internal static bool IsUserError(Exception ex) =>
        ex is PlanException or ScenarioException or DemandException or PeopleException or TrainingException or HostException or AdminException
            or SkillRatingException or ArgumentException or InvalidDataException or FormatException or InvalidOperationException;

    public void Dispose()
    {
        State.AsOfChanged -= OnAsOfChanged;
        GC.SuppressFinalize(this);
    }
}
