using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace TamaPoke.Web.Game;

[JsonSerializable(typeof(PokemonState))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;

// 브라우저 localStorage에 세이브를 둡니다. PC판 save.json / save.bak.json처럼 직전 세이브도 남깁니다.
public sealed class SaveStore(IJSRuntime js)
{
    private const string SaveKey = "tamapoke.save";
    private const string BackupKey = "tamapoke.save.bak";
    private const string CorruptKey = "tamapoke.save.corrupt";

    public async Task<PokemonState> LoadAsync()
    {
        string? main = await GetAsync(SaveKey);
        if (main is null) return Parse(await GetAsync(BackupKey)) ?? new PokemonState();

        if (Parse(main) is { } state) return state;

        // 손상된 세이브는 지우지 않고 따로 남긴 뒤 백업에서 복구합니다.
        await SetAsync(CorruptKey, main);
        return Parse(await GetAsync(BackupKey)) ?? new PokemonState();
    }

    public async Task SaveAsync(PokemonState state)
    {
        string? previous = await GetAsync(SaveKey);
        if (previous is not null) await SetAsync(BackupKey, previous);
        await SetAsync(SaveKey, JsonSerializer.Serialize(state, SaveJsonContext.Default.PokemonState));
    }

    private static PokemonState? Parse(string? json)
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
