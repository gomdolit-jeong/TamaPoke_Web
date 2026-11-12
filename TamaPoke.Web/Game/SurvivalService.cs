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

    // 기술 4개 고르기 (판마다 무작위)
    // 후보: 진화 계열 전체(갈래 포함)가 Lv.50까지 레벨업으로 배우는 기술 + 실전(Pokémon Showdown 랜덤 배틀)에서 쓰는 대표 기술.
    // 무게: 위력이 셀수록, 내 타입일수록, 대표 기술일수록(가장 많이 쓰는 기술은 5배) 잘 뽑힙니다.
    //       이미 뽑은 기술과 공격 모양이 같으면 1/4로 줄어 되도록 겹치지 않게 하고, 변화 기술(오라)은 1개까지입니다.
    // 처음부터 쓰는 기술(맨 앞)은 오라가 아닌 내 타입 공격 기술 중에서 뽑습니다.
    // 뽑은 기술은 게임을 시작할 때까지 기억해서, 고르는 화면에 보인 기술이 그대로 게임에 나옵니다.
    private const int FAMOUS_MIN_SHARE = 8;     // 가장 많이 쓰는 기술의 1/8 미만으로 나온 기술은 대표 기술로 치지 않습니다.
    private const double FAMOUS_WEIGHT = 4;     // 대표 기술 무게: 1 + 4 × (자주 쓰는 정도 0~1)
    private const double SAME_SHAPE_WEIGHT = 0.25;

    private readonly Dictionary<int, int[]> _rolled = [];

    private List<SkillInfo> PickMoves(int speciesId, PokemonType type1, PokemonType type2)
    {
        if (!_rolled.TryGetValue(speciesId, out var ids))
            _rolled[speciesId] = ids = RollMoves(speciesId, type1, type2).Select(s => s.Id).ToArray();
        return ids.Select(skills.GetSkill).OfType<SkillInfo>().ToList();
    }

    private List<SkillInfo> RollMoves(int speciesId, PokemonType type1, PokemonType type2)
    {
        var line = StagesOf(speciesId).SelectMany(stage => stage).Distinct().ToList();
        bool SameType(SkillInfo s) => s.Type == type1 || s.Type == type2;
        bool IsStatus(SkillInfo s) => s.Category == SkillCategory.Status;
        bool IsAura(SkillInfo s) => SurvivalRules.KindFor(s) == "aura";

        var fame = new Dictionary<int, int>();
        foreach (int id in line)
            foreach (var (moveId, weight) in skills.GetFamousMoves(id))
                fame[moveId] = fame.GetValueOrDefault(moveId) + weight;
        int topFame = fame.Count > 0 ? fame.Values.Max() : 1;
        foreach (int moveId in fame.Where(p => p.Value * FAMOUS_MIN_SHARE < topFame).Select(p => p.Key).ToList()) fame.Remove(moveId);

        var pool = line
            .SelectMany(id => skills.GetActiveLearnset(id))
            .Where(m => m.Level is > 0 and <= SKILL_LEVEL)
            .Select(m => m.MoveId)
            .Concat(fame.Keys)
            .Distinct()
            .Select(skills.GetSkill)
            .OfType<SkillInfo>()
            .Where(s => s.IsSupported)
            .ToList();

        double Weight(SkillInfo s)
        {
            double famous = fame.GetValueOrDefault(s.Id) / (double)topFame; // 0~1
            if (IsStatus(s)) return famous > 0 ? 1 + 3 * famous : 0.3;
            if (s.Power < 40 && famous == 0) return 0.15;
            return Math.Clamp(s.Power, 40, 120) / 80.0 * (SameType(s) ? 1.6 : 1) * (1 + FAMOUS_WEIGHT * famous);
        }

        SkillInfo? Draw(IEnumerable<SkillInfo> candidates, Func<SkillInfo, double> extra)
        {
            var weighted = candidates.Select(s => (Skill: s, Weight: Weight(s) * extra(s))).Where(x => x.Weight > 0).ToList();
            double roll = Random.Shared.NextDouble() * weighted.Sum(x => x.Weight);
            foreach (var (skill, weight) in weighted)
            {
                roll -= weight;
                if (roll <= 0) return skill;
            }
            return weighted.Count > 0 ? weighted[^1].Skill : null;
        }

        var picked = new List<SkillInfo>();
        var attacks = pool.Where(s => !IsStatus(s) && !IsAura(s)).ToList();
        var starters = attacks.Where(SameType).ToList();
        if (Draw(starters.Count > 0 ? starters : attacks, _ => 1) is { } first)
            picked.Add(first);
        while (picked.Count < 4)
        {
            var next = Draw(pool.Where(s => !picked.Contains(s) && !(IsStatus(s) && picked.Any(IsStatus))),
                s => picked.Any(p => SurvivalRules.KindFor(p) == SurvivalRules.KindFor(s)) ? SAME_SHAPE_WEIGHT : 1);
            if (next is null) break;
            picked.Add(next);
        }

        if (picked.All(IsStatus))
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
        _rolled.Remove(speciesId); // 다음 판에는 기술을 새로 뽑습니다.
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
        var (moves, moveLevels) = SurvivalRules.MovesOf(result);
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
            Moves = moves,
            MoveLevels = moveLevels,
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
