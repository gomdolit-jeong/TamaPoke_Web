using Microsoft.JSInterop;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public enum CloudStatus { Loading, Unavailable, SignedOut, Syncing, Saved, Failed, Conflict }

public sealed record CloudConflict(string Json, int Version, PokemonState Remote);

// 구글 로그인 세이브. 게임은 항상 브라우저 저장으로 돌아가고, 서버에는 몇 분마다 올립니다.
// 다른 기기가 먼저 저장했으면 덮어쓰지 않고 어느 쪽을 쓸지 물어봅니다.
public sealed class CloudSave(IJSRuntime js, SaveStore store) : IDisposable
{
    private const double UPLOAD_INTERVAL_MS = 5 * 60 * 1000;
    private const string VersionKeyPrefix = "tamapoke.cloud.version.";
    private const string ResetKey = "tamapoke.cloud.reset";

    private IJSObjectReference? _module;
    private DotNetObjectReference<CloudSave>? _self;
    private Func<PokemonState>? _current;
    private Func<string, Task>? _reloadWith;
    private string? _userId;
    private int _version;
    private string? _lastUploaded;
    private double _sinceUploadMs;
    private bool _isUploading;

    public CloudStatus Status { get; private set; } = CloudStatus.Loading;
    public CloudConflict? Conflict { get; private set; }
    public string? SignInError { get; private set; }
    public bool IsSignedIn => _userId is not null;
    public event Action? Changed;

    public async Task InitializeAsync(Func<PokemonState> current, Func<string, Task> reloadWith)
    {
        _current = current;
        _reloadWith = reloadWith;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", BuildInfo.Versioned("./js/cloud.js"));
            _self = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("init", _self);
        }
        catch (JSException ex)
        {
            // 회사 네트워크 등에서 막혀도 게임은 브라우저 저장으로 계속됩니다.
            Console.WriteLine($"클라우드 세이브를 쓸 수 없습니다: {ex.Message}");
            SetStatus(CloudStatus.Unavailable);
        }
    }

    public async Task SignInAsync()
    {
        if (_module is null) return;
        SignInError = await _module.InvokeAsync<string>("signIn");
        if (SignInError.Length == 0) SignInError = null;
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        if (_module is null) return;
        await UploadAsync();
        await _module.InvokeVoidAsync("signOut");
    }

    [JSInvokable]
    public async Task OnAuthChanged(string? userId)
    {
        _userId = userId;
        Conflict = null;
        _lastUploaded = null;
        SignInError = null;
        if (userId is null)
        {
            SetStatus(CloudStatus.SignedOut);
            return;
        }
        await SyncAfterSignInAsync();
    }

    [JSInvokable]
    public Task OnPageHidden() => UploadAsync();

    private async Task SyncAfterSignInAsync()
    {
        SetStatus(CloudStatus.Syncing);
        try
        {
            string raw = await _module!.InvokeAsync<string>("load");
            int.TryParse(await store.GetTextAsync(VersionKeyPrefix + _userId), out _version);

            if (raw.Length == 0)
            {
                _version = 0;
                await UploadAsync();
                return;
            }

            int split = raw.IndexOf('\n');
            int remoteVersion = int.Parse(raw[..split]);
            string json = raw[(split + 1)..];

            var remote = SaveStore.Parse(json);
            if (remote is null)
            {
                SetStatus(CloudStatus.Failed);
                return;
            }

            var conflict = new CloudConflict(json, remoteVersion, remote);

            // 이 기기에서 초기화했다면 서버 세이브를 다시 받아오지 않습니다.
            bool wasReset = await store.GetTextAsync(ResetKey) == _userId;
            if (wasReset) await store.SetTextAsync(ResetKey, "");

            // 이 기기가 막 시작한 상태면 버전과 상관없이 서버 세이브를 받습니다. (빈 세이브로 덮어쓰지 않도록)
            if (_current!().HasNoProgress && !remote.HasNoProgress && !wasReset)
            {
                await UseCloudAsync(conflict);
                return;
            }

            if (remoteVersion == _version)
            {
                await UploadAsync();
                return;
            }

            Conflict = conflict;
            SetStatus(CloudStatus.Conflict);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"클라우드 세이브 읽기 실패: {ex.Message}");
            SetStatus(CloudStatus.Failed);
        }
    }

    // 초기화 직전에 부릅니다. 다시 읽을 때 지운 세이브를 서버에서 되살리지 않게 표시해 둡니다.
    public async Task MarkFactoryResetAsync()
    {
        if (IsSignedIn) await store.SetTextAsync(ResetKey, _userId!);
    }

    public Task ChooseCloudAsync() => Conflict is { } conflict ? UseCloudAsync(conflict) : Task.CompletedTask;

    public async Task ChooseThisDeviceAsync()
    {
        if (Conflict is not { } conflict) return;
        _version = conflict.Version;
        Conflict = null;
        await UploadAsync();
    }

    private async Task UseCloudAsync(CloudConflict conflict)
    {
        await store.SetTextAsync(VersionKeyPrefix + _userId, conflict.Version.ToString());
        await _reloadWith!(conflict.Json);
    }

    public async Task SaveNowAsync()
    {
        _lastUploaded = null;
        await UploadAsync();
    }

    public void Tick(double elapsedMs)
    {
        if (!IsSignedIn) return;
        _sinceUploadMs += elapsedMs;
        if (_sinceUploadMs >= UPLOAD_INTERVAL_MS) _ = UploadAsync();
    }

    private async Task UploadAsync()
    {
        if (_module is null || !IsSignedIn || Conflict is not null || _isUploading) return;

        _sinceUploadMs = 0;
        string json = SaveStore.Serialize(_current!());
        if (json == _lastUploaded)
        {
            SetStatus(CloudStatus.Saved);
            return;
        }

        _isUploading = true;
        SetStatus(CloudStatus.Syncing);
        try
        {
            int result = await _module.InvokeAsync<int>("save", json, _version);
            if (result > 0)
            {
                _version = result;
                _lastUploaded = json;
                await store.SetTextAsync(VersionKeyPrefix + _userId, result.ToString());
                SetStatus(CloudStatus.Saved);
            }
            else
            {
                // 다른 기기가 먼저 저장했습니다. 덮어쓰지 않고 물어봅니다.
                _isUploading = false;
                await SyncAfterSignInAsync();
            }
        }
        catch (JSException ex)
        {
            Console.WriteLine($"클라우드 세이브 쓰기 실패: {ex.Message}");
            SetStatus(CloudStatus.Failed);
        }
        finally
        {
            _isUploading = false;
        }
    }

    private void SetStatus(CloudStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public void Dispose() => _self?.Dispose();
}
