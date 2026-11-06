using System.Globalization;
using System.Text.Json.Serialization;

namespace TamaPoke.Web.Game;

// 대전에 등록한 포켓몬 (Firestore arena/{시즌}/players/{uid} 문서 한 개)
// 상대는 이 정보로 만든 포켓몬과 싸우고, 컴퓨터가 상대 포켓몬을 조종합니다.
public sealed record ArenaEntry
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Uid { get; init; }
    public string Tag { get; init; } = "";
    public int SpeciesId { get; init; }
    public int Level { get; init; } = 1;
    public string Form { get; init; } = "";
    public bool Shiny { get; init; }
    public bool Female { get; init; }
    public int[] Skills { get; init; } = new int[4];
    public int[] Genes { get; init; } = new int[4];    // 체력, 공격, 방어, 스피드
    public int[] Training { get; init; } = new int[3]; // 공격, 방어, 스피드 훈련
    public int Points { get; init; } = ArenaRules.START_POINTS;
    public int Wins { get; init; }
    public int Losses { get; init; }

    // 로그인한 사람이 정한 닉네임 (영어 글자와 숫자). 정하지 않았으면 없습니다.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Nickname { get; init; }
}

public sealed class ArenaBoardData
{
    public ArenaEntry? Me { get; set; }
    public List<ArenaEntry> Top { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ArenaEntry))]
[JsonSerializable(typeof(ArenaBoardData))]
internal sealed partial class ArenaJsonContext : JsonSerializerContext;

// 대전 규칙: 각자 키운 레벨 그대로 싸우고(최대 100), 점수는 주마다 새로 시작합니다.
public static class ArenaRules
{
    public const int MAX_LEVEL = 100;
    public const int NEAR_LEVEL = 10; // 랜덤 상대는 레벨 차이가 이 안쪽인 트레이너부터 고릅니다.
    public const int START_POINTS = 1000;
    public const int MAX_GENE = 31;
    public const int MAX_TRAINING = 100;

    // 시즌: 세계 어디서나 같도록 UTC 기준 ISO 주 번호 (예: 2026-W45)
    public static string SeasonKey(DateTime utcNow) => $"{ISOWeek.GetYear(utcNow)}-W{ISOWeek.GetWeekOfYear(utcNow):D2}";

    public static (DateTime Start, DateTime End) SeasonRange(DateTime utcNow)
    {
        var start = ISOWeek.ToDateTime(ISOWeek.GetYear(utcNow), ISOWeek.GetWeekOfYear(utcNow), DayOfWeek.Monday);
        return (start, start.AddDays(6));
    }

    // 계정 정보를 드러내지 않는 짧은 이름표 (#AB12)
    public static string TagFor(string uid) => "#" + new string(uid.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();

    // 점수 변화: 점수와 레벨로 예상 승률을 내고, 예상을 뒤집을수록 크게 움직입니다. (체스 랭킹 Elo 방식)
    // 레벨은 한 단계마다 점수 10점으로 칩니다. 한 레벨 차이도 점수에 1점씩 드러납니다.
    // 예상 승률이 50%면 이기면 +20, 지면 -20이고, 레벨이 20 높은 상대면 +30 / -10입니다.
    public const int POINTS_PER_LEVEL = 10;
    public const int MAX_CHANGE = 40;
    public const int MIN_CHANGE = 2;
    private const double RATING_SCALE = 400;

    public static double Strength(ArenaEntry entry) => entry.Points + entry.Level * POINTS_PER_LEVEL;

    public static double WinChance(ArenaEntry me, ArenaEntry foe) => 1 / (1 + Math.Pow(10, (Strength(foe) - Strength(me)) / RATING_SCALE));

    public static int WinPoints(ArenaEntry me, ArenaEntry foe) =>
        Math.Clamp((int)Math.Round(MAX_CHANGE * (1 - WinChance(me, foe))), MIN_CHANGE, MAX_CHANGE);

    // 0점 아래로는 내려가지 않습니다.
    public static int LossPoints(ArenaEntry me, ArenaEntry foe) =>
        Math.Min(me.Points, Math.Clamp((int)Math.Round(MAX_CHANGE * WinChance(me, foe)), MIN_CHANGE, MAX_CHANGE));

    // 기록은 그대로 두고 포켓몬과 닉네임을 지금 세이브에 맞춥니다. (닉네임은 세이브에 있어서 시즌이 바뀌어도 남습니다)
    public static ArenaEntry Snapshot(PokemonState pet, string tag, ArenaEntry? record)
    {
        string? nickname = ArenaNames.Check(pet.ArenaNickname) == NicknameProblem.None ? pet.ArenaNickname : record?.Nickname;
        return new()
        {
            Tag = tag,
            Nickname = nickname,
            SpeciesId = pet.SpeciesId,
            Level = ArenaLevel(pet.Level),
            Form = pet.SpriteForm,
            Shiny = pet.IsShiny,
            Female = pet.IsFemale,
            Skills = [.. pet.Skills.Take(4)],
            Genes = [pet.Genes.HpGene, pet.Genes.AtkGene, pet.Genes.DefGene, pet.Genes.SpeGene],
            Training = [pet.TrAtk, pet.TrDef, pet.TrSpeed],
            Points = record?.Points ?? START_POINTS,
            Wins = record?.Wins ?? 0,
            Losses = record?.Losses ?? 0,
        };
    }

    public static int ArenaLevel(int level) => Math.Clamp(level, 1, MAX_LEVEL);


    // 레벨이 비슷한 상대 중에서 고르고, 없으면 레벨이 가장 가까운 쪽에서 고릅니다.
    public static ArenaEntry? PickRandomFoe(IReadOnlyList<ArenaEntry> foes, int myLevel, Random random)
    {
        if (foes.Count == 0) return null;
        var near = foes.Where(f => Math.Abs(f.Level - myLevel) <= NEAR_LEVEL).ToList();
        if (near.Count == 0)
        {
            int closest = foes.Min(f => Math.Abs(f.Level - myLevel));
            near = foes.Where(f => Math.Abs(f.Level - myLevel) == closest).ToList();
        }
        return near[random.Next(near.Count)];
    }

    public static bool SameSnapshot(ArenaEntry a, ArenaEntry b) =>
        a.Nickname == b.Nickname && a.Level == b.Level && a.Tag == b.Tag && a.SpeciesId == b.SpeciesId && a.Form == b.Form && a.Shiny == b.Shiny && a.Female == b.Female &&
        a.Skills.SequenceEqual(b.Skills) && a.Genes.SequenceEqual(b.Genes) && a.Training.SequenceEqual(b.Training);

    // 다른 사람이 올린 값은 믿지 않고 게임 안의 범위로 맞춥니다. 쓸 수 없는 포켓몬이면 null입니다.
    public static ArenaEntry? Sanitize(ArenaEntry entry, PokemonDex dex, SkillDex skills)
    {
        if (string.IsNullOrEmpty(entry.Uid) || dex.Get(entry.SpeciesId) is null) return null;

        int[] moves = new int[4];
        for (int i = 0; i < 4 && i < (entry.Skills?.Length ?? 0); i++)
        {
            int id = entry.Skills![i];
            if (skills.GetSkill(id) is { IsSupported: true }) moves[i] = id;
        }

        int Pick(int[]? values, int index, int max) => values is not null && index < values.Length ? Math.Clamp(values[index], 0, max) : 0;

        return entry with
        {
            Tag = entry.Tag.Length is > 0 and <= 12 ? entry.Tag : TagFor(entry.Uid),
            Level = ArenaLevel(entry.Level),
            Form = !string.IsNullOrEmpty(entry.Form) && dex.HasForm(entry.SpeciesId, entry.Form) ? entry.Form : "",
            Skills = moves,
            Genes = [Pick(entry.Genes, 0, MAX_GENE), Pick(entry.Genes, 1, MAX_GENE), Pick(entry.Genes, 2, MAX_GENE), Pick(entry.Genes, 3, MAX_GENE)],
            Training = [Pick(entry.Training, 0, MAX_TRAINING), Pick(entry.Training, 1, MAX_TRAINING), Pick(entry.Training, 2, MAX_TRAINING)],
            Points = Math.Max(0, entry.Points),
            Wins = Math.Max(0, entry.Wins),
            Losses = Math.Max(0, entry.Losses),
            Nickname = ArenaNames.Check(entry.Nickname) == NicknameProblem.None ? entry.Nickname : null,
        };
    }
}
