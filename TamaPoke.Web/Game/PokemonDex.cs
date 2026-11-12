using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TamaPoke.Web.Game;

public enum PokemonType
{
    None, Normal, Fire, Water, Electric, Grass, Ice, Fighting,
    Poison, Ground, Flying, Psychic, Bug, Rock, Ghost, Dragon,
    Dark, Steel, Fairy
}

// wwwroot/data/pokemon.json 한 줄. PC판 pokemon_data.json에 HasSprite를 더한 것입니다. (tools/make_pokemon_data.py)
public sealed class PokemonInfo
{
    public int Id { get; set; }
    [JsonPropertyName("DisplayName")] public string NameKo { get; set; } = "";
    public string? NameEn { get; set; }
    public PokemonType Type1 { get; set; }
    public PokemonType Type2 { get; set; }
    public int BaseHp { get; set; }
    public int BaseAtk { get; set; }
    public int BaseDef { get; set; }
    public int BaseSpeed { get; set; }
    public int BaseSpA { get; set; }
    public int BaseSpD { get; set; }
    public int EvolveTo { get; set; }
    public int EvolveLevel { get; set; }
    public int GenderRate { get; set; } // -1: 무성, 0~8: 암컷 비율(8분율)
    public bool HasSprite { get; set; }
}

// wwwroot/data/abilities.json (tools/make_ability_data.py, PokeAPI 데이터)
//   Names: 특성 → [한국어, 영어], Species: 포켓몬 번호 → [[특성, 숨겨진 특성이면 1], ...]
public sealed class AbilityData
{
    [JsonPropertyName("names")] public Dictionary<string, string[]> Names { get; set; } = [];
    [JsonPropertyName("species")] public Dictionary<string, List<List<System.Text.Json.JsonElement>>> Species { get; set; } = [];
}

// 포켓몬의 특성 하나. Key는 영어 이름을 소문자로 붙여 쓴 것(예: "overgrow")
public sealed record PokemonAbility(string Key, bool Hidden);

[JsonSerializable(typeof(List<PokemonInfo>))]
[JsonSerializable(typeof(Dictionary<int, Dictionary<string, string>>), TypeInfoPropertyName = "Forms")]
[JsonSerializable(typeof(AbilityData))]
internal sealed partial class DexJsonContext : JsonSerializerContext;

// 포켓몬 데이터와 진화 갈래. 규칙은 PC판 PokemonDex.cs / PokemonState.LifeCycle.cs와 같습니다.
public sealed class PokemonDex(HttpClient http)
{
#if DEBUG
    // 개발 중에는 테스트하기 쉽도록 항상 파이리가 부화합니다. (배포판은 무작위)
    private const int TEST_HATCH_SPECIES_ID = 4;
#endif

    private const int WILD_LEGENDARY_CHANCE_PERCENT = 1;
    private const int LEGENDARY_HATCH_MIN_DEX = 30;
    private const int LEGENDARY_HATCH_CHANCE_PERCENT = 3;

    // 세대(지방)별 도감 번호 범위 (PC판 GameRegions.cs와 같음)
    public static readonly (int Generation, int StartId, int EndId)[] Regions =
    [
        (1, 1, 151), (2, 152, 251), (3, 252, 386), (4, 387, 493), (5, 494, 649),
        (6, 650, 721), (7, 722, 809), (8, 810, 905), (9, 906, 1025),
    ];

    private static readonly HashSet<int> LegendaryIds =
    [
        144, 145, 146, 150, 151,
        243, 244, 245, 249, 250, 251,
        377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493,
        494, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649,
        716, 717, 718, 719, 720, 721,
        772, 773, 785, 786, 787, 788, 789, 790, 791, 792, 793, 794, 795, 796, 797, 798, 799, 800, 801, 802, 803, 804, 805, 806, 807, 808, 809,
        888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898, 905,
        1001, 1002, 1003, 1004, 1007, 1008, 1014, 1015, 1016, 1017, 1024, 1025,
    ];

    // 진화 갈래가 여러 개이거나 레벨이 아닌 방법으로 진화하는 포켓몬 (PC판 InitializeEvolutionTable과 같음)
    private static readonly Dictionary<int, (int EvolveLevel, int[] NextSpeciesIds)> EvolutionOverrides = new()
    {
        [44] = (36, [45, 182]),
        [61] = (36, [62, 186]),
        [79] = (37, [80, 199]),
        [133] = (30, [134, 135, 136, 196, 197, 470, 471, 700]),
        [236] = (20, [106, 107, 237]),
        [265] = (7, [266, 268]),
        [281] = (30, [282, 475]),
        [290] = (20, [291, 292]),
        [361] = (42, [362, 478]),
        [366] = (30, [367, 368]),
        [412] = (20, [413, 414]),
        [415] = (21, [416]),
        [52] = (28, [53, 863]),
        [123] = (40, [212, 900]),
        [439] = (18, [122, 866]),
        [562] = (34, [563, 867]),
        [790] = (53, [791, 792]),
        [840] = (30, [841, 842, 1011]),
        [935] = (30, [936, 937]),
        [194] = (20, [195, 980]),
        [215] = (28, [461, 903]),
    };

    private Dictionary<int, PokemonInfo> _byId = [];
    private Dictionary<int, (int EvolveLevel, int[] NextSpeciesIds)> _evolutions = [];
    private int[] _hatchCandidates = [];
    private int[] _wildCandidates = [];
    private int[] _wildLegendaries = [];

    private const int SPECIAL_FORM_CHANCE = 10; // 특수 폼(알로라 등)이 나올 확률(%)
    private static readonly string[] BattleOnlyFormTokens = ["mega", "primal", "gigantamax", "shadow"];

    private Dictionary<int, Dictionary<string, string>> _forms = [];

    // 폼 접미사(Shiny, Alola, Mega_X ...)로 스프라이트 경로를 찾습니다. 없으면 기본 모습입니다.
    public string SpritePath(int speciesId, string? form) =>
        !string.IsNullOrEmpty(form) && _forms.GetValueOrDefault(speciesId)?.GetValueOrDefault(form) is { } path ? path : $"{speciesId:D4}";

    public bool HasForm(int speciesId, string form) => _forms.GetValueOrDefault(speciesId)?.ContainsKey(form) == true;

    // 폼이 여러 개면 10% 확률로 특수 폼 하나를 고릅니다. (이로치/암컷/배틀 전용 폼 제외)
    public string PickBaseForm(int speciesId)
    {
        if (Random.Shared.Next(100) >= SPECIAL_FORM_CHANCE || !_forms.TryGetValue(speciesId, out var forms)) return "";
        var special = forms.Keys.Where(f =>
        {
            string lower = f.ToLowerInvariant();
            return !lower.Contains("shiny") && !lower.Contains("female") && !lower.Split('_').Any(BattleOnlyFormTokens.Contains);
        }).ToArray();
        return special.Length > 0 ? special[Random.Shared.Next(special.Length)] : "";
    }

    // 기준 폼에 이로치/암컷 변형이 있으면 그 폼으로 바꿉니다. 없으면 기준 폼 그대로입니다.
    public string ApplyVariants(int speciesId, string baseForm, bool shiny, bool female)
    {
        string prefix = baseForm.Length > 0 ? baseForm + "_" : "";
        string? Find(string suffix) => HasForm(speciesId, prefix + suffix) ? prefix + suffix : null;

        string? match = null;
        if (shiny && female) match = Find("Shiny_Female") ?? Find("Female_Shiny") ?? Find("Shiny");
        else if (female) match = Find("Female");
        else if (shiny) match = Find("Shiny");
        return match ?? baseForm;
    }

    public async Task InitializeAsync()
    {
        var all = await http.GetFromJsonAsync(Utils.BuildInfo.Versioned("data/pokemon.json"), DexJsonContext.Default.ListPokemonInfo) ?? [];
        _forms = await http.GetFromJsonAsync(Utils.BuildInfo.Versioned("data/forms.json"), DexJsonContext.Default.Forms) ?? [];
        _byId = all.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        await LoadAbilitiesAsync();

        // 레벨이 아닌 방법(돌, 교환 등)으로 진화하는 포켓몬은 Lv.36에 진화합니다.
        _evolutions = all.Where(p => p.EvolveTo > 0)
            .ToDictionary(p => p.Id, p => (p.EvolveLevel > 0 ? p.EvolveLevel : 36, new[] { p.EvolveTo }));
        foreach (var (id, evolution) in EvolutionOverrides) _evolutions[id] = evolution;

        // 부화 후보: 다른 포켓몬이 진화한 모습이 아니고, 전설이 아니며, 스프라이트가 있는 포켓몬
        var evolvedIds = _evolutions.Values.SelectMany(e => e.NextSpeciesIds).ToHashSet();
        _hatchCandidates = all
            .Where(p => p.HasSprite && !evolvedIds.Contains(p.Id) && !LegendaryIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToArray();
        _wildCandidates = all.Where(p => p.HasSprite && !LegendaryIds.Contains(p.Id)).Select(p => p.Id).ToArray();
        _wildLegendaries = all.Where(p => p.HasSprite && LegendaryIds.Contains(p.Id)).Select(p => p.Id).ToArray();
    }

    // --- 특성 (서바이벌 특성 효과에 씀) ---

    private Dictionary<string, string[]> _abilityNames = [];
    private Dictionary<int, List<PokemonAbility>> _abilities = [];

    // 특성 데이터는 없어도 게임은 돌아가야 해서, 못 받으면 비워 둡니다.
    private async Task LoadAbilitiesAsync()
    {
        try
        {
            var data = await http.GetFromJsonAsync(Utils.BuildInfo.Versioned("data/abilities.json"), DexJsonContext.Default.AbilityData);
            if (data is null) return;
            _abilityNames = data.Names;
            _abilities = data.Species
                .Where(p => int.TryParse(p.Key, out _))
                .ToDictionary(p => int.Parse(p.Key), p => p.Value
                    .Where(row => row.Count >= 2)
                    .Select(row => new PokemonAbility(row[0].GetString() ?? "", row[1].GetInt32() == 1))
                    .Where(a => a.Key.Length > 0)
                    .ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException or InvalidOperationException)
        {
            Console.WriteLine($"특성 데이터 읽기 실패: {ex.Message}");
        }
    }

    public IReadOnlyList<PokemonAbility> GetAbilities(int speciesId) =>
        _abilities.TryGetValue(speciesId, out var list) ? list : [];

    public string GetAbilityName(string key, string language) =>
        _abilityNames.TryGetValue(key, out var names) && names.Length >= 2
            ? (language == Utils.Loc.English && names[1].Length > 0 ? names[1] : names[0])
            : key;

    // 시작 화면에서 뛰어다닐 포켓몬: 진화형과 전설까지 스프라이트가 있는 포켓몬 중 겹치지 않게 고릅니다.
    public int[] PickTitleSpecies(int count) =>
        _wildCandidates.Concat(_wildLegendaries).OrderBy(_ => Random.Shared.Next()).Take(count).ToArray();

    // 야생 상대: 스프라이트가 있는 포켓몬. 전설은 1% 확률입니다.
    public int PickWildSpecies(IReadOnlyCollection<int> generations)
    {
        var legendaries = _wildLegendaries.Where(id => IsInGenerations(id, generations)).ToArray();
        if (legendaries.Length > 0 && Random.Shared.Next(100) < WILD_LEGENDARY_CHANCE_PERCENT)
            return legendaries[Random.Shared.Next(legendaries.Length)];
        var candidates = _wildCandidates.Where(id => IsInGenerations(id, generations)).ToArray();
        return candidates.Length > 0 ? candidates[Random.Shared.Next(candidates.Length)] : 1;
    }

    // 서바이벌에 나올 야생 포켓몬: 처음 모습 절반, 진화한 모습 절반을 고르고 약한 순서로 늘어놓습니다. (전설 제외)
    public int[] PickSurvivalRoster(IReadOnlyCollection<int> generations, int count, Random random)
    {
        var basics = _hatchCandidates.Where(id => IsInGenerations(id, generations)).ToArray();
        var evolved = _wildCandidates.Where(id => IsInGenerations(id, generations)).Except(basics).ToArray();
        if (basics.Length == 0) basics = _hatchCandidates;
        if (evolved.Length == 0) evolved = basics;

        int basicCount = count / 2;
        return basics.OrderBy(_ => random.Next()).Take(basicCount)
            .Concat(evolved.OrderBy(_ => random.Next()).Take(count - basicCount))
            .Distinct()
            .OrderBy(id => _byId.TryGetValue(id, out var p) ? p.BaseHp + p.BaseAtk + p.BaseDef + p.BaseSpA + p.BaseSpD + p.BaseSpeed : 0)
            .ToArray();
    }

    // 서바이벌 보스: 고른 세대의 전설 포켓몬 count마리 (겹치지 않게, 모자라면 다른 세대에서도)
    public int[] PickSurvivalBosses(IReadOnlyCollection<int> generations, int count, Random random)
    {
        var picked = _wildLegendaries.Where(id => IsInGenerations(id, generations)).OrderBy(_ => random.Next()).Take(count).ToList();
        picked.AddRange(_wildLegendaries.Except(picked).OrderBy(_ => random.Next()).Take(count - picked.Count));
        return picked.Count > 0 ? [.. picked] : [150];
    }

    public PokemonInfo? Get(int speciesId) => _byId.GetValueOrDefault(speciesId);

    public string GetName(int speciesId, string language)
    {
        var info = Get(speciesId);
        if (info is null) return $"MON #{speciesId:D4}";
        return language == Utils.Loc.English && !string.IsNullOrEmpty(info.NameEn) ? info.NameEn : info.NameKo;
    }

    // 고른 세대의 부화 후보에서 고릅니다. 도감이 30마리 이상이면 3% 확률로 전설이 나옵니다.
    public int PickHatchSpecies(IReadOnlyCollection<int> generations, int registeredCount)
    {
#if DEBUG
        return TEST_HATCH_SPECIES_ID;
#else
        if (registeredCount >= LEGENDARY_HATCH_MIN_DEX && Random.Shared.Next(100) < LEGENDARY_HATCH_CHANCE_PERCENT)
        {
            var legendaries = _wildLegendaries.Where(id => IsInGenerations(id, generations)).ToArray();
            if (legendaries.Length > 0) return legendaries[Random.Shared.Next(legendaries.Length)];
        }
        var candidates = _hatchCandidates.Where(id => IsInGenerations(id, generations)).ToArray();
        return candidates.Length > 0 ? candidates[Random.Shared.Next(candidates.Length)] : 1;
#endif
    }

    private static bool IsInGenerations(int speciesId, IReadOnlyCollection<int> generations) =>
        Regions.Any(r => generations.Contains(r.Generation) && speciesId >= r.StartId && speciesId <= r.EndId);

    // 종족의 성비에 따라 성별을 정합니다. (무성 종족은 IsGenderless = true)
    public (bool IsGenderless, bool IsFemale) RollGender(int speciesId)
    {
        int rate = Get(speciesId)?.GenderRate ?? -1;
        if (rate == -1) return (true, false);
        if (rate == 0) return (false, false); // 100% 수컷
        if (rate == 8) return (false, true);  // 100% 암컷
        return (false, Random.Shared.Next(8) < rate);
    }

    // 진화 전 모습을 거슬러 올라간 첫 모습 (리자몽 → 파이리). 진화하지 않는 포켓몬은 자기 자신입니다.
    public int BaseOf(int speciesId)
    {
        _previous ??= _evolutions
            .SelectMany(e => e.Value.NextSpeciesIds.Select(next => (Next: next, Prev: e.Key)))
            .GroupBy(x => x.Next)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Prev));
        int current = speciesId;
        for (int i = 0; i < 4 && _previous.TryGetValue(current, out int prev) && Get(prev)?.HasSprite == true; i++) current = prev;
        return current;
    }

    private Dictionary<int, int>? _previous;

    // 스프라이트가 없는 진화 갈래는 뺍니다. (갈래가 모두 없으면 진화하지 않음)
    public (int EvolveLevel, int[] NextSpeciesIds)? GetEvolution(int speciesId) =>
        _evolutions.TryGetValue(speciesId, out var evolution)
            ? (evolution.EvolveLevel, evolution.NextSpeciesIds.Where(id => Get(id)?.HasSprite == true).ToArray())
            : null;

    // 폼에 맞는 타입 (알로라의 모습 등)
    public (PokemonType Type1, PokemonType Type2) GetFormTypes(int speciesId, string? form) =>
        PokemonForms.GetTypes(this, speciesId, string.IsNullOrEmpty(form) ? null : $"p{speciesId:D4}_{form}.bin");

    // 타입별 배경. 알은 풀숲입니다. (PC판 GetBiomeFromType과 같음)
    public string GetBiomeName(int speciesId, string? form = null)
    {
        if (speciesId < 0) return "TALL GRASS";
        return GetFormTypes(speciesId, form).Type1 switch
        {
            PokemonType.Water => "BEACH",
            PokemonType.Normal or PokemonType.Flying or PokemonType.Electric => "PATH",
            PokemonType.Fire or PokemonType.Fighting or PokemonType.Rock or PokemonType.Ground => "MOUNTAIN",
            PokemonType.Ice => "SNOW",
            PokemonType.Dark or PokemonType.Ghost or PokemonType.Dragon or PokemonType.Steel => "CAVE",
            _ => "TALL GRASS",
        };
    }
}
