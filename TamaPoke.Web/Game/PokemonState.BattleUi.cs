using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public sealed class PokemonGene
{
    public int HpGene { get; set; }
    public int AtkGene { get; set; }
    public int DefGene { get; set; }
    public int SpeGene { get; set; }
}

public sealed record ItemDefinition(string Id, bool IsBall, int HealPercent, string Icon);

public static class ItemDex
{
    public const string MonsterBall = "monsterball";
    public const string Potion = "potion";
    public const string SuperPotion = "super_potion";
    public const string HyperPotion = "hyper_potion";

    public static readonly ItemDefinition[] All =
    [
        new(MonsterBall, true, 0, "item_monsterball.png"),
        new(Potion, false, 15, "item_potion.png"),
        new(SuperPotion, false, 30, "item_super_potion.png"),
        new(HyperPotion, false, 50, "item_hyper_potion.png"),
    ];
}

// 지금 화면에 그릴 기술 이펙트 한 장면 (시트 안의 프레임 번호와 게임판 좌표)
public sealed record BattleFxFrame(string Image, int FrameCount, int Frame, double X, double Y, double Size, bool Flipped);

// 배틀 화면 상태와 시작, 포획, 보상. 턴 진행은 PokemonState.BattleTurns.cs입니다. (PC판에서 옮김)
public sealed partial class PokemonState
{
    private const int BATTLE_PLAYER_ROW = 3; // 내 포켓몬: 뒷모습으로 상대(오른쪽 위)를 바라봄
    private const int BATTLE_ENEMY_ROW = 7;  // 상대 포켓몬: 앞모습으로 나(왼쪽 아래)를 바라봄
    private const int AUTO_ENCOUNTER_TIMEOUT_SECONDS = 60; // 대기 중 저절로 시작된 배틀은 1분 동안 조작이 없으면 자동 도망
    private const int WILD_ENCOUNTER_PER_10000 = 3;
    private const int WILD_SHINY_CHANCE_PERCENT = 1;

    // 게임판 좌표 (PC판 BattleView와 같음)
    private const double ENEMY_CENTER_X = 214, ENEMY_CENTER_Y = 90, PLAYER_CENTER_X = 80, PLAYER_CENTER_Y = 139;
    private const double BATTLE_AREA_HEIGHT = 173;
    private const int FX_FRAME_MS = 80, SHOT_STEP_MS = 35, SHOT_STEPS = 9, SHOT_FRAME_EVERY = 2, CHARGE_FX_LOOPS = 4;
    private const double ENEMY_FX_SCALE = 2, PLAYER_FX_SCALE = 3, SHOT_SCALE = 2;
    private const int BURST_SIZE = 32, SHOT_SIZE = 16, BURST_FRAMES = 4, SHOT_FRAMES = 2;

    // 저장되는 값
    public int TrAtk { get; set; }
    public int TrDef { get; set; }
    public PokemonGene Genes { get; set; } = new();
    public int[] Skills { get; set; } = new int[4];
    public List<PartyMember> Party { get; set; } = [];
    public Dictionary<string, int> Items { get; set; } = new()
    {
        [ItemDex.MonsterBall] = 5, [ItemDex.Potion] = 1, [ItemDex.SuperPotion] = 1, [ItemDex.HyperPotion] = 1,
    };

    [JsonIgnore] public SkillDex SkillBook { get; set; } = null!;

    private int _goodTicks;
    private bool _isCounterReady;
    private int _playerAilmentTurns, _playerBadPoisonTurnCount, _enemyAilmentTurns, _enemyBadPoisonTurnCount;
    private int _battleSerial;
    private bool _battleAttended;
    private SkillInfo? _skillToLearn;
    private int _enemyTempActionId = PokemonAnim.IDLE;
    private int _enemyTempActionTimer;
    private double _enemyTickMs;

    [JsonIgnore] public bool IsBattleOpen { get; private set; }
    [JsonIgnore] public bool IsDefeatedFadeOut { get; private set; }
    [JsonIgnore] public bool IsPlayerTurn { get; private set; } = true;
    [JsonIgnore] public bool IsBattleResolved { get; private set; }
    [JsonIgnore] public bool IsCatchOffered { get; private set; }
    [JsonIgnore] public bool IsAttackMenuOpen { get; set; }
    [JsonIgnore] public bool IsBattleInventoryOpen { get; set; }
    [JsonIgnore] public bool IsEnemyVisible { get; private set; } = true;
    [JsonIgnore] public bool IsPlayerVisible { get; private set; } = true;
    [JsonIgnore] public int EnemySpeciesId { get; private set; } = 1;
    [JsonIgnore] public int EnemyLevel { get; private set; } = 1;
    [JsonIgnore] public int PlayerHp { get; private set; } = 100;
    [JsonIgnore] public int PlayerMaxHp { get; private set; } = 100;
    [JsonIgnore] public int EnemyHp { get; private set; } = 100;
    [JsonIgnore] public int EnemyMaxHp { get; private set; } = 100;
    [JsonIgnore] public bool IsPlayerTakingDamage { get; private set; }
    [JsonIgnore] public bool IsEnemyTakingDamage { get; private set; }
    [JsonIgnore] public string BattleMessage { get; set; } = "";
    [JsonIgnore] public int[] EnemySkills { get; } = new int[4];
    [JsonIgnore] public SkillAilment PlayerAilment { get; private set; }
    [JsonIgnore] public SkillAilment EnemyAilment { get; private set; }
    [JsonIgnore] public bool IsPlayerAilmentFlash { get; private set; }
    [JsonIgnore] public bool IsEnemyAilmentFlash { get; private set; }
    [JsonIgnore] public bool IsEnemyFemale { get; private set; }
    [JsonIgnore] public bool IsEnemyGenderless { get; private set; }
    [JsonIgnore] public bool IsSkillLearnMenuOpen { get; private set; }
    [JsonIgnore] public bool IsSkillReplaceMenuOpen { get; private set; }
    [JsonIgnore] public SkillInfo? RecommendedSkill1 { get; private set; }
    [JsonIgnore] public SkillInfo? RecommendedSkill2 { get; private set; }
    [JsonIgnore] public BattleFxFrame? BattleFx { get; private set; }
    [JsonIgnore] public bool IsBallVisible { get; private set; }
    [JsonIgnore] public bool IsBallWobbling { get; private set; }

    [JsonIgnore] public string Name => Dex.GetName(SpeciesId, L.Language);
    [JsonIgnore] public string EnemyName => Dex.GetName(EnemySpeciesId, L.Language);
    [JsonIgnore] public string EnemySpritePath => Dex.SpritePath(EnemySpeciesId, _enemyForm);
    [JsonIgnore] public string EnemyBasePath => $"{EnemySpeciesId:D4}";
    [JsonIgnore] public bool IsEnemyShiny { get; private set; }
    [JsonIgnore] private string EnemyBattleName => IsGymBattle ? EnemyName : L.F("battle.wildName", EnemyName);
    [JsonIgnore] public int EnemyCurrentAction => _enemyTempActionTimer > 0 ? _enemyTempActionId : PokemonAnim.IDLE;
    [JsonIgnore] public int EnemyRow => BATTLE_ENEMY_ROW;
    [JsonIgnore] public bool IsMainMenuVisible => !IsCatchOffered && !IsBattleResolved && !IsAttackMenuOpen && !IsBattleInventoryOpen && IsPlayerTurn;
    [JsonIgnore] public bool IsAttackMenuVisible => !IsCatchOffered && !IsBattleResolved && IsAttackMenuOpen && !IsBattleInventoryOpen && IsPlayerTurn;
    [JsonIgnore] public bool IsBagVisible => !IsCatchOffered && !IsBattleResolved && IsBattleInventoryOpen && IsPlayerTurn;
    [JsonIgnore] public IEnumerable<SkillInfo> CurrentSkills => Skills.Where(id => id != 0).Select(SkillBook.GetSkill).OfType<SkillInfo>();
    [JsonIgnore] public int SkillCount => Skills.Count(s => s != 0);

    public bool KnowsSkill(int skillId) => skillId != 0 && Array.Exists(Skills, s => s == skillId);

    private string _enemyForm = "";
    private PokemonInfo? MyBattleInfo => WithoutTypes(ActiveMegaForm?.ToPokemonInfo() ?? Dex.Get(SpeciesId), _playerLostTypes);
    private int WildStat(int? baseStat) => BattleCalculator.Stat(baseStat ?? 50, BattleCalculator.WILD_GENE, EnemyLevel);

    private int CombatMaxHp => BattleCalculator.MaxHp(MyBattleInfo?.BaseHp ?? 50, Genes.HpGene + TrDef / 4, Level);
    private int CombatAtk => BattleCalculator.Stat(MyBattleInfo?.BaseAtk ?? 50, Genes.AtkGene + TrAtk / 4, Level);
    private int CombatDef => BattleCalculator.Stat(MyBattleInfo?.BaseDef ?? 50, Genes.DefGene + TrDef / 4, Level);
    private int CombatSpeed => BattleCalculator.Stat(MyBattleInfo?.BaseSpeed ?? 50, Genes.SpeGene + TrSpeed / 4, Level);
    private int CombatSpAtk => BattleCalculator.Stat(MyBattleInfo?.BaseSpA ?? 50, Genes.AtkGene - 80, Level) + TrAtk;
    private int CombatSpDef => BattleCalculator.Stat(MyBattleInfo?.BaseSpD ?? 50, Genes.DefGene - 80, Level) + TrDef / 2;

    private void PlayBattleMotion(int action, int loopTicks) => PlayTempAction(action, loopTicks * LOOP_TICK_MS);

    private void UpdateEnemyAnimation(int action) => _enemyTempActionId = action;

    private void UpdateEnemyMotion(double ms)
    {
        _enemyTickMs += ms;
        while (_enemyTickMs >= LOOP_TICK_MS)
        {
            _enemyTickMs -= LOOP_TICK_MS;
            if (_enemyTempActionTimer > 0) _enemyTempActionTimer--;
        }
    }

    private void BeginBattleSession()
    {
        _battleSerial++;
        _battleAttended = false;
        ResetStatStages();
    }

    private void MarkBattleAttended() => _battleAttended = true;

    private async void FleeIfUnattendedAsync(int serial)
    {
        await Task.Delay(AUTO_ENCOUNTER_TIMEOUT_SECONDS * 1000);
        if (serial != _battleSerial || !IsBattleOpen || _battleAttended || !IsPlayerTurn || IsBattleResolved) return;

        IsPlayerTurn = false;
        BattleMessage = L.T("battle.autoFled");
        await Task.Delay(2000);
        if (serial != _battleSerial) return;
        CloseBattle();
        IsPlayerTurn = true;
    }

    // autoEncounter: 대기 중 저절로 시작된 배틀 (1분 동안 조작이 없으면 자동 도망)
    public async void StartWildBattle(bool autoEncounter = false)
    {
        if (!CanStartActivity) return;
        BeginBattleSession();
        PrepareBattleScreen();
        if (autoEncounter) Notify(L.T("notify.wild.title"), "⚔️");

        EnemySpeciesId = Dex.PickWildSpecies(Settings.SelectedGenerations);
        EnemyLevel = Math.Max(1, Level + Random.Shared.Next(-2, 3));
        PlayerMaxHp = CombatMaxHp;
        PlayerHp = PlayerMaxHp;
        (IsEnemyGenderless, IsEnemyFemale) = Dex.RollGender(EnemySpeciesId);
        IsEnemyShiny = Random.Shared.Next(100) < WILD_SHINY_CHANCE_PERCENT;
        _enemyForm = Dex.ApplyVariants(EnemySpeciesId, Dex.PickBaseForm(EnemySpeciesId), IsEnemyShiny, IsEnemyFemale);

        EnemyMaxHp = BattleCalculator.MaxHp(Dex.Get(EnemySpeciesId)?.BaseHp ?? 50, BattleCalculator.WILD_GENE, EnemyLevel);
        EnemyHp = EnemyMaxHp;

        GenerateEnemySkills();
        _enemyTempActionTimer = 0;

        IsBattleOpen = true;
        IsPlayerTurn = false;
        BattleMessage = L.F(IsEnemyShiny ? "battle.appear.shiny" : "battle.appear", EnemyName);

        await Task.Delay(1500);
        BattleMessage = L.T("battle.choose");
        IsPlayerTurn = true;

        if (autoEncounter) FleeIfUnattendedAsync(_battleSerial);
    }

    private void PrepareBattleScreen()
    {
        IsFeedMenuOpen = false;
        IsPlayMenuOpen = false;
        IsProfileOpen = false;
        _isCounterReady = false;
        IsBattleResolved = false;
        IsCatchOffered = false;
        IsAttackMenuOpen = false;
        IsBattleInventoryOpen = false;
        IsEnemyVisible = true;
        IsSkillLearnMenuOpen = false;
        IsSkillReplaceMenuOpen = false;
        IsGymBattle = false;
        ResetMegaForBattle();
        ResetPosition();

        PlayerAilment = SkillAilment.None; _playerAilmentTurns = 0; _playerBadPoisonTurnCount = 0;
        EnemyAilment = SkillAilment.None; _enemyAilmentTurns = 0; _enemyBadPoisonTurnCount = 0;
    }

    public void CloseBattle()
    {
        ResetMegaForBattle();
        IsGymBattle = false;
        IsBattleOpen = false;
        BattleMessage = "";
        BattleFx = null;
        IsBallVisible = false;
        NeedsSave = true;
    }

    // 기술 이펙트: 특수 기술은 쓴 쪽에서 날아가서 터지고, 나머지는 맞는 쪽에서 바로 터집니다.
    private async Task PlaySkillFxAsync(SkillInfo skill, bool targetIsEnemy)
    {
        string type = skill.Type.ToString().ToLowerInvariant();
        if (skill.Category == SkillCategory.Special)
        {
            var (fromX, fromY) = FxCenter(!targetIsEnemy);
            var (toX, toY) = FxCenter(targetIsEnemy);
            for (int i = 0; i <= SHOT_STEPS; i++)
            {
                double t = (double)i / SHOT_STEPS;
                ShowFx($"fx_shot_{type}.png", SHOT_FRAMES, i / SHOT_FRAME_EVERY % SHOT_FRAMES,
                    fromX + (toX - fromX) * t, fromY + (toY - fromY) * t, SHOT_SIZE * SHOT_SCALE, targetIsEnemy);
                await Task.Delay(SHOT_STEP_MS);
            }
        }
        await PlayBurstAsync($"fx_skill_{type}.png", targetIsEnemy);
    }

    private Task PlayStatFxAsync(bool rose, bool targetIsEnemy) =>
        PlayBurstAsync(rose ? "fx_stat_up.png" : "fx_stat_down.png", targetIsEnemy);

    private async Task PlayChargeFxAsync(SkillInfo skill, bool userIsPlayer)
    {
        string image = $"fx_charge_{skill.Type.ToString().ToLowerInvariant()}.png";
        for (int i = 0; i < CHARGE_FX_LOOPS; i++) await PlayBurstAsync(image, targetIsEnemy: !userIsPlayer);
    }

    private async Task PlayBurstAsync(string image, bool targetIsEnemy)
    {
        var (x, y) = FxCenter(targetIsEnemy);
        double size = BURST_SIZE * (targetIsEnemy ? ENEMY_FX_SCALE : PLAYER_FX_SCALE);
        for (int frame = 0; frame < BURST_FRAMES; frame++)
        {
            ShowFx(image, BURST_FRAMES, frame, x, y, size, targetIsEnemy);
            await Task.Delay(FX_FRAME_MS);
        }
        BattleFx = null;
    }

    private static (double X, double Y) FxCenter(bool enemy) =>
        enemy ? (ENEMY_CENTER_X, ENEMY_CENTER_Y) : (PLAYER_CENTER_X, PLAYER_CENTER_Y);

    // 그림이 오른쪽을 향해 그려져 있으니 내 포켓몬 쪽으로 갈 때는 뒤집습니다.
    private void ShowFx(string image, int frames, int frame, double centerX, double centerY, double size, bool targetIsEnemy)
    {
        double top = Math.Min(centerY - size / 2, BATTLE_AREA_HEIGHT - size);
        BattleFx = new BattleFxFrame(image, frames, frame, centerX - size / 2, top, size, !targetIsEnemy);
    }

    public int ItemCount(string itemId) => Items.GetValueOrDefault(itemId);

    public void AddItem(string itemId, int amount)
    {
        Items[itemId] = ItemCount(itemId) + amount;
        NeedsSave = true;
    }

    private void UseUpItem(string itemId)
    {
        int left = ItemCount(itemId) - 1;
        if (left > 0) Items[itemId] = left;
        else Items.Remove(itemId);
        NeedsSave = true;
    }

    // 가방에서 아이템 쓰기. 체력이 가득 차 있으면 턴도 아이템도 쓰지 않습니다.
    public async Task UseBattleItemAsync(ItemDefinition item)
    {
        if (ItemCount(item.Id) <= 0)
        {
            BattleMessage = L.F("battle.notEnough", L.T($"item.{item.Id}.name"));
            return;
        }

        if (item.IsBall)
        {
            if (IsGymBattle)
            {
                BattleMessage = L.T("battle.noBallInGym");
                return;
            }
            IsBattleInventoryOpen = false;
            await ThrowBallAsync();
            return;
        }

        if (PlayerHp >= PlayerMaxHp)
        {
            BattleMessage = L.T("battle.hpFull");
            return;
        }

        IsBattleInventoryOpen = false;
        UseUpItem(item.Id);
        MarkBattleAttended();
        IsPlayerTurn = false;

        int heal = Math.Max(1, PlayerMaxHp * item.HealPercent / 100);
        PlayerHp = Math.Min(PlayerMaxHp, PlayerHp + heal);
        BattleMessage = L.F("battle.item.used", Name, L.T($"item.{item.Id}.name"), heal);
        PlayBattleMotion(PokemonAnim.HOP, 30);
        await Task.Delay(1500);
        await EnemyTurnAction(false);
    }

    // 몬스터볼 던지기 연출 뒤 포획 판정
    public async Task ThrowBallAsync()
    {
        if (ItemCount(ItemDex.MonsterBall) <= 0)
        {
            IsCatchOffered = false;
            BattleMessage = L.T("battle.noBalls");
            await Task.Delay(2000);
            BattleMessage = L.T("battle.askCatch");
            IsCatchOffered = EnemyHp <= 0;
            return;
        }

        UseUpItem(ItemDex.MonsterBall);
        IsPlayerTurn = false;
        IsCatchOffered = false;
        BattleMessage = L.T("battle.throw");
        IsBallVisible = true;
        IsBallWobbling = false;
        await Task.Delay(500);

        IsEnemyVisible = false;
        IsBallWobbling = true;
        int shakes = Random.Shared.Next(2, 4);
        for (int i = 1; i <= shakes; i++)
        {
            BattleMessage = L.F("battle.wobble", i);
            await Task.Delay(600);
        }
        IsBallVisible = false;
        IsBallWobbling = false;
        await ExecuteCatchResultAsync();
    }

    private async Task ExecuteCatchResultAsync()
    {
        MarkBattleAttended();
        double hpPercent = (double)EnemyHp / EnemyMaxHp;
        int catchRate = hpPercent <= 0.2 ? 70 : hpPercent <= 0.5 ? 35 : 10;

        if (Random.Shared.Next(100) < catchRate)
        {
            if (!UnlockedPokemon.Contains(EnemySpeciesId))
            {
                UnlockedPokemon.Add(EnemySpeciesId);
                UnlockedPokemon.Sort();
            }

            bool wasFull = Party.Count >= MAX_OTHER_PARTY_MEMBERS;
            AddCaughtMember(new PartyMember
            {
                SpeciesId = EnemySpeciesId,
                SpriteForm = _enemyForm,
                IsShiny = IsEnemyShiny,
                AgeMinutes = (EnemyLevel - 1) * MINUTES_PER_LEVEL,
                IsFemale = IsEnemyFemale,
                IsGenderless = IsEnemyGenderless,
                Weight = Random.Shared.Next(10, 31), // 야생에서 막 온 포켓몬: 바로 퍼펙트 바디를 받지 않도록
                Skills = (int[])EnemySkills.Clone(),
            });

            BattleMessage = L.F(wasFull ? "battle.caught.full" : "battle.caught", EnemyName);
            await Task.Delay(wasFull ? 2500 : 2000);
            await ProcessWildBattleRewardsAsync();
            return;
        }

        IsEnemyVisible = !_enemyLock.Hidden;
        BattleMessage = L.F("battle.brokeFree", EnemyName);
        await Task.Delay(2000);

        if (EnemyHp <= 0)
        {
            BattleMessage = L.T("battle.throwAgain");
            IsCatchOffered = true;
        }
        else
        {
            await EnemyTurnAction(false);
        }
    }

    private async Task CheckBattleEndAsync()
    {
        IsPlayerTurn = false;

        if (PlayerHp <= 0)
        {
            IsBattleResolved = true;
            BattleMessage = L.T(IsGymBattle ? "battle.lost.gym" : "battle.lost.wild");
            Joy = Math.Max(0, Joy - 10);
            Energy = Math.Max(0, Energy - 20);

            IsDefeatedFadeOut = true;
            await Task.Delay(3000);
            IsDefeatedFadeOut = false;
            CloseBattle();
            IsPlayerTurn = true;
        }
        else if (EnemyHp <= 0)
        {
            IsBattleResolved = true;
            TrAtk = Math.Min(100, TrAtk + 5);
            Bond = Math.Min(100, Bond + 5);
            NeedsSave = true;

            if (IsGymBattle)
            {
                await AnnounceGymWinAsync();
            }
            else
            {
                BattleMessage = L.T("battle.won");
                await Task.Delay(2000);
            }
            CheckAndLearnSkillAfterVictory();
        }
    }

    private void ProceedToCatchOrEnd()
    {
        if (IsGymBattle)
        {
            _ = ProcessGymBattleRewardsAsync();
            return;
        }

        BattleMessage = L.T("battle.askCatch");
        IsCatchOffered = true;
    }

    private async Task ProcessWildBattleRewardsAsync()
    {
        string dropMessage = "";
        if (Random.Shared.Next(100) < 50)
        {
            string item = ItemDex.MonsterBall;
            string key = "battle.drop";
            if (Random.Shared.Next(100) >= 50)
            {
                int potionRoll = Random.Shared.Next(100);
                item = potionRoll < 60 ? ItemDex.Potion : potionRoll < 90 ? ItemDex.SuperPotion : ItemDex.HyperPotion;
                if (item == ItemDex.HyperPotion) key = "battle.drop.rare";
            }
            AddItem(item, 1);
            dropMessage = L.F(key, L.T($"item.{item}.name"));
        }

        if (dropMessage.Length > 0)
        {
            BattleMessage = dropMessage;
            await Task.Delay(2000);
        }

        if (Random.Shared.Next(100) < WILD_SHARD_CHANCE && GiveMegaShards(1) is { } shardMessage)
        {
            BattleMessage = shardMessage;
            await Task.Delay(2000);
        }

        CloseBattle();
        IsPlayerTurn = true;
    }

    public void ChooseAttackMenu(bool open)
    {
        if (open) MarkBattleAttended();
        IsAttackMenuOpen = open;
    }

    public void ChooseBag(bool open)
    {
        if (open) MarkBattleAttended();
        IsBattleInventoryOpen = open;
    }

    // 설정 값을 PC판과 같은 범위로 맞춥니다.
    public void NormalizeSettings()
    {
        Settings.FarewellAgeDays = Math.Clamp(Settings.FarewellAgeDays, 1, 365);
        Settings.RoamingPokemonCount = Math.Clamp(Settings.RoamingPokemonCount, 1, 6);
        Settings.RoamingPetScale = Math.Clamp(Settings.RoamingPetScale, GameSettings.MinPetScale, GameSettings.MaxPetScale);
    }
}
