using System.Text.Json;
using Microsoft.JSInterop;
using TamaPoke.Web.Sprites;

namespace TamaPoke.Web.Game;

// 서바이벌 준비(적 포켓몬과 스프라이트)와 주간 랭킹(Firestore survival/{시즌}/players/{uid}).
// 기록은 더 좋은 점수일 때만 저장하고, 점수 계산과 저장 간격은 Firestore 규칙이 다시 확인합니다.
public sealed class SurvivalService(CloudSave cloud, PokemonDex dex, SkillDex skills, SpriteLoader sprites)
{
    public const int TOP_COUNT = 100;
    private const int ROSTER_SIZE = 8;

    public static string CurrentSeason => ArenaRules.SeasonKey(DateTime.UtcNow);

    // --- 고른 포켓몬 ---

    // 능력치는 모든 포켓몬이 같은 기준입니다. (Lv.50, 개체값 15, 훈련 없음) 종족값만 다르고, 강해지는 건 게임 안에서입니다.
    public const int BASE_LEVEL = 50;
    private const int BASE_GENE = 15;
    private const int SKILL_LEVEL = 50;

    // Stages: 진화 단계별 포켓몬 (처음 모습, 첫 진화 후보들, 두 번째 진화 후보들)
    public sealed record Pick(int SpeciesId, string SpritePath, string Form, bool Shiny, List<SurvivalWeapon> Weapons,
        int Hp, int Attack, int Defense, int Speed, List<int[]> Stages);

    private (int Hp, int Attack, int Defense, int Speed) StatsOf(int speciesId)
    {
        var info = dex.Get(speciesId);
        return (BattleCalculator.MaxHp(info?.BaseHp ?? 50, BASE_GENE, BASE_LEVEL),
            BattleCalculator.Stat(Math.Max(info?.BaseAtk ?? 50, info?.BaseSpA ?? 50), BASE_GENE, BASE_LEVEL),
            BattleCalculator.Stat(((info?.BaseDef ?? 50) + (info?.BaseSpD ?? 50)) / 2, BASE_GENE, BASE_LEVEL),
            BattleCalculator.Stat(info?.BaseSpeed ?? 50, BASE_GENE, BASE_LEVEL));
    }

    private int[] NextOf(int speciesId) => dex.GetEvolution(speciesId)?.NextSpeciesIds ?? [];

    private List<int[]> StagesOf(int speciesId)
    {
        var stages = new List<int[]> { new[] { speciesId } };
        for (int depth = 0; depth < SurvivalRules.EvolveLevels.Length; depth++)
        {
            int[] next = stages[^1].SelectMany(NextOf).Distinct().ToArray();
            if (next.Length == 0) break;
            stages.Add(next);
        }
        return stages;
    }

    // 게임 중 모습이 바뀌어도 능력치 계산은 같습니다.
    private static (double MaxHp, double Speed, double Power, double Guard) PlayerStats((int Hp, int Attack, int Defense, int Speed) s) =>
        (Math.Round(80 + s.Hp * 0.6), Math.Min(110, 62 + s.Speed * 0.18), 0.8 + s.Attack / 200.0, 100.0 / (100 + s.Defense / 2.0));

    // 지금 포켓몬을 고르면 지금 모습(폼, 이로치) 그대로, 다른 포켓몬은 기본 모습입니다.
    public Pick PickFor(PokemonState pet, int speciesId)
    {
        bool isLead = !pet.IsEgg && pet.SpeciesId == speciesId;
        var info = dex.Get(speciesId);
        var (type1, type2) = isLead ? dex.GetFormTypes(speciesId, pet.SpriteForm) : (info?.Type1 ?? PokemonType.Normal, info?.Type2 ?? PokemonType.None);

        var (hp, attack, defense, speed) = StatsOf(speciesId);

        var weapons = PickMoves(speciesId, type1, type2).Select(s => SurvivalRules.WeaponFor(s, s.Type == type1 || s.Type == type2)).ToList();
        return new Pick(speciesId,
            isLead ? pet.SpritePath : $"{speciesId:D4}",
            isLead ? pet.SpriteForm : "",
            isLead && pet.IsShiny,
            weapons, hp, attack, defense, speed, StagesOf(speciesId));
    }

    // Lv.50까지 레벨업으로 배우는 기술 중에서 4개: 공격 모양이 겹치지 않게 센 공격 기술 3개, 변화 기술 1개, 모자라면 남은 공격 기술.
    // 가장 센 공격 기술이 맨 앞(처음부터 쓰는 기술)입니다.
    private List<SkillInfo> PickMoves(int speciesId, PokemonType type1, PokemonType type2)
    {
        var learnable = skills.GetActiveLearnset(speciesId)
            .Where(m => m.Level is > 0 and <= SKILL_LEVEL)
            .Select(m => skills.GetSkill(m.MoveId))
            .OfType<SkillInfo>()
            .Where(s => s.IsSupported)
            .DistinctBy(s => s.Id)
            .ToList();

        int Score(SkillInfo s) => s.Power + (s.Type == type1 || s.Type == type2 ? 30 : 0);
        var attacks = learnable.Where(s => s.Category != SkillCategory.Status).OrderByDescending(Score).ToList();
        var statuses = learnable.Where(s => s.Category == SkillCategory.Status).ToList();

        var picked = new List<SkillInfo>();
        foreach (var skill in attacks)
        {
            if (picked.Count >= 3) break;
            if (picked.All(p => SurvivalRules.KindFor(p) != SurvivalRules.KindFor(skill))) picked.Add(skill);
        }
        if (statuses.Count > 0 && picked.All(p => SurvivalRules.KindFor(p) != "aura"))
            picked.Add(statuses.OrderByDescending(s => s.Ailment != SkillAilment.None).First());
        foreach (var skill in attacks)
        {
            if (picked.Count >= 4) break;
            if (!picked.Contains(skill)) picked.Add(skill);
        }

        if (picked.All(s => s.Category == SkillCategory.Status))
        {
            if (skills.GetSkill(skills.GetFallbackMove(type1)) is { } typed) picked.Insert(0, typed);
            else if (skills.GetSkill(SurvivalRules.DEFAULT_SKILL_ID) is { } tackle) picked.Insert(0, tackle);
        }
        return picked.Take(4).ToList();
    }

    // --- 게임 준비 ---

    public async Task<SurvivalSetup> BuildSetupAsync(PokemonState pet, int speciesId)
    {
        var random = new Random();
        var pick = PickFor(pet, speciesId);
        var info = dex.Get(speciesId);

        // 진화할 수 있는 모습을 모두 미리 준비합니다. (갈래가 여럿이면 모두)
        var ids = new List<(int SpeciesId, int Depth, string Path)> { (speciesId, 0, pick.SpritePath) };
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i].Depth >= SurvivalRules.EvolveLevels.Length) continue;
            foreach (int next in NextOf(ids[i].SpeciesId))
                if (ids.All(x => x.SpeciesId != next)) ids.Add((next, ids[i].Depth + 1, $"{next:D4}"));
        }
        var sprites = await Task.WhenAll(ids.Select(async x =>
            (Walk: await LoadSpriteAsync(x.Path, $"{x.SpeciesId:D4}", PokemonAnim.WALK),
             Idle: await LoadSpriteAsync(x.Path, $"{x.SpeciesId:D4}", PokemonAnim.IDLE))));
        var forms = ids.Select((x, i) =>
        {
            var (maxHp, speed, power, guard) = PlayerStats(StatsOf(x.SpeciesId));
            int[] next = NextOf(x.SpeciesId).Select(n => ids.FindIndex(y => y.SpeciesId == n)).Where(n => n > i).ToArray();
            if (x.Depth >= SurvivalRules.EvolveLevels.Length) next = [];
            return new SurvivalForm(x.SpeciesId, dex.GetName(x.SpeciesId, Utils.L.Language), sprites[i].Walk, sprites[i].Idle,
                maxHp, speed, power, guard, x.Depth, next);
        }).ToList();

        var weapons = pick.Weapons;
        int[] roster = dex.PickSurvivalRoster(pet.Settings.SelectedGenerations, ROSTER_SIZE, random);
        var enemyTasks = roster.Select(id => BuildEnemyAsync(id, weapons, isBoss: false)).ToList();
        var bossTasks = dex.PickSurvivalBosses(pet.Settings.SelectedGenerations, SurvivalRules.BossTimes.Length, random)
            .Select(id => BuildEnemyAsync(id, weapons, isBoss: true)).ToList();
        var enemies = (await Task.WhenAll(enemyTasks)).Where(e => e.Sprite is not null).ToList();
        if (enemies.Count == 0) enemies = [.. await Task.WhenAll(enemyTasks)];

        var bosses = (await Task.WhenAll(bossTasks)).ToList();
        while (bosses.Count < SurvivalRules.BossTimes.Length) bosses.Add(bosses[^1]);
        return new SurvivalSetup(forms, SurvivalRules.EvolveLevels, weapons, enemies, bosses, SurvivalRules.BossTimes, pet.IsMuted,
            Utils.L.T("web.survival.swarm"), Utils.L.T("web.survival.evolve.text"), Utils.L.T("web.survival.boss"),
            new Dictionary<string, string>
            {
                ["magnet"] = Utils.L.T("web.survival.item.magnet"),
                ["ice"] = Utils.L.T("web.survival.item.ice"),
                ["bomb"] = Utils.L.T("web.survival.item.bomb"),
                ["flyby"] = Utils.L.T("web.survival.flyby"),
            });
    }

    private async Task<SurvivalEnemy> BuildEnemyAsync(int speciesId, List<SurvivalWeapon> weapons, bool isBoss)
    {
        var info = dex.Get(speciesId);
        string path = $"{speciesId:D4}";
        var sprite = await LoadSpriteAsync(path, path, PokemonAnim.WALK);

        double[] eff = weapons.Select(w =>
        {
            var type = Enum.TryParse<PokemonType>(w.Type, ignoreCase: true, out var t) ? t : PokemonType.Normal;
            double multiplier = TypeMatchupHelper.GetMultiplier(type, info?.Type1 ?? PokemonType.None)
                * TypeMatchupHelper.GetMultiplier(type, info?.Type2 ?? PokemonType.None);
            return multiplier == 0 ? 0.35 : Math.Clamp(multiplier, 0.25, 4);
        }).ToArray();

        // 보스는 게임에서 체력을 크게 늘립니다. 여기서는 종족값만 반영합니다.
        double hp = 4 + (info?.BaseHp ?? 50) / 10.0;
        double speed = Math.Clamp(30 + (info?.BaseSpeed ?? 50) * 0.22, 32, isBoss ? 52 : 60);
        double damage = 2 + (info?.BaseAtk ?? 50) / 25.0;
        return new SurvivalEnemy(speciesId, dex.GetName(speciesId, Utils.L.Language), sprite, hp, speed, damage, eff);
    }

    private async Task<SurvivalSprite?> LoadSpriteAsync(string path, string fallback, int action)
    {
        foreach (string candidate in path == fallback ? [path] : new[] { path, fallback })
        {
            try
            {
                var strip = await sprites.GetStripAsync(candidate, action);
                return new SurvivalSprite($"data:image/png;base64,{Convert.ToBase64String(strip.Png)}",
                    strip.FrameWidth, strip.FrameHeight, strip.Columns, strip.Rows, strip.Durations);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Xml.XmlException)
            {
                Console.WriteLine($"서바이벌 스프라이트 로드 실패 {candidate}: {ex.Message}");
            }
        }
        return null;
    }

    // --- 랭킹 ---

    public async Task<SurvivalBoard?> LoadAsync()
    {
        if (cloud.UserId is not { } uid) return null;
        string season = CurrentSeason;
        try
        {
            string raw = await cloud.CallAsync<string>("survivalLoad", season, TOP_COUNT);
            var data = JsonSerializer.Deserialize(raw, SurvivalJsonContext.Default.SurvivalBoardData) ?? new SurvivalBoardData();
            var me = data.Me is { } stored ? SurvivalRules.Sanitize(stored, dex) : null;
            var top = data.Top.Select(e => SurvivalRules.Sanitize(e, dex)).OfType<SurvivalEntry>()
                .OrderByDescending(e => e.Score).Take(TOP_COUNT).ToList();
            int index = top.FindIndex(e => e.Uid == uid);
            return new SurvivalBoard(season, me, index >= 0 ? index + 1 : null, top);
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"서바이벌 랭킹 읽기 실패: {ex.Message}");
            return null;
        }
    }

    public enum SubmitOutcome { NotSignedIn, NotBetter, Saved, Failed }

    // 이번 주 내 기록보다 좋으면 저장합니다. 저장한 뒤의 순위표도 돌려줍니다.
    public async Task<(SubmitOutcome Outcome, SurvivalBoard? Board)> SubmitAsync(PokemonState pet, int speciesId, SurvivalResult result)
    {
        if (cloud.UserId is not { } uid) return (SubmitOutcome.NotSignedIn, null);
        var board = await LoadAsync();
        if (board is null) return (SubmitOutcome.Failed, null);

        result = SurvivalRules.Clamp(result);
        int score = SurvivalRules.Score(result);
        if (board.Me is { } best && best.Score >= score) return (SubmitOutcome.NotBetter, board);

        var pick = PickFor(pet, speciesId);
        // 랭킹에는 끝까지 진화한 모습으로 올립니다. (고른 포켓몬의 진화 계통이 아니면 고른 포켓몬)
        int finalSpecies = result.FinalSpeciesId > 0 && pick.Stages.Any(stage => stage.Contains(result.FinalSpeciesId)) ? result.FinalSpeciesId : pick.SpeciesId;
        var entry = new SurvivalEntry
        {
            Tag = ArenaRules.TagFor(uid),
            Nickname = ArenaNames.Check(pet.ArenaNickname) == NicknameProblem.None ? pet.ArenaNickname : board.Me?.Nickname,
            SpeciesId = finalSpecies,
            Level = Math.Clamp(result.Level, 1, ArenaRules.MAX_LEVEL), // 게임 안에서 도달한 레벨
            Form = finalSpecies == pick.SpeciesId ? pick.Form : "",
            Shiny = pick.Shiny,
            Score = score,
            TimeSec = result.TimeSec,
            Kills = result.Kills,
            Cleared = result.Cleared,
        };

        try
        {
            string json = JsonSerializer.Serialize(entry, SurvivalJsonContext.Default.SurvivalEntry);
            string error = await cloud.CallAsync<string>("survivalSave", board.Season, json);
            if (error.Length > 0)
            {
                Console.WriteLine($"서바이벌 기록 저장 실패: {error}");
                return (SubmitOutcome.Failed, board);
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            Console.WriteLine($"서바이벌 기록 저장 실패: {ex.Message}");
            return (SubmitOutcome.Failed, board);
        }

        var me = entry with { Uid = uid };
        var top = board.Top.Where(e => e.Uid != uid).Append(me).OrderByDescending(e => e.Score).Take(TOP_COUNT).ToList();
        int index = top.FindIndex(e => e.Uid == uid);
        return (SubmitOutcome.Saved, new SurvivalBoard(board.Season, me, index >= 0 ? index + 1 : null, top));
    }
}
