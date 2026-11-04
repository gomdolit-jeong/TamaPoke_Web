using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace TamaPoke.Web.Game;

[JsonSerializable(typeof(PokemonState))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;

// 브라우저 localStorage에 세이브를 둡니다. PC판 save.json / save.bak.json처럼 직전 세이브도 남깁니다.
// 게스트와 구글 계정은 저장 칸이 따로입니다. (Slot이 null이면 게스트, 아니면 계정 uid)
public sealed class SaveStore(IJSRuntime js)
{
    private const string SaveKey = "tamapoke.save";
    private const string BackupKey = "tamapoke.save.bak";
    private const string CorruptKey = "tamapoke.save.corrupt";

    public string? Slot { get; set; }

    // 이번에 불러올 때 사용자에게 알릴 내용 (세이브 손상)
    private string? _loadNoticeKey;

    public string? TakeLoadNotice()
    {
        string? key = _loadNoticeKey;
        _loadNoticeKey = null;
        return key;
    }

    private static string KeyFor(string key, string? slot) => slot is null ? key : $"{key}.user.{slot}";

    public Task<string?> GetSlotTextAsync(string? slot) => GetAsync(KeyFor(SaveKey, slot));

    public async Task<PokemonState> LoadAsync()
    {
        string saveKey = KeyFor(SaveKey, Slot), backupKey = KeyFor(BackupKey, Slot);
        string? main = await GetAsync(saveKey);
        if (main is null) return Parse(await GetAsync(backupKey)) ?? new PokemonState();

        if (Parse(main) is { } state) return state;

        // 손상된 세이브는 지우지 않고 따로 남긴 뒤 백업에서 복구합니다.
        await SetAsync(KeyFor(CorruptKey, Slot), main);
        var backup = Parse(await GetAsync(backupKey));
        _loadNoticeKey = backup is null ? "web.save.corrupted" : "web.save.restored";
        return backup ?? new PokemonState();
    }

    public async Task SaveAsync(PokemonState state)
    {
        // 저장 중에 칸이 바뀌어도 원래 칸에 저장되도록 시작할 때 정합니다.
        string saveKey = KeyFor(SaveKey, Slot), backupKey = KeyFor(BackupKey, Slot);
        string? previous = await GetAsync(saveKey);
        if (previous is not null) await SetAsync(backupKey, previous);
        await SetAsync(saveKey, Serialize(state));
    }

    public static string Serialize(PokemonState state) => JsonSerializer.Serialize(state, SaveJsonContext.Default.PokemonState);

    // 서버 세이브로 바꿀 때 씁니다. 이전 세이브는 백업에 남깁니다.
    public async Task ReplaceAsync(string json)
    {
        string saveKey = KeyFor(SaveKey, Slot);
        string? previous = await GetAsync(saveKey);
        if (previous == json) return;
        if (previous is not null) await SetAsync(KeyFor(BackupKey, Slot), previous);
        await SetAsync(saveKey, json);
    }

    public Task<string?> GetTextAsync(string key) => GetAsync(key);

    public Task SetTextAsync(string key, string value) => SetAsync(key, value);

    public static PokemonState? Parse(string? json)
    {
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize(json, SaveJsonContext.Default.PokemonState);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"세이브 읽기 실패: {ex.Message}");
            return null;
        }
    }

    private async Task SetAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"세이브 쓰기 실패: {ex.Message}");
        }
    }
}
