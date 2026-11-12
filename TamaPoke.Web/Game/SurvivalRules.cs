using System.Text.Json.Serialization;

namespace TamaPoke.Web.Game;

// --- 게임(wwwroot/js/survival.js)에 넘기는 값 ---

public sealed record SurvivalSprite(string Url, int Fw, int Fh, int Cols, int Rows, int[] Durations);

// 게임 중의 모습 하나. Forms[0]이 처음 모습이고, Next는 진화할 수 있는 모습의 번호(Forms 안)입니다. Depth는 몇 번 진화한 모습인지.
// EvolveLevel: 게임 안 레벨이 이만큼 되면 다음 모습으로 진화합니다. (진화하지 않으면 0)
public sealed record SurvivalForm(int SpeciesId, string Name, SurvivalSprite? Walk, SurvivalSprite? Idle,
    double MaxHp, double Speed, double Power, double Guard, int Depth, int[] Next, int EvolveLevel);

// 기술 하나 = 무기 하나. Kind는 공격 모양(bolt, spray, slash, nova, orbit, chain, aura), Type은 효과 그림(fx_shot_{type}.png)입니다.
public sealed record SurvivalWeapon(int SkillId, string Kind, string Type, string Color, double Power, double Cooldown, int Extra, bool Slow);

// 적 한 종류. Eff는 내 무기(순서대로)가 이 포켓몬에게 주는 타입 상성 배율입니다.
public sealed record SurvivalEnemy(int SpeciesId, string Name, SurvivalSprite? Sprite, double Hp, double Speed, double Damage, double[] Eff);

public sealed record SurvivalSetup(List<SurvivalForm> Forms, List<SurvivalWeapon> Weapons, List<SurvivalEnemy> Enemies,
    List<SurvivalEnemy> Bosses, int[] BossTimes, bool Muted, string SwarmText, string EvolveText, string BossText,
    Dictionary<string, string> Texts);

// 게임이 끝나면 돌려받는 값
public sealed record SurvivalResult
{
    public int TimeSec { get; init; }
    public int Kills { get; init; }
    public int Level { get; init; }
    public bool Cleared { get; init; }
    public int FinalSpeciesId { get; init; } // 게임이 끝났을 때의 모습 (진화했으면 진화한 포켓몬)

    // 기술별 기록 (SurvivalSetup.Weapons 순서). 대폭발 아이템은 Item~에 따로 셉니다.
    public int[] WeaponSkills { get; init; } = [];
    public int[] WeaponLevels { get; init; } = [];
    public int[] WeaponDamage { get; init; } = [];
    public int[] WeaponKills { get; init; } = [];
    public int ItemDamage { get; init; }
    public int ItemKills { get; init; }
}

// 일시정지 화면에 보여 줄 지금 상태. Power~Pickup은 처음 대비 % (받는 데미지와 재사용 시간은 낮을수록 좋음)
public sealed record SurvivalPassive(string Name, int Level);

public sealed record SurvivalState
{
    public int SpeciesId { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public int Level { get; init; }
    public int Exp { get; init; }
    public int ExpNext { get; init; }
    public int TimeSec { get; init; }
    public int Kills { get; init; }
    public int[] Weapons { get; init; } = [];
    public int[] WeaponDamage { get; init; } = [];
    public int[] WeaponKills { get; init; } = [];
    public List<SurvivalPassive> Passives { get; init; } = [];
    public int Power { get; init; }
    public int Speed { get; init; }
    public int Guard { get; init; }
    public int Cooldown { get; init; }
    public int Pickup { get; init; }
}

// 레벨업 때 고를 수 있는 것: new(새 기술), up(기술 강화), passive(능력 강화), heal(회복)
public sealed record SurvivalOption
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Index { get; init; }
    public string? Name { get; init; }
    public int Level { get; init; }
}

// --- 랭킹 (Firestore survival/{시즌}/players/{uid}) ---

public sealed record SurvivalEntry
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Uid { get; init; }
    public string Tag { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Nickname { get; init; }
    public int SpeciesId { get; init; }
    public int Level { get; init; } = 1;
    public string Form { get; init; } = "";
    public bool Shiny { get; init; }
    public int Score { get; init; }
    public int TimeSec { get; init; }
    public int Kills { get; init; }
    public bool Cleared { get; init; }

    // 그 판에서 배운 기술 (데미지가 많은 순서, 최대 4개)과 기술 레벨. 예전 기록에는 없습니다.
    public int[] Moves { get; init; } = [];
    public int[] MoveLevels { get; init; } = [];
}

public sealed class SurvivalBoardData
{
    public SurvivalEntry? Me { get; set; }
    public List<SurvivalEntry> Top { get; set; } = [];
}

public sealed record SurvivalBoard(string Season, SurvivalEntry? Me, int? MyRank, IReadOnlyList<SurvivalEntry> Top);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SurvivalSetup))]
[JsonSerializable(typeof(SurvivalResult))]
[JsonSerializable(typeof(SurvivalState))]
[JsonSerializable(typeof(List<SurvivalOption>))]
[JsonSerializable(typeof(SurvivalEntry))]
[JsonSerializable(typeof(SurvivalBoardData))]
internal sealed partial class SurvivalJsonContext : JsonSerializerContext;

public static class SurvivalRules
{
    public const int RUN_SECONDS = 600;
    public const int MAX_KILLS_PER_SECOND = 20;
    public const int MIN_ENERGY = 20;
    public const int DEFAULT_SKILL_ID = 33; // 기술이 하나도 없으면 몸통박치기

    // 진화는 도감의 진화 레벨을 따르되, 조금 일찍 합니다. (돌, 교환 등 레벨이 아닌 진화는 도감처럼 Lv.36으로 셈)
    // 예: 파이리 Lv.11 → 리자드 Lv.25, 딥상어동 Lv.17 → 한바이트 Lv.34, 잉어킹 Lv.14
    // 진화하면 체력이 크게 늘어서, 도감 레벨 그대로면 초반을 버티기 어렵습니다.
    public const int MAX_EVOLUTIONS = 2;
    public const double EVOLVE_LEVEL_SCALE = 0.7;
    public const int MIN_EVOLVE_LEVEL = 8;
    public const int MAX_EVOLVE_LEVEL = 40;

    public static int EvolveLevel(int dexLevel) =>
        Math.Clamp((int)Math.Round(dexLevel * EVOLVE_LEVEL_SCALE), MIN_EVOLVE_LEVEL, MAX_EVOLVE_LEVEL);

    // 정기 보스: 이 시간(초)마다 전설 포켓몬이 하나씩 나옵니다. 뒤에 나올수록 단단합니다.
    public static readonly int[] BossTimes = [120, 240, 360, 480];

    // 순위: 오래 버틴 사람이 위, 같으면 쓰러뜨린 수가 많은 사람이 위.
    // 한 숫자로 정렬하도록 버틴 초 × 100000 + 쓰러뜨린 수로 저장합니다. (Firestore 규칙도 같은 식으로 확인합니다)
    public const int SCORE_TIME_UNIT = 100000;

    public static int Score(SurvivalResult result) => Score(result.TimeSec, result.Kills, result.Cleared);

    public static int Score(int timeSec, int kills, bool cleared) => timeSec * SCORE_TIME_UNIT + Math.Min(kills, SCORE_TIME_UNIT - 1);

    public static string RecordText(int timeSec, int kills) => $"{Clock(timeSec)} · KO {kills}";

    // 게임이 보낸 값을 가능한 범위로 맞춥니다.
    public const int MAX_MOVE_LEVEL = 5;

    // 결과에서 랭킹에 올릴 기술: 배운 기술만, 데미지가 많은 순서로
    public static (int[] Moves, int[] Levels) MovesOf(SurvivalResult result)
    {
        var rows = Enumerable.Range(0, Math.Min(result.WeaponSkills.Length, result.WeaponLevels.Length))
            .Where(i => result.WeaponLevels[i] > 0 && result.WeaponSkills[i] > 0)
            .OrderByDescending(i => i < result.WeaponDamage.Length ? result.WeaponDamage[i] : 0)
            .Take(4)
            .ToList();
        return (rows.Select(i => result.WeaponSkills[i]).ToArray(), rows.Select(i => Math.Clamp(result.WeaponLevels[i], 1, MAX_MOVE_LEVEL)).ToArray());
    }

    public static SurvivalResult Clamp(SurvivalResult result)
    {
        int time = Math.Clamp(result.TimeSec, 0, RUN_SECONDS);
        return result with
        {
            TimeSec = time,
            Kills = Math.Clamp(result.Kills, 0, time * MAX_KILLS_PER_SECOND),
            Level = Math.Clamp(result.Level, 1, 999),
            Cleared = result.Cleared && time >= RUN_SECONDS,
        };
    }

    // 기술 타입에 따라 공격 모양이 달라집니다. 변화 기술은 주변에 머무는 오라가 됩니다.
    public static string KindFor(SkillInfo skill) => skill.Category == SkillCategory.Status ? "aura" : skill.Type switch
    {
        PokemonType.Fire or PokemonType.Water => "spray",
        PokemonType.Electric => "chain",
        PokemonType.Grass or PokemonType.Ghost or PokemonType.Fairy => "orbit",
        PokemonType.Ground or PokemonType.Rock or PokemonType.Psychic => "nova",
        PokemonType.Poison => "aura",
        PokemonType.Normal or PokemonType.Fighting or PokemonType.Dark or PokemonType.Steel => "slash",
        _ => "bolt",
    };

    public static string ColorFor(PokemonType type) => type switch
    {
        PokemonType.Fire => "#e65100",
        PokemonType.Water => "#1e78d6",
        PokemonType.Electric => "#e0a800",
        PokemonType.Grass => "#388e3c",
        PokemonType.Ice => "#00acc1",
        PokemonType.Fighting => "#c62828",
        PokemonType.Poison => "#8e24aa",
        PokemonType.Ground => "#a1742a",
        PokemonType.Flying => "#5c6bc0",
        PokemonType.Psychic => "#d81b60",
        PokemonType.Bug => "#7c8a0e",
        PokemonType.Rock => "#8d7a3a",
        PokemonType.Ghost => "#5e4b8b",
        PokemonType.Dragon => "#4527a0",
        PokemonType.Dark => "#4e3c30",
        PokemonType.Steel => "#607d8b",
        PokemonType.Fairy => "#c2588f",
        _ => "#9e9e7e",
    };

    public static SurvivalWeapon WeaponFor(SkillInfo skill, bool sameType)
    {
        string kind = KindFor(skill);
        double power = skill.Category == SkillCategory.Status ? 30 : Math.Clamp(skill.Power > 0 ? skill.Power : 50, 30, 150);
        if (sameType) power *= 1.25;

        double cooldown = 1;
        if (skill.Priority > 0) cooldown *= 0.85;
        if (skill.NeedsRecharge || skill.IsCharging)
        {
            cooldown *= 1.4;
            power *= 1.3;
        }

        int extra = skill.MaxHits > 1 ? Math.Min(2, skill.MaxHits - 1) : 0;
        if (extra > 0) power *= 0.7;
        bool slow = skill.Type == PokemonType.Ice || skill.Ailment is SkillAilment.Paralysis or SkillAilment.Freeze or SkillAilment.Sleep;
        return new SurvivalWeapon(skill.Id, kind, skill.Type.ToString().ToLowerInvariant(), ColorFor(skill.Type), power, cooldown, extra, slow);
    }

    // 다른 사람이 올린 값은 믿지 않고 게임 안의 범위로 맞춥니다.
    public static SurvivalEntry? Sanitize(SurvivalEntry entry, PokemonDex dex)
    {
        if (string.IsNullOrEmpty(entry.Uid) || dex.Get(entry.SpeciesId) is null) return null;
        int time = Math.Clamp(entry.TimeSec, 0, RUN_SECONDS);
        int kills = Math.Clamp(entry.Kills, 0, time * MAX_KILLS_PER_SECOND);
        bool cleared = entry.Cleared && time >= RUN_SECONDS;
        return entry with
        {
            Tag = entry.Tag.Length is > 0 and <= 12 ? entry.Tag : ArenaRules.TagFor(entry.Uid),
            Level = Math.Clamp(entry.Level, 1, ArenaRules.MAX_LEVEL),
            Form = !string.IsNullOrEmpty(entry.Form) && dex.HasForm(entry.SpeciesId, entry.Form) ? entry.Form : "",
            TimeSec = time,
            Kills = kills,
            Cleared = cleared,
            Score = Score(time, kills, cleared),
            Nickname = ArenaNames.Check(entry.Nickname) == NicknameProblem.None ? entry.Nickname : null,
            Moves = (entry.Moves ?? []).Take(4).Where(id => id is > 0 and <= 2000).ToArray(),
            MoveLevels = (entry.MoveLevels ?? []).Take(4).Select(level => Math.Clamp(level, 1, MAX_MOVE_LEVEL)).ToArray(),
        };
    }

    public static string Display(SurvivalEntry entry) =>
        string.IsNullOrEmpty(entry.Nickname) ? Utils.L.F("web.arena.trainer", entry.Tag) : entry.Nickname;

    // 강화 아이콘 (게임 화면 왼쪽 아래도 같은 그림 문자를 씁니다)
    public static string PassiveIcon(string? name) => name switch
    {
        "might" => "💪",
        "swift" => "👟",
        "vital" => "❤️",
        "haste" => "⏱️",
        "magnet" => "🧲",
        "armor" => "🛡️",
        _ => "✨",
    };

    // 공격 모양 아이콘
    public static string KindIcon(string kind) => kind switch
    {
        "bolt" => "🎯",
        "spray" => "🌊",
        "slash" => "⚔️",
        "nova" => "💥",
        "orbit" => "🌀",
        "chain" => "⚡",
        "aura" => "🔮",
        _ => "•",
    };

    public static string Clock(int seconds) => $"{seconds / 60:D2}:{seconds % 60:D2}";
}
