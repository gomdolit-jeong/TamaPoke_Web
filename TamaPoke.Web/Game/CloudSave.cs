using Microsoft.JSInterop;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public enum CloudStatus { Loading, Unavailable, SignedOut, Syncing, Saved, Failed, Conflict }

public sealed record CloudConflict(string Json, int Version, PokemonState Remote);

// 구글 계정 세이브. 게스트 세이브와 저장 칸이 따로이고, 로그인하면 서버 세이브를 기준으로 시작합니다.
// 플레이 중에는 이 기기의 계정 칸에 저장하고 서버에는 몇 분마다 올립니다.
// 다른 기기가 저장한 세이브와 양쪽 다 달라졌을 때만 어느 쪽을 쓸지 물어봅니다.
public sealed class CloudSave(IJSRuntime js, SaveStore store) : IDisposable
{
    private const double UPLOAD_INTERVAL_MS = 5 * 60 * 1000;
    private const string VersionKeyPrefix = "tamapoke.cloud.version.";
    private const string SyncedKeyPrefix = "tamapoke.cloud.synced."; // 마지막으로 서버와 맞춘 세이브

    private IJSObjectReference? _module;
    private DotNetObjectReference<CloudSave>? _self;
    private Func<PokemonState>? _current;
    private Func<string?, string?, Task>? _switchSave;
    private string? _userId;
    private int _version;
    private string? _lastUploaded;
    private double _sinceUploadMs;
    private bool _isUploading;
    private bool _isReady;
    private bool _isSuspended;

    public CloudStatus Status { get; private set; } = CloudStatus.Loading;
    public CloudConflict? Conflict { get; private set; }
    public bool IsGuestImportPending { get; private set; }
    public string? SignInError { get; private set; }
    public bool IsSignedIn => _userId is not null;
    public string? UserId => _userId;

    // 계정 세이브를 불러와서 플레이할 수 있는 상태
    public bool IsAccountReady => _isReady;
    public bool IsWaitingForChoice => Conflict is not null || IsGuestImportPending;
    public bool IsSyncing => IsSignedIn && !_isReady && !IsWaitingForChoice && Status == CloudStatus.Syncing;
    public event Action? Changed;

    // switchSave(저장 칸, 세이브 JSON): 해당 칸으로 바꿉니다. 저장 칸이 null이면 게스트, JSON이 null이면 칸에 있는 세이브입니다.
    public async Task InitializeAsync(Func<PokemonState> current, Func<string?, string?, Task> switchSave)
    {
        _current = current;
        _switchSave = switchSave;
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

        // 이미 로그인했는데 서버를 못 읽은 경우에는 다시 읽어 봅니다.
        if (IsSignedIn && !_isReady)
        {
            await SyncAsync();
            return;
        }

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
        string? previous = _userId;
        _userId = userId;
        Conflict = null;
        IsGuestImportPending = false;
        _lastUploaded = null;
        _isReady = false;
        SignInError = null;

        if (userId is null)
        {
            SetStatus(CloudStatus.SignedOut);
            if (previous is not null) await _switchSave!(null, null);
            return;
        }
        await SyncAsync();
    }

    [JSInvokable]
    public Task OnPageHidden() => UploadAsync();

    // 이 페이지가 곧 사라지거나 다른 탭에 밀렸을 때 부릅니다. 이후로 이 페이지는 서버에 올리지 않습니다.
    public void Suspend() => _isSuspended = true;

    private async Task SyncAsync()
    {
        string userId = _userId!;
        SetStatus(CloudStatus.Syncing);

        bool isActive = store.Slot == userId;
        string? local = isActive ? SaveStore.Serialize(_current!()) : await store.GetSlotTextAsync(userId);

        string raw;
        try
        {
            raw = await _module!.InvokeAsync<string>("load");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"클라우드 세이브 읽기 실패: {ex.Message}");
            await PlayLocalAfterFailureAsync(local);
            return;
        }

        int.TryParse(await store.GetTextAsync(VersionKeyPrefix + userId), out _version);

        // 서버에 세이브가 없습니다.
        if (raw.Length == 0)
        {
            if (local is not null)
            {
                _version = 0;
                await StartPlayingAsync(local);
                return;
            }
            if (!_current!().HasNoProgress)
            {
                IsGuestImportPending = true;
                SetStatus(CloudStatus.Conflict);
                return;
            }
            await BeginAccountAsync(SaveStore.Serialize(new PokemonState()));
            return;
        }

        int split = raw.IndexOf('\n');
        var remote = split > 0 ? SaveStore.Parse(raw[(split + 1)..]) : null;
        if (remote is null || !int.TryParse(raw[..split], out int remoteVersion))
        {
            await PlayLocalAfterFailureAsync(local);
            return;
        }
        string json = raw[(split + 1)..];

        // 이 기기에 이 계정 세이브가 없으면 서버 세이브로 시작합니다.
        if (local is null)
        {
            await ApplyRemoteAsync(json, remoteVersion);
            return;
        }

        // 서버가 그대로면 이 기기가 같거나 더 최신입니다.
        if (remoteVersion == _version)
        {
            if (local == json) _lastUploaded = json;
            await StartPlayingAsync(local);
            return;
        }

        // 다른 기기가 저장했고 이 기기에서 달라진 게 없으면 서버 세이브를 받습니다.
        if (local == json || local == await store.GetTextAsync(SyncedKeyPrefix + userId))
        {
            await ApplyRemoteAsync(json, remoteVersion);
            return;
        }

        // 양쪽이 모두 달라졌으면 물어봅니다.
        await ActivateAsync(local);
        Conflict = new CloudConflict(json, remoteVersion, remote);
        SetStatus(CloudStatus.Conflict);
    }

    // 서버를 못 읽었을 때: 이 기기에 계정 세이브가 있으면 그것으로 계속합니다. 없으면 시작 화면에 머뭅니다.
    private async Task PlayLocalAfterFailureAsync(string? local)
    {
        if (local is not null)
        {
            await ActivateAsync(local);
            _isReady = true;
        }
        SetStatus(CloudStatus.Failed);
    }

    private async Task ActivateAsync(string local)
    {
        if (store.Slot != _userId) await _switchSave!(_userId, local);
    }

    private async Task StartPlayingAsync(string local)
    {
        await ActivateAsync(local);
        _isReady = true;
        await UploadAsync();
    }

    private async Task ApplyRemoteAsync(string json, int version)
    {
        _version = version;
        await store.SetTextAsync(VersionKeyPrefix + _userId, version.ToString());
        await store.SetTextAsync(SyncedKeyPrefix + _userId, json);
        await _switchSave!(_userId, json);
        Conflict = null;
        _lastUploaded = json;
        _isReady = true;
        SetStatus(CloudStatus.Saved);
    }

    private async Task BeginAccountAsync(string json)
    {
        _version = 0;
        IsGuestImportPending = false;
        await _switchSave!(_userId, json);
        _isReady = true;
        await UploadAsync();
    }

    // 처음 로그인: 게스트 세이브를 계정 세이브로 가져옵니다. 게스트 세이브는 그대로 남습니다.
    public Task ChooseGuestSaveAsync() =>
        IsGuestImportPending ? BeginAccountAsync(SaveStore.Serialize(_current!())) : Task.CompletedTask;

    public Task ChooseNewEggAsync() =>
        IsGuestImportPending ? BeginAccountAsync(SaveStore.Serialize(new PokemonState())) : Task.CompletedTask;

    public Task ChooseCloudAsync() => Conflict is { } conflict ? ApplyRemoteAsync(conflict.Json, conflict.Version) : Task.CompletedTask;

    public async Task ChooseThisDeviceAsync()
    {
        if (Conflict is not { } conflict) return;
        _version = conflict.Version;
        Conflict = null;
        _isReady = true;
        await UploadAsync();
    }

    public async Task SaveNowAsync()
    {
        _lastUploaded = null;
        await UploadAsync();
    }

    public void Tick(double elapsedMs)
    {
        if (!_isReady) return;
        _sinceUploadMs += elapsedMs;
        if (_sinceUploadMs >= UPLOAD_INTERVAL_MS) _ = UploadAsync();
    }

    private async Task UploadAsync()
    {
        if (_module is null || !IsSignedIn || !_isReady || _isSuspended || Conflict is not null || _isUploading) return;

        _sinceUploadMs = 0;
        string json = SaveStore.Serialize(_current!());
        if (json == _lastUploaded)
        {
            await store.SetTextAsync(SyncedKeyPrefix + _userId, json);
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
                await store.SetTextAsync(SyncedKeyPrefix + _userId, json);
                SetStatus(CloudStatus.Saved);
            }
            else
            {
                // 다른 기기가 먼저 저장했습니다. 덮어쓰지 않고 다시 맞춥니다.
                _isUploading = false;
                await SyncAsync();
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

    // 대전처럼 같은 Firebase 연결을 쓰는 기능용
    public async Task<T> CallAsync<T>(string function, params object?[] args)
    {
        if (_module is null || !IsSignedIn) throw new InvalidOperationException("로그인하지 않았습니다.");
        return await _module.InvokeAsync<T>(function, args);
    }

    private void SetStatus(CloudStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public void Dispose() => _self?.Dispose();
}
