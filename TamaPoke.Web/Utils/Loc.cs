using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace TamaPoke.Web.Utils;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LangJsonContext : JsonSerializerContext;

// 게임 로직에서 문구를 읽을 때 씁니다. (PC판의 정적 Loc.T / Loc.F와 같은 역할)
public static class L
{
    public static Loc Current { get; set; } = null!;
    public static string Language => Current.Language;
    public static string T(string key) => Current.T(key);
    public static string F(string key, params object?[] args) => string.Format(Current.T(key), args);

    // 한국어 조사: 마지막 글자에 받침이 있으면 withBatchim(이/은), 없으면 withoutBatchim(가/는)
    public static string Josa(string word, string withBatchim, string withoutBatchim)
    {
        if (string.IsNullOrEmpty(word)) return withBatchim;
        char last = word[^1];
        if (last < '가' || last > '힣') return withBatchim;
        return (last - 0xAC00) % 28 != 0 ? withBatchim : withoutBatchim;
    }
}

// 다국어 문구: wwwroot/lang/{언어}.json (PC판 Lang 파일과 같은 키)
// 번역이 없는 키는 한국어 → 키 이름 순으로 대체합니다.
public sealed class Loc(HttpClient http, IJSRuntime js)
{
    public const string Korean = "ko";
    public const string English = "en";
    private const string LanguageKey = "tamapoke.language";

    private Dictionary<string, string> _strings = [];
    private Dictionary<string, string> _fallback = [];

    public string Language { get; private set; } = Korean;

    public string T(string key) =>
        _strings.TryGetValue(key, out var text) || _fallback.TryGetValue(key, out text) ? text : key;

    public async Task InitializeAsync()
    {
        L.Current = this;
        string? saved = await js.InvokeAsync<string?>("localStorage.getItem", LanguageKey);
        string browser = await js.InvokeAsync<string>("tamaPoke.browserLanguage");
        await LoadAsync(saved is Korean or English ? saved : browser.StartsWith(Korean, StringComparison.OrdinalIgnoreCase) ? Korean : English);
    }

    public async Task ToggleLanguageAsync()
    {
        await LoadAsync(Language == Korean ? English : Korean);
        await js.InvokeVoidAsync("localStorage.setItem", LanguageKey, Language);
    }

    private async Task LoadAsync(string language)
    {
        if (_fallback.Count == 0) _fallback = await ReadAsync(Korean);
        _strings = language == Korean ? _fallback : await ReadAsync(language);
        Language = language;
        await js.InvokeVoidAsync("tamaPoke.setDocumentLanguage", language);
    }

    private async Task<Dictionary<string, string>> ReadAsync(string language) =>
        await http.GetFromJsonAsync(BuildInfo.Versioned($"lang/{language}.json"), LangJsonContext.Default.DictionaryStringString) ?? [];
}
