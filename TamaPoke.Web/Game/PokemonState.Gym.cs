using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 설정 (PC판 GameSettings와 같은 범위)
public sealed class GameSettings
{
    public const double MinPetScale = 0.5;
    public const double MaxPetScale = 5.0;

    public int FarewellAgeDays { get; set; } = 3;
    public List<int> SelectedGenerations { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    public int RoamingPokemonCount { get; set; } = 1;
    public double RoamingPetScale { get; set; } = 2.0;
    public bool UseNotifications { get; set; }
}

// 메가진화 폼 (wwwroot/data/mega_forms.json)
public sealed class MegaFormInfo
{
    public int SpeciesId { get; set; }
    public string Form { get; set; } = "";
    public PokemonType Type1 { get; set; }
    public PokemonType Type2 { get; set; }
    public int BaseHp { get; set; }
    public int BaseAtk { get; set; }
    public int BaseDef { get; set; }
    public int BaseSpeed { get; set; }
    public int BaseSpA { get; set; }
    public int BaseSpD { get; set; }
    public string StoneId { get; set; } = "";
    public string? StoneNameKo { get; set; }
    public string? StoneNameEn { get; set; }

    [JsonIgnore] public string StoneName => (L.Language == Loc.English ? StoneNameEn ?? StoneNameKo : StoneNameKo ?? StoneNameEn) ?? StoneId;
    [JsonIgnore] public bool IsPrimal => Form.StartsWith("Primal", StringComparison.OrdinalIgnoreCase);

    public PokemonInfo ToPokemonInfo() => new()
    {
        Id = SpeciesId, Type1 = Type1, Type2 = Type2,
        BaseHp = BaseHp, BaseAtk = BaseAtk, BaseDef = BaseDef, BaseSpeed = BaseSpeed, BaseSpA = BaseSpA, BaseSpD = BaseSpD,
    };
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<MegaFormInfo>))]
internal sealed partial class MegaJsonContext : JsonSerializerContext;

public sealed class MegaDex(HttpClient http, PokemonDex dex)
{
    private List<MegaFormInfo> _forms = [];

    public async Task InitializeAsync() =>
        _forms = await http.GetFromJsonAsync(BuildInfo.Versioned("data/mega_forms.json"), MegaJsonContext.Default.ListMegaFormInfo) ?? [];

    private bool HasSprite(MegaFormInfo form) => dex.HasForm(form.SpeciesId, form.Form);

    // 이미지까지 있어서 지금 메가진화할 수 있는 폼
    public List<MegaFormInfo> GetAvailableForms(int speciesId) => _forms.Where(f => f.SpeciesId == speciesId && HasSprite(f)).ToList();

    // 조각을 모을 수 있는 메가스톤: 스톤마다 하나씩, 그 스톤으로 메가진화하는 폼의 이미지가 있는 것만
    public List<MegaFormInfo> CollectibleStones() =>
        _forms.Where(f => !string.IsNullOrEmpty(f.StoneId) && HasSprite(f)).GroupBy(f => f.StoneId).Select(g => g.First()).ToList();

    public MegaFormInfo? FindStone(string stoneId) => _forms.FirstOrDefault(f => f.StoneId == stoneId);

    public string DisplayName(MegaFormInfo form)
    {
        string species = dex.GetName(form.SpeciesId, L.Language);
        if (form.IsPrimal) return L.F("form.primal.name", species);
        string letter = form.Form.Split('_').SkipWhile(t => !t.Equals("Mega", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? "";
        return L.F("form.mega.name", species, letter).Trim();
    }
}

// 체육관과 메가진화. 규칙은 PC판 PokemonState.Battle.cs / Mega.cs와 같습니다.
public sealed partial class PokemonState
{
    private const int GYM_PRIZE_POTIONS = 2, GYM_PRIZE_BALLS = 3, GYM_REMATCH_PRIZE_POTIONS = 1, GYM_REMATCH_PRIZE_BALLS = 1;
    public const int MEGA_SHARDS_PER_STONE = 10;
    public const int MEGA_RING_BADGES = 8;
    private const int WILD_SHARD_CHANCE = 10, GYM_SHARDS = 3, GYM_REMATCH_SHARDS = 1, WALK_SHARD_CHANCE = 30;
    private const int LEAD_SHARD_CHANCE = 50, PARTY_SHARD_CHANCE = 30;
    private const int MEGA_GLOW_MS = 2000, MEGA_MESSAGE_MS = 1800;

    // 저장되는 값
    public int GymBadges { get; set; }
    public bool HasMegaRing { get; set; }
    public Dictionary<string, int> MegaShards { get; set; } = [];
    public List<string> MegaStones { get; set; } = [];

    [JsonIgnore] public MegaDex Megas { get; set; } = null!;
    [JsonIgnore] public bool IsGymBattle { get; private set; }
    [JsonIgnore] public MegaFormInfo? ActiveMegaForm { get; private set; }
    [JsonIgnore] public bool IsMegaEvolving { get; private set; }
    [JsonIgnore] public bool IsMegaChoiceOpen { get; private set; }
    [JsonIgnore] public List<MegaFormInfo> MegaChoices { get; private set; } = [];
    [JsonIgnore] public string PlayerBattleName => ActiveMegaForm is { } form ? Megas.DisplayName(form) : Name;
    [JsonIgnore] public bool CanMegaEvolveNow => IsBattleOpen && !_megaUsedThisBattle && !IsMegaEvolving && !IsArenaBattle && ActiveMegaForm is null && GetUsableMegaForms(SpeciesId).Count > 0;

    private int _selectedGymIndex;
    private bool _isGymRematch;
    private bool _megaUsedThisBattle;

    public bool HasMegaStone(string stoneId) => MegaStones.Contains(stoneId);

    // 이 포켓몬이 지금 메가진화할 수 있는 폼: 메가링 + 메가스톤(레쿠쟈는 불필요) + 이미지
    public List<MegaFormInfo> GetUsableMegaForms(int speciesId) =>
        !HasMegaRing ? [] : Megas.GetAvailableForms(speciesId).Where(f => string.IsNullOrEmpty(f.StoneId) || HasMegaStone(f.StoneId)).ToList();

    private bool TryGrantMegaRing()
    {
        if (HasMegaRing || GymBadges < MEGA_RING_BADGES) return false;
        HasMegaRing = true;
        NeedsSave = true;
        return true;
    }

    // 메가스톤 조각: 대표 → 파티 → 아무 스톤 순으로 잘 나옵니다. 받은 내용을 메시지로 돌려줍니다.
    private string? GiveMegaShards(int amount)
    {
        if (!HasMegaRing || amount <= 0) return null;

        var candidates = Megas.CollectibleStones().Where(s => !HasMegaStone(s.StoneId)).ToList();
        if (candidates.Count == 0) return null;

        var leadStones = candidates.Where(s => s.SpeciesId == SpeciesId).ToList();
        var partyIds = Party.Select(m => m.SpeciesId).ToHashSet();
        var partyStones = candidates.Where(s => s.SpeciesId != SpeciesId && partyIds.Contains(s.SpeciesId)).ToList();

        int roll = Random.Shared.Next(100);
        var pool = roll < LEAD_SHARD_CHANCE && leadStones.Count > 0 ? leadStones
                 : roll < LEAD_SHARD_CHANCE + PARTY_SHARD_CHANCE && partyStones.Count > 0 ? partyStones
                 : candidates;
        var stone = pool[Random.Shared.Next(pool.Count)];

        int count = Math.Min(MEGA_SHARDS_PER_STONE, MegaShards.GetValueOrDefault(stone.StoneId) + amount);
        NeedsSave = true;
        if (count >= MEGA_SHARDS_PER_STONE)
        {
            MegaShards.Remove(stone.StoneId);
            MegaStones.Add(stone.StoneId);
            return L.F("mega.stone.complete", stone.StoneName);
        }
        MegaShards[stone.StoneId] = count;
        return L.F("mega.shard.got", stone.StoneName, count, MEGA_SHARDS_PER_STONE);
    }

    private void ResetMegaForBattle()
    {
        _megaUsedThisBattle = false;
        IsMegaChoiceOpen = false;
        IsMegaEvolving = false;
        ActiveMegaForm = null;
    }

    public void RequestMegaEvolution()
    {
        if (!CanMegaEvolveNow || !IsPlayerTurn) return;
        MarkBattleAttended();

        var forms = GetUsableMegaForms(SpeciesId);
        if (forms.Count == 1)
        {
            _ = MegaEvolveAsync(forms[0]);
            return;
        }
        MegaChoices = forms;
        IsMegaChoiceOpen = true;
    }

    public void ChooseMegaForm(MegaFormInfo form)
    {
        IsMegaChoiceOpen = false;
        if (CanMegaEvolveNow && IsPlayerTurn) _ = MegaEvolveAsync(form);
    }

    public void CancelMegaChoice() => IsMegaChoiceOpen = false;

    private async Task MegaEvolveAsync(MegaFormInfo form)
    {
        _megaUsedThisBattle = true;
        IsAttackMenuOpen = false;
        IsPlayerTurn = false;
        IsMegaEvolving = true;

        BattleMessage = string.IsNullOrEmpty(form.StoneId) ? L.F("mega.battle.reactNoStone", Name) : L.F("mega.battle.react", Name, form.StoneName);
        await Task.Delay(MEGA_GLOW_MS);

        ActiveMegaForm = form;
        BattleMessage = form.IsPrimal ? L.F("mega.battle.primal", Name) : L.F("mega.battle.done", Name, Megas.DisplayName(form));
        await Task.Delay(MEGA_MESSAGE_MS);

        IsMegaEvolving = false;
        if (!IsBattleOpen) return;
        BattleMessage = L.T("battle.choose");
        IsPlayerTurn = true;
    }

    public async void StartGymBattle(int gymIndex)
    {
        if (!CanStartActivity) return;

        var leader = GymManager.GetLeader(gymIndex);
        if (leader is null || gymIndex > GymBadges) return;

        _selectedGymIndex = gymIndex;
        _isGymRematch = gymIndex < GymBadges;
        BeginBattleSession();
        PrepareBattleScreen();
        IsGymBattle = true;

        EnemySpeciesId = leader.PokemonSpeciesId;
        _enemyForm = "";
        IsEnemyShiny = false;
        EnemyLevel = leader.Level;
        PlayerMaxHp = CombatMaxHp;
        PlayerHp = PlayerMaxHp;

        EnemyMaxHp = (int)(BattleCalculator.MaxHp(Dex.Get(EnemySpeciesId)?.BaseHp ?? 50, BattleCalculator.GYM_GENE, EnemyLevel) * BattleCalculator.GYM_BONUS);
        EnemyHp = EnemyMaxHp;

        GenerateEnemySkills();
        _enemyTempActionTimer = 0;
        UpdateEnemyAnimation(PokemonAnim.IDLE);

        IsBattleOpen = true;
        IsPlayerTurn = false;
        BattleMessage = L.F("battle.gym.intro", leader.LeaderName);

        await Task.Delay(2000);
        BattleMessage = L.T("battle.choose");
        IsPlayerTurn = true;
    }

    private async Task AnnounceGymWinAsync()
    {
        var leader = GymManager.GetLeader(_selectedGymIndex);
        if (leader is not null && !_isGymRematch)
        {
            GymBadges++;
            NeedsSave = true;
            BattleMessage = L.F("battle.gym.won", leader.LeaderName, leader.BadgeName);
            await Task.Delay(3000);

            if (TryGrantMegaRing())
            {
                BattleMessage = L.T("mega.ring.got");
                await Task.Delay(3000);
            }
        }
        else if (leader is not null)
        {
            BattleMessage = L.F("battle.gym.rematchWon", leader.LeaderName);
            await Task.Delay(3000);
        }
    }

    private async Task ProcessGymBattleRewardsAsync()
    {
        BattleMessage = L.T("battle.gym.prize");
        await Task.Delay(2000);

        AddItem(ItemDex.HyperPotion, _isGymRematch ? GYM_REMATCH_PRIZE_POTIONS : GYM_PRIZE_POTIONS);
        AddItem(ItemDex.MonsterBall, _isGymRematch ? GYM_REMATCH_PRIZE_BALLS : GYM_PRIZE_BALLS);

        BattleMessage = L.F("battle.gym.prizeItems", L.T($"item.{ItemDex.HyperPotion}.name"), L.T($"item.{ItemDex.MonsterBall}.name"));
        await Task.Delay(2500);

        string? shardMessage = GiveMegaShards(_isGymRematch ? GYM_REMATCH_SHARDS : GYM_SHARDS);
        if (shardMessage is not null)
        {
            BattleMessage = shardMessage;
            await Task.Delay(2500);
        }

        Bond = Math.Min(100, Bond + 10);
        BattleMessage = L.F("battle.bondUp", Name);
        await Task.Delay(2500);

        CloseBattle();
        IsPlayerTurn = true;
    }
}
