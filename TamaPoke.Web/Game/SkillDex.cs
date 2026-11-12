using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public enum SkillCategory { Physical, Special, Status }

public enum SkillAilment { None, Paralysis, Sleep, Freeze, Burn, Poison, BadPoison }

// 배틀 중에만 유지되는 능력치 단계(-6 ~ +6)의 종류
public enum BattleStat { Attack, Defense, SpAttack, SpDefense, Speed, Accuracy, Evasion }

public enum StatTarget { Opponent, User }

public sealed class StatChange
{
    public BattleStat Stat { get; set; }
    public int Change { get; set; }
}

// wwwroot/data/moves.json 한 줄. 기술 조건은 PC판 SkillInfo.cs와 같습니다.
public sealed class SkillInfo
{
    public int Id { get; set; }
    [JsonPropertyName("Name")] public string NameKo { get; set; } = "";
    public string? NameEn { get; set; }
    public PokemonType Type { get; set; }
    public SkillCategory Category { get; set; }
    public int Power { get; set; }
    public int Accuracy { get; set; }
    public SkillAilment Ailment { get; set; }
    public int AilmentChance { get; set; }
    public List<StatChange>? StatChanges { get; set; }
    public StatTarget StatTarget { get; set; }
    public int StatChance { get; set; }
    public int Priority { get; set; }
    public int MinHits { get; set; }
    public int MaxHits { get; set; }
    public int Drain { get; set; }
    public int CritRate { get; set; }
    public int FlinchChance { get; set; }

    [JsonIgnore] public string Name => L.Language == Loc.English && !string.IsNullOrEmpty(NameEn) ? NameEn : NameKo;
    [JsonIgnore] public bool HasStatChanges => StatChanges is { Count: > 0 };

    // 조건이 맞아야 성공하는 기술
    [JsonIgnore] public bool IsFirstTurnOnly => Id is 252 or 660;
    [JsonIgnore] public bool NeedsTargetAsleep => Id == 138;
    [JsonIgnore] public bool NeedsUserAsleep => Id == 173;
    [JsonIgnore] public bool FailsIfHitFirst => Id == 264;
    [JsonIgnore] public bool NeedsPhysicalHitFirst => Id == 704;
    [JsonIgnore] public bool NeedsTargetAttacking => Id is 389 or 909;
    [JsonIgnore] public bool NeedsSharedType => Id == 485;
    [JsonIgnore] public bool LosesOwnType => Id is 682 or 892;
    [JsonIgnore] public bool IsLastResort => Id == 387;
    [JsonIgnore] public bool IsUnavailable => Id is 562 or 798;

    // 여러 턴에 걸친 기술
    [JsonIgnore] public bool NeedsRecharge => Id is 63 or 416 or 307 or 308 or 338 or 439 or 459 or 711 or 794 or 795;
    [JsonIgnore] public bool IsCharging => Id is 13 or 76 or 130 or 143 or 669 or 553 or 554 or 800 or 905;
    [JsonIgnore] public bool IsHiding => Id is 19 or 91 or 291 or 340 or 467 or 507 or 566;
    [JsonIgnore] public bool IsRolling => Id is 205 or 301;
    [JsonIgnore] public bool IsUproar => Id == 253;
    [JsonIgnore] public bool IsLocking => Id is 37 or 80 or 200 or 833 || IsRolling || IsUproar;
    [JsonIgnore] public bool IsTrapping => Id is 20 or 35 or 83 or 128 or 250 or 328 or 463 or 611 or 779 or 819;

    // 이 게임에서 효과를 낼 수 있는 기술인지 (일격필살, 카운터, 회복기 등은 후보에서 빠짐)
    [JsonIgnore] public bool IsSupported => !IsUnavailable && (Power > 0 || Ailment != SkillAilment.None || HasStatChanges);
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<SkillInfo>))]
[JsonSerializable(typeof(Dictionary<int, int[][]>), TypeInfoPropertyName = "Learnsets")]
internal sealed partial class SkillJsonContext : JsonSerializerContext;

public readonly record struct LearnMove(int Level, int MoveId);

public sealed class SkillDex(HttpClient http)
{
    private const int TOXIC_MOVE_ID = 92;

    // 타입별 기본 공격기 (자력기가 없거나 자기 타입 기술이 없을 때 채워 넣는 용도)
    private static readonly Dictionary<PokemonType, int> FallbackMoves = new()
    {
        [PokemonType.Normal] = 33, [PokemonType.Fire] = 52, [PokemonType.Water] = 55, [PokemonType.Electric] = 84,
        [PokemonType.Grass] = 22, [PokemonType.Ice] = 181, [PokemonType.Fighting] = 2, [PokemonType.Poison] = 40,
        [PokemonType.Ground] = 189, [PokemonType.Flying] = 16, [PokemonType.Psychic] = 93, [PokemonType.Bug] = 522,
        [PokemonType.Rock] = 88, [PokemonType.Ghost] = 122, [PokemonType.Dragon] = 225, [PokemonType.Dark] = 44,
        [PokemonType.Steel] = 232, [PokemonType.Fairy] = 584,
    };

    private Dictionary<int, SkillInfo> _moves = [];
    private Dictionary<int, LearnMove[]> _learnsets = [];
    private Dictionary<int, (int MoveId, int Weight)[]> _famous = [];

    public async Task InitializeAsync()
    {
        var moves = await http.GetFromJsonAsync(BuildInfo.Versioned("data/moves.json"), SkillJsonContext.Default.ListSkillInfo) ?? [];
        foreach (var move in moves)
        {
            // 맹독은 데이터상 일반 독으로 들어오므로 맹독으로 보정합니다.
            if (move.Id == TOXIC_MOVE_ID && move.Ailment == SkillAilment.Poison) move.Ailment = SkillAilment.BadPoison;
        }
        _moves = moves.ToDictionary(m => m.Id);

        var learnsets = await http.GetFromJsonAsync(BuildInfo.Versioned("data/learnsets.json"), SkillJsonContext.Default.Learnsets) ?? [];
        _learnsets = learnsets.ToDictionary(p => p.Key, p => p.Value.Select(m => new LearnMove(m[0], m[1])).ToArray());

        // 서바이벌 대표 기술 (tools/make_survival_moves.py). 없어도 게임은 레벨업 기술만으로 돌아갑니다.
        try
        {
            var famous = await http.GetFromJsonAsync(BuildInfo.Versioned("data/survival_moves.json"), SkillJsonContext.Default.Learnsets) ?? [];
            _famous = famous.ToDictionary(p => p.Key, p => p.Value.Select(m => (m[0], m[1])).ToArray());
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException)
        {
            _famous = [];
        }
    }

    // 실전(Pokémon Showdown 랜덤 배틀)에서 이 포켓몬이 자주 쓰는 기술과 그 무게. 무게가 큰 순서입니다.
    public IReadOnlyList<(int MoveId, int Weight)> GetFamousMoves(int speciesId) => _famous.GetValueOrDefault(speciesId) ?? [];

    public IReadOnlyList<LearnMove> GetActiveLearnset(int speciesId) => _learnsets.GetValueOrDefault(speciesId) ?? [];

    public SkillInfo? GetSkill(int skillId) => _moves.GetValueOrDefault(skillId);

    public int GetLearnCount(int speciesId) => GetActiveLearnset(speciesId).Count;

    public int GetLearnLevel(int speciesId, int index) => GetActiveLearnset(speciesId)[index].Level;

    public int GetLearnMove(int speciesId, int index) => GetActiveLearnset(speciesId)[index].MoveId;

    public int GetFallbackMove(PokemonType type) =>
        FallbackMoves.TryGetValue(type, out int id) && _moves.ContainsKey(id) ? id : 33;
}
