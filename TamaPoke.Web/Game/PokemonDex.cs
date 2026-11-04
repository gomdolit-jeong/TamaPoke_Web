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

[JsonSerializable(typeof(List<PokemonInfo>))]
internal sealed partial class DexJsonContext : JsonSerializerContext;

// 포켓몬 데이터와 진화 갈래. 규칙은 PC판 PokemonDex.cs / PokemonState.LifeCycle.cs와 같습니다.
public sealed class PokemonDex(HttpClient http)
{
#if DEBUG
    // 개발 중에는 테스트하기 쉽도록 항상 파이리가 부화합니다. (배포판은 무작위)
    private const int TEST_HATCH_SPECIES_ID = 4;
#endif

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

    public async Task InitializeAsync()
    {
        var all = await http.GetFromJsonAsync("data/pokemon.json", DexJsonContext.Default.ListPokemonInfo) ?? [];
        _byId = all.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());

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
    }

    public PokemonInfo? Get(int speciesId) => _byId.GetValueOrDefault(speciesId);

    public string GetName(int speciesId, string language)
    {
        var info = Get(speciesId);
        if (info is null) return $"MON #{speciesId:D4}";
        return language == Utils.Loc.English && !string.IsNullOrEmpty(info.NameEn) ? info.NameEn : info.NameKo;
    }

    // 전설 부화는 도감을 옮긴 뒤 PC판처럼 도감 등록 수 조건과 함께 넣습니다.
    public int PickHatchSpecies()
    {
#if DEBUG
        return TEST_HATCH_SPECIES_ID;
#else
        return _hatchCandidates.Length > 0 ? _hatchCandidates[Random.Shared.Next(_hatchCandidates.Length)] : 1;
#endif
    }

    // 종족의 성비에 따라 성별을 정합니다. (무성 종족은 IsGenderless = true)
    public (bool IsGenderless, bool IsFemale) RollGender(int speciesId)
    {
        int rate = Get(speciesId)?.GenderRate ?? -1;
        if (rate == -1) return (true, false);
        if (rate == 0) return (false, false); // 100% 수컷
        if (rate == 8) return (false, true);  // 100% 암컷
        return (false, Random.Shared.Next(8) < rate);
    }

    // 스프라이트가 없는 진화 갈래는 뺍니다. (갈래가 모두 없으면 진화하지 않음)
    public (int EvolveLevel, int[] NextSpeciesIds)? GetEvolution(int speciesId) =>
        _evolutions.TryGetValue(speciesId, out var evolution)
            ? (evolution.EvolveLevel, evolution.NextSpeciesIds.Where(id => Get(id)?.HasSprite == true).ToArray())
            : null;

    // 타입별 배경. 알은 풀숲입니다. (PC판 GetBiomeFromType과 같음)
    public string GetBiomeName(int speciesId)
    {
        if (speciesId < 0) return "TALL GRASS";
        return Get(speciesId)?.Type1 switch
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
