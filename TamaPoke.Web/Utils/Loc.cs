using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace TamaPoke.Web.Utils;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LangJsonContext : JsonSerializerContext;

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
        await http.GetFromJsonAsync($"lang/{language}.json", LangJsonContext.Default.DictionaryStringString) ?? [];
}
