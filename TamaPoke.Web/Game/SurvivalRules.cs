using System.Text.Json.Serialization;

namespace TamaPoke.Web.Game;

// --- 게임(wwwroot/js/survival.js)에 넘기는 값 ---

public sealed record SurvivalSprite(string Url, int Fw, int Fh, int Cols, int Rows, int[] Durations);

// 게임 중의 모습 하나. Forms[0]이 처음 모습이고, Next는 진화할 수 있는 모습의 번호(Forms 안)입니다. Depth는 몇 번 진화한 모습인지.
// EvolveLevel: 게임 안 레벨이 이만큼 되면 다음 모습으로 진화합니다. (진화하지 않으면 0)
// Portraits: 화면 왼쪽 위에 보일 얼굴 그림 주소. 없는 그림이 있어서 앞에서부터 차례로 받아 봅니다.
public sealed record SurvivalForm(int SpeciesId, string Name, SurvivalSprite? Walk, SurvivalSprite? Idle,
    double MaxHp, double Speed, double Power, double Guard, int Depth, int[] Next, int EvolveLevel, string[] Portraits);

// 기술 하나 = 무기 하나. Kind는 공격 모양(bolt, spray, slash, nova, orbit, chain, aura), Type은 효과 그림(fx_shot_{type}.png)입니다.
// Name은 화면 오른쪽 기술 목록에 보일 기술 이름입니다.
// SameType: 내 타입 기술인지, MovePower: 원래 기술 위력 (특성 효과 적응력·테크니션에 씀)
// Fx: 기술 효과 그림 (wwwroot/pfx, SurvivalRules.FxFor). 같은 모양이라도 기술마다 다르게 보입니다.
// Crit: 원작 급소 랭크 (0 보통, 1 급소에 잘 맞는 기술, 3 이상 반드시 급소)
public sealed record SurvivalWeapon(int SkillId, string Kind, string Type, string Color, double Power, double Cooldown, int Extra, bool Slow, string Name,
    bool SameType = false, int MovePower = 0, string Fx = "", int Crit = 0);

// 이번 판의 특성. Key는 서바이벌 효과(SurvivalRules.TraitOf), Ability는 원래 특성, Type은 효과가 걸리는 타입(소문자, 없으면 "")
public sealed record SurvivalTrait(string Key, string Ability, string Name, string Type);

// 적 한 종류. Eff는 내 무기(순서대로)가 이 포켓몬에게 주는 타입 상성 배율입니다.
public sealed record SurvivalEnemy(int SpeciesId, string Name, SurvivalSprite? Sprite, double Hp, double Speed, double Damage, double[] Eff);

// 이번 주 순위표의 한 줄 (게임 중 "다음 순위" 표시용). Me는 내 기록.
public sealed record SurvivalRival(int Rank, string Name, int TimeSec, int Kills, bool Me);

public sealed record SurvivalSetup(List<SurvivalForm> Forms, List<SurvivalWeapon> Weapons, List<SurvivalEnemy> Enemies,
    List<SurvivalEnemy> Bosses, int[] BossTimes, bool Muted, string SwarmText, string EvolveText, string BossText,
    Dictionary<string, string> Texts)
{
    // 이번 주 순위표 (로그인하지 않았으면 비어 있음)
    public List<SurvivalRival> Rivals { get; init; } = [];

    // 이번 판의 특성 (없으면 null)
    public SurvivalTrait? Trait { get; init; }

    // 저주 단계 (0~SurvivalRules.MAX_CURSE): 적이 강해지고 많아지는 대신 경험치를 더 받습니다.
    public int Curse { get; init; }

    // 저주 1단계마다 늘어나는 정도 (survival.js가 그대로 씀)
    public double CurseHp { get; init; } = SurvivalRules.CURSE_HP;
    public double CurseSpawn { get; init; } = SurvivalRules.CURSE_SPAWN;
    public double CurseDamage { get; init; } = SurvivalRules.CURSE_DAMAGE;
    public double CurseSpeed { get; init; } = SurvivalRules.CURSE_SPEED;
    public double CurseExp { get; init; } = SurvivalRules.CURSE_EXP;
}

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
    public bool[] WeaponEvolved { get; init; } = []; // 기술 진화를 했는지
    public int[] WeaponUnion { get; init; } = [];    // 합체: 합친 기술이면 짝의 번호, 짝에게 흡수됐으면 -2, 아니면 -1
    public int ItemDamage { get; init; }
    public int ItemKills { get; init; }

    public int BestCombo { get; init; } // 가장 길게 이어 간 연속 처치 (결과 화면에만 보임)
    public int Curse { get; init; }     // 이 판의 저주 단계
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
    public bool[] WeaponEvolved { get; init; } = [];
    public int[] WeaponDamage { get; init; } = [];
    public int[] WeaponKills { get; init; } = [];
    public int[] WeaponUnion { get; init; } = [];
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
    public int Partner { get; init; } = -1; // union(기술 합체): Index와 합칠 기술 번호
}

// 레벨업 고르는 화면 하나: 카드와 남은 다시 뽑기·건너뛰기·지우기 횟수 (-1이면 그 버튼을 숨김, 진화 고르기 등)
public sealed record SurvivalChoice
{
    public List<SurvivalOption> Options { get; init; } = [];
    public int Reroll { get; init; } = -1;
    public int Skip { get; init; } = -1;
    public int Banish { get; init; } = -1;
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

    // 그 판의 저주 단계 (예전 기록에는 없어서 0). 0이면 저장하지 않습니다. (Firestore 규칙을 배포하기 전에도 저주 없는 기록은 저장되도록)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Curse { get; init; }
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
[JsonSerializable(typeof(SurvivalChoice))]
[JsonSerializable(typeof(SurvivalEntry))]
[JsonSerializable(typeof(SurvivalBoardData))]
internal sealed partial class SurvivalJsonContext : JsonSerializerContext;

public static class SurvivalRules
{
    public const int RUN_SECONDS = 600;       // 이만큼 버티면 클리어
    public const int MAX_RUN_SECONDS = 3600;  // 무한 모드(클리어 뒤 계속하기)는 60분에 끝납니다.
    public const int MAX_KILLS_PER_SECOND = 40; // 저주를 올리면 적이 많아져서 넉넉하게 (Firestore 규칙과 같게)
    public const int MAX_KILLS = SCORE_TIME_UNIT - 1; // 점수 식(시간 × 100000 + KO)이 겹치지 않도록

    // 저주: 시작 전에 0~MAX_CURSE단계를 고릅니다. 1단계마다 적 체력·수·공격·속도가 늘고, 경험치도 더 받습니다.
    public const int MAX_CURSE = 5;
    public const double CURSE_HP = 0.20;
    public const double CURSE_SPAWN = 0.12;
    public const double CURSE_DAMAGE = 0.10;
    public const double CURSE_SPEED = 0.03;
    public const double CURSE_EXP = 0.10;
    public const int MIN_ENERGY = 20;
    public const int DEFAULT_SKILL_ID = 33; // 기술이 하나도 없으면 몸통박치기

    // 진화는 원작(도감)의 진화 레벨 그대로 합니다. (돌, 교환 등 레벨이 아닌 진화는 도감처럼 Lv.36으로 셈)
    // 예: 파이리 Lv.16 → 리자드 Lv.36, 딥상어동 Lv.24 → 한바이트 Lv.48, 잉어킹 Lv.20
    public const int MAX_EVOLUTIONS = 2;

    public static int EvolveLevel(int dexLevel) => Math.Max(1, dexLevel);

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

    public static int ClampKills(int kills, int time) => Math.Clamp(kills, 0, Math.Min(time * MAX_KILLS_PER_SECOND, MAX_KILLS));

    public static SurvivalResult Clamp(SurvivalResult result)
    {
        int time = Math.Clamp(result.TimeSec, 0, MAX_RUN_SECONDS);
        return result with
        {
            TimeSec = time,
            Kills = ClampKills(result.Kills, time),
            Level = Math.Clamp(result.Level, 1, 999),
            Cleared = result.Cleared && time >= RUN_SECONDS,
            Curse = Math.Clamp(result.Curse, 0, MAX_CURSE),
        };
    }

    // --- 특성: 원작 특성을 서바이벌 효과(아래 TRAITS의 값)로 바꿉니다. 효과 수치는 survival.js의 TRAIT와 같게. ---
    // 자주 나오는 특성 위주로 효과를 정했고, 표에 없는 특성은 "basic"(위기 때 내 첫 타입 기술 강화, 약하게)입니다.
    // 값: (효과, 효과가 걸리는 타입. ""이면 타입과 상관없음)
    private static readonly Dictionary<string, (string Trait, string Type)> TRAITS = new()
    {
        // 위기 때 그 타입 기술 위력 ↑ (원작: 체력 1/3 이하에서 1.5배)
        ["overgrow"] = ("pinch", "grass"), ["blaze"] = ("pinch", "fire"), ["torrent"] = ("pinch", "water"), ["swarm"] = ("pinch", "bug"),
        // 받는 데미지 ↓
        ["intimidate"] = ("intimidate", ""), ["unnerve"] = ("intimidate", ""),
        ["thickfat"] = ("guard", ""), ["filter"] = ("guard", ""), ["solidrock"] = ("guard", ""), ["prismarmor"] = ("guard", ""),
        ["furcoat"] = ("guard", ""), ["shellarmor"] = ("guard", ""), ["battlearmor"] = ("guard", ""), ["magicguard"] = ("guard", ""),
        ["overcoat"] = ("guard", ""), ["heatproof"] = ("guard", ""), ["icescales"] = ("guard", ""), ["fluffy"] = ("guard", ""),
        ["multiscale"] = ("multiscale", ""), ["shadowshield"] = ("multiscale", ""),
        ["sturdy"] = ("sturdy", ""),
        // 회복
        ["regenerator"] = ("regen", ""),
        ["naturalcure"] = ("regenLite", ""), ["shedskin"] = ("regenLite", ""), ["hydration"] = ("regenLite", ""),
        ["raindish"] = ("regenLite", ""), ["icebody"] = ("regenLite", ""), ["poisonheal"] = ("regenLite", ""),
        ["waterabsorb"] = ("absorb", ""), ["voltabsorb"] = ("absorb", ""), ["flashfire"] = ("absorb", ""), ["sapsipper"] = ("absorb", ""),
        ["lightningrod"] = ("absorb", ""), ["stormdrain"] = ("absorb", ""), ["motordrive"] = ("absorb", ""), ["dryskin"] = ("absorb", ""),
        ["gluttony"] = ("glutton", ""), ["cheekpouch"] = ("glutton", ""), ["ripen"] = ("glutton", ""),
        // 공격 ↑
        ["adaptability"] = ("adaptability", ""),
        ["protean"] = ("protean", ""), ["libero"] = ("protean", ""),
        ["technician"] = ("technician", ""), ["skilllink"] = ("technician", ""),
        ["hugepower"] = ("brute", ""), ["purepower"] = ("brute", ""), ["hustle"] = ("brute", ""), ["gorillatactics"] = ("brute", ""),
        ["sheerforce"] = ("power", ""), ["toughclaws"] = ("power", ""), ["strongjaw"] = ("power", ""), ["ironfist"] = ("power", ""),
        ["reckless"] = ("power", ""), ["sharpness"] = ("power", ""), ["megalauncher"] = ("power", ""), ["punkrock"] = ("power", ""),
        ["guts"] = ("guts", ""), ["flareboost"] = ("guts", ""), ["toxicboost"] = ("guts", ""), ["marvelscale"] = ("guts", ""),
        ["moxie"] = ("moxie", ""), ["beastboost"] = ("moxie", ""), ["chillingneigh"] = ("moxie", ""), ["grimneigh"] = ("moxie", ""),
        ["asone"] = ("moxie", ""), ["battlebond"] = ("moxie", ""), ["supremeoverlord"] = ("moxie", ""),
        ["keeneye"] = ("crit", ""), ["compoundeyes"] = ("crit", ""), ["sniper"] = ("sniper", ""), ["superluck"] = ("crit", ""),
        // 날씨·필드: 그 타입 기술 위력 ↑
        ["drought"] = ("weather", "fire"), ["orichalcumpulse"] = ("weather", "fire"), ["desolateland"] = ("weather", "fire"),
        ["drizzle"] = ("weather", "water"), ["primordialsea"] = ("weather", "water"),
        ["sandstream"] = ("weather", "rock"), ["snowwarning"] = ("weather", "ice"),
        ["electricsurge"] = ("weather", "electric"), ["hadronengine"] = ("weather", "electric"),
        ["grassysurge"] = ("weather", "grass"), ["psychicsurge"] = ("weather", "psychic"), ["mistysurge"] = ("weather", "fairy"),
        ["deltastream"] = ("weather", "flying"), ["solarpower"] = ("weather", "fire"),
        // 이동 속도 ↑
        ["swiftswim"] = ("speed", ""), ["chlorophyll"] = ("speed", ""), ["sandrush"] = ("speed", ""), ["slushrush"] = ("speed", ""),
        ["surgesurfer"] = ("speed", ""), ["quickfeet"] = ("speed", ""), ["unburden"] = ("speed", ""), ["runaway"] = ("speed", ""),
        ["levitate"] = ("speed", ""),
        ["speedboost"] = ("speedboost", ""),
        // 닿은 적에게 반격
        ["static"] = ("contact", ""), ["flamebody"] = ("contact", ""), ["poisonpoint"] = ("contact", ""), ["effectspore"] = ("contact", ""),
        ["roughskin"] = ("contact", ""), ["ironbarbs"] = ("contact", ""), ["gooey"] = ("contact", ""), ["tanglinghair"] = ("contact", ""),
        ["cutecharm"] = ("contact", ""),
        // 그 밖
        ["pickup"] = ("pickup", ""), ["honeygather"] = ("pickup", ""), ["magnetpull"] = ("pickup", ""), ["frisk"] = ("pickup", ""),
        ["pressure"] = ("pressure", ""),
        ["serenegrace"] = ("serene", ""), ["shielddust"] = ("serene", ""),
    };

    // 특성 → 서바이벌 효과. 표에 없으면 기본 효과(basic, 내 첫 타입)
    public static (string Trait, string Type) TraitOf(string ability, PokemonType type1) =>
        TRAITS.TryGetValue(ability, out var trait) ? trait : ("basic", type1.ToString().ToLowerInvariant());

    public static bool HasOwnTrait(string ability) => TRAITS.ContainsKey(ability);

    // 기술 진화의 짝 강화: 공격 모양마다 하나 (survival.js의 EVOLVE_PAIR와 같게)
    // 기술 Lv.5 + 짝 강화 EVOLVE_PAIR_LEVEL단계 이상일 때 보스 상자(EVOLVE_ELITE_FROM_MINUTES분부터는 엘리트 상자도)를 열면 진화합니다.
    // (survival.js TUNE.evolvePairLevel, TUNE.evolveEliteFrom과 같게)
    public const int EVOLVE_PAIR_LEVEL = 2;
    public const int EVOLVE_ELITE_FROM_MINUTES = 6;
    public static string EvolvePair(string kind) => kind switch
    {
        "bolt" => "haste",
        "spray" => "might",
        "slash" => "armor",
        "nova" => "vital",
        "orbit" => "swift",
        "chain" => "magnet",
        _ => "armor",
    };

    // 판마다 뽑는 기술 4개의 모양 제한: 같은 모양은 MAX_SAME_SHAPE개까지, 범위 공격(AREA_KINDS)은 적어도 하나.
    // 한 마리씩 맞히는 모양(bolt)이나 주변만 도는 모양(orbit, aura)만 있으면 적이 쌓여 4~7분에 쓰러지는 판이 많았습니다. (봇 시뮬레이션)
    public const int MAX_SAME_SHAPE = 2;

    // 판마다 기술: 처음 칸 START_MOVES개 + 기술 합체로 빈 칸에 나올 여분 SPARE_MOVES개
    // 기술 합체: 진화한 같은 타입 기술 두 개를 상자에서 합치면 둘 다 계속 쓰면서 한 칸만 차지하고, 위력이 UNION_POWER_PERCENT% 오릅니다.
    public const int START_MOVES = 4;
    public const int SPARE_MOVES = 3;
    public const int UNION_POWER_PERCENT = 20;
    public static readonly string[] AREA_KINDS = ["spray", "nova", "slash", "chain"];

    // 공격 모양: 변화 기술은 오라, 그 밖에는 영어 이름(빔·펀치·지진 등)을 먼저 보고, 없으면 타입으로 정합니다.
    // 같은 타입 기술(거품광선과 파도타기 등)이 같은 효과로 겹쳐 보이지 않게 합니다.
    public static string KindFor(SkillInfo skill)
    {
        if (skill.Category == SkillCategory.Status) return "aura";
        string name = skill.NameEn ?? "";
        if (SLASH_WORDS.IsMatch(name)) return "slash";
        if (BOLT_WORDS.IsMatch(name)) return "bolt";
        if (NOVA_WORDS.IsMatch(name)) return "nova";
        return KindForType(skill.Type);
    }

    static readonly System.Text.RegularExpressions.Regex SLASH_WORDS = new(
        @"\b(Punch|Kick|Claw|Claws|Fang|Bite|Slash|Cutter|Blade|Chop|Tail|Whip|Crunch|Jab|Uppercut|Scratch|Smash|Hammer|Axe|Horn|Fist|Wing)\b");
    static readonly System.Text.RegularExpressions.Regex BOLT_WORDS = new(
        @"\b(Beam|Pulse|Ball|Shot|Gun|Bullet|Cannon|Sphere|Bomb|Missile|Needle|Dart|Arrow|Shuriken|Spike|Spikes|Ray|Orb)\b");
    static readonly System.Text.RegularExpressions.Regex NOVA_WORDS = new(
        @"\b(Quake|Earthquake|Explosion|Eruption|Burst|Shockwave)\b");

    // 효과 그림: 기술 이름에 맞는 그림을 먼저 고르고, 없으면 모양과 타입으로 고릅니다.
    // 그림은 회색이고 게임에서 기술 타입 색을 곱합니다. (tools/make_pfx_sheets.js)
    public static string FxFor(SkillInfo skill, string kind)
    {
        string name = skill.NameEn ?? "";
        bool Has(string words) => System.Text.RegularExpressions.Regex.IsMatch(name, $@"\b({words})\b");
        PokemonType type = skill.Type;
        return kind switch
        {
            "spray" when Has("Flamethrower|Flame|Blaze|Inferno|Burn") => "s_flame",
            "spray" when type is PokemonType.Water => "s_jet",
            "spray" when type is PokemonType.Ice => "s_frost",
            "spray" => "w_spray",

            "bolt" when Has("Bubble|Bubbles") => "w_orbit",
            "bolt" when Has("Needle|Dart|Arrow|Spike|Spikes|Pin|Shuriken|Sting") => "b_arrow",
            "bolt" when type is PokemonType.Fire => "b_fireball",
            "bolt" when type is PokemonType.Ice => "b_ice",
            "bolt" when Has("Beam|Ray|Laser|Cannon|Charge") || type is PokemonType.Electric or PokemonType.Steel => "b_laser",
            "bolt" when type is PokemonType.Psychic or PokemonType.Fairy or PokemonType.Flying or PokemonType.Dragon => "b_disc",
            "bolt" => "w_bolt",

            "slash" when Has("Claw|Claws|Scratch|Fury") => "w_slash",
            "slash" when Has("Fang|Bite|Crunch|Jaw") => "sl_double",
            "slash" when Has("Punch|Fist|Uppercut|Jab|Chop|Kick|Hit") => "sl_punch",
            "slash" when Has("Whip|Tail|Vine|Lash|Slap") => "sl_whip",
            "slash" when Has("Horn|Drill|Peck|Spear|Megahorn|Lunge") => "sl_thrust",
            "slash" when Has("Hammer|Smash|Slam|Crash|Wood|Body|Heavy") => "sl_hammer",
            "slash" when Has("Blade|Slash|Cutter|Cut|Sword|Edge|Axe|Wing") => "sl_sword",
            "slash" when type is PokemonType.Dark or PokemonType.Ghost => "sl_scythe",
            "slash" when type is PokemonType.Fighting => "sl_punch",
            "slash" when type is PokemonType.Steel => "sl_sword",
            "slash" when type is PokemonType.Grass => "sl_whip",
            "slash" when type is PokemonType.Dragon or PokemonType.Bug => "sl_double",
            "slash" => "w_slash",

            "nova" when Has("Quake|Earthquake|Magnitude|Bulldoze|Stomp|Fissure") => "n_slam",
            "nova" when type is PokemonType.Fire => "n_fire",
            "nova" when type is PokemonType.Ice => "n_frost",
            "nova" when type is PokemonType.Electric => "n_thunder",
            "nova" when type is PokemonType.Water => "n_splash",
            "nova" when type is PokemonType.Dark or PokemonType.Ghost => "n_dark",
            "nova" when Has("Explosion|Burst") || type is PokemonType.Normal or PokemonType.Dragon or PokemonType.Steel => "n_plasma",
            "nova" when type is PokemonType.Ground or PokemonType.Rock => "n_slam",
            "nova" => "nova",

            "chain" when Has("Thunder|Bolt|Strike|Wild") => "c_strike",
            "chain" => "w_chain",

            "orbit" when type is PokemonType.Grass or PokemonType.Bug => "o_leaf",
            "orbit" when type is PokemonType.Ghost or PokemonType.Fire => "o_flame",
            "orbit" when type is PokemonType.Fairy or PokemonType.Psychic => "o_arcane",
            "orbit" => "w_orbit",

            "aura" when type is PokemonType.Poison or PokemonType.Bug => "a_puddle",
            "aura" when type is PokemonType.Fairy || Has("Sunny|Light|Wish|Moon|Morning|Sun") => "a_sun",
            "aura" when type is PokemonType.Water or PokemonType.Ice => "a_whirl",
            _ => "w_aura",
        };
    }

    static string KindForType(PokemonType type) => type switch
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
        return new SurvivalWeapon(skill.Id, kind, skill.Type.ToString().ToLowerInvariant(), ColorFor(skill.Type), power, cooldown, extra, slow, skill.Name,
            sameType, skill.Category == SkillCategory.Status ? 0 : skill.Power, FxFor(skill, kind),
            skill.Category == SkillCategory.Status ? 0 : Math.Clamp(skill.CritRate, 0, 3));
    }

    // 다른 사람이 올린 값은 믿지 않고 게임 안의 범위로 맞춥니다.
    public static SurvivalEntry? Sanitize(SurvivalEntry entry, PokemonDex dex)
    {
        if (string.IsNullOrEmpty(entry.Uid) || dex.Get(entry.SpeciesId) is null) return null;
        int time = Math.Clamp(entry.TimeSec, 0, MAX_RUN_SECONDS);
        int kills = ClampKills(entry.Kills, time);
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
            Curse = Math.Clamp(entry.Curse, 0, MAX_CURSE),
        };
    }

    // 저주 설명 숫자: 적 체력 +%, 적 수 +%, 적 공격 +%, 경험치 +%
    public static (int Hp, int Spawn, int Damage, int Exp) CursePercents(int curse) =>
        ((int)Math.Round(curse * CURSE_HP * 100), (int)Math.Round(curse * CURSE_SPAWN * 100), (int)Math.Round(curse * CURSE_DAMAGE * 100), (int)Math.Round(curse * CURSE_EXP * 100));

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
