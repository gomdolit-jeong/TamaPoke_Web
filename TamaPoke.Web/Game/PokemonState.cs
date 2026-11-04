using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;

namespace TamaPoke.Web.Game;

// 돌보기와 시간 흐름. 수치와 규칙은 PC판 PokemonState.Care.cs / LifeCycle.cs와 같습니다.
// PC판처럼 페이지가 열려 있는 동안만 시간이 흐릅니다. (닫혀 있던 시간을 따라잡지 않음)
public sealed partial class PokemonState
{
    private const int MAX_POOPS = 3;
    private const int POOP_CHANCE = 5;
    private const int MINUTES_PER_LEVEL = 60;
    private const int RUNAWAY_TICKS = 60;
    private const int CARE_MISTAKE_THRESHOLD = 10;
    private const int CARE_MISTAKE_COOLDOWN_MINUTES = 60;
    private const int WEIGHT_BURN_EVERY_MINUTES = 3;
    private const int EGG_HATCH_SECONDS = 180;
    private const int EGG_HATCH_TAPS = 3;
    private const int FAREWELL_AGE_DAYS = 3; // 설정을 옮기면 PC판처럼 바꿀 수 있게 합니다.
    private const int EVOLVE_MIN_STAT = 40;
    private const int FEMALE_ONLY_EVOLUTION_SPECIES = 415; // 세꿀버리는 암컷만 진화합니다.

    private const int CEREMONY_FAREWELL = 1;
    private const int CEREMONY_RUNAWAY = 2;
    private const int CEREMONY_RELEASE = 3;
    private const int CEREMONY_EVOLVE = 4;
    private const double CEREMONY_MS = 10_000;

    // 진화 연출: 오잉!? → 빛나는 동안 진화 → 축하 메시지
    private const double EVOLVE_FLASH_START_MS = 1500;
    private const double EVOLVE_FLASH_END_MS = 4500;
    private const double EVOLVE_END_MS = 8000;
    private const double EVOLVE_FLOAT_PIXELS = 20;

    // PC판 메인 루프 한 칸. 모션 길이는 PC판의 칸 수를 그대로 곱해서 정합니다.
    private const double LOOP_TICK_MS = 33;
    private const double MAX_MOTION_STEP_MS = 100;
    private const double EAT_MS = 65 * LOOP_TICK_MS;
    private const double FOOD_BITE_MS = 700;
    private const double FOOD_END_MS = 2100;
    private const double FOOD_FADE_MS = 300;

    private const double WALK_LIMIT = 90;
    private const double WALK_PIXELS_PER_SECOND = 45; // PC판과 같은 속도

    private static readonly HashSet<int> SideViewActions =
    [
        PokemonAnim.WALK, PokemonAnim.SLEEP, PokemonAnim.EVENTSLEEP, PokemonAnim.HOP,
        PokemonAnim.HURT, PokemonAnim.PAIN, PokemonAnim.CRINGE,
    ];

    // 메달 (비트 값은 PC판과 같음)
    public static readonly (int Bit, string Key)[] MedalList =
    [
        (1 << 0, "medal.lv10"), (1 << 1, "medal.lv25"), (1 << 2, "medal.lv50"), (1 << 3, "medal.berry"),
        (1 << 4, "medal.streak7"), (1 << 5, "medal.bond"), (1 << 6, "medal.final"), (1 << 7, "medal.fit"),
    ];

    // 저장되는 값
    public int SaveVersion { get; set; } = 1;
    public int SpeciesId { get; set; } = -1;
    public int Fullness { get; set; } = 80;
    public int Joy { get; set; } = 80;
    public int Energy { get; set; } = 80;
    public int Hygiene { get; set; } = 100;
    public int Weight { get; set; }
    public int Bond { get; set; }
    public int Poops { get; set; }
    public int CareMistakes { get; set; }
    public int MistakeCooldown { get; set; }
    public int NeglectTicks { get; set; }
    public int AgeSeconds { get; set; }
    public int AgeMinutes { get; set; }
    public int EggTaps { get; set; }
    public bool IsSleeping { get; set; }
    public bool BerryKnown { get; set; }
    public bool IsFemale { get; set; }
    public bool IsGenderless { get; set; }
    public bool IsEvolutionPostponed { get; set; }
    public bool IsFarewellPostponed { get; set; }
    public int LastEnd { get; set; } = CEREMONY_FAREWELL; // 이로치 확률에 씁니다. (1 작별, 2 가출, 3 놔주기)
    public int EvolvedFromSpeciesId { get; set; }
    public bool IsMuted { get; set; }
    public List<int> UnlockedPokemon { get; set; } = [];
    public int Medals { get; set; }
    public int Streak { get; set; }
    public DateTime LastPlayedDate { get; set; } = DateTime.Now.Date;
    public int Ceremony { get; set; }
    public double CeremonyElapsedMs { get; set; }

    // 화면 전용 값
    private int _tempAction = PokemonAnim.IDLE;
    private double _tempActionMs;
    private bool _isAutoAction;
    private int _nextAction = -1;
    private double _nextActionMs;
    private double _secondElapsedMs;
    private double _pettingMs;

    [JsonIgnore] public double PosX { get; private set; }
    [JsonIgnore] public int FlipX { get; private set; } = 1;
    [JsonIgnore] public bool IsBathing { get; private set; }
    [JsonIgnore] public int? EatingFood { get; private set; }
    [JsonIgnore] public bool IsFeedMenuOpen { get; set; }
    [JsonIgnore] public bool NeedsSave { get; set; }

    [JsonIgnore] public PokemonDex Dex { get; set; } = null!;
    [JsonIgnore] public Action<int[][]>? PlaySound { get; set; }
    [JsonIgnore] public bool IsProfileOpen { get; set; }

    [JsonIgnore] public bool IsEgg => SpeciesId < 0;
    [JsonIgnore] public bool IsCeremony => Ceremony != 0;

    // 막 시작한 기기의 세이브인지 (로그인할 때 물어보지 않고 서버 세이브를 받습니다)
    [JsonIgnore] public bool HasNoProgress => IsEgg && AgeSeconds < 60 && UnlockedPokemon.Count == 0 && Party.Count == 0;
    [JsonIgnore] public bool IsPetting => _pettingMs > 0;
    [JsonIgnore] public int Level => 1 + AgeMinutes / MINUTES_PER_LEVEL;
    [JsonIgnore] public int LowestStat => Math.Min(Math.Min(Fullness, Joy), Math.Min(Energy, Hygiene));
    [JsonIgnore] public int FavoriteBerry => Math.Abs(SpeciesId) % 3;
    [JsonIgnore] public bool CanRunawayNow => !IsEgg && !IsSleeping && !IsCeremony && NeglectTicks >= RUNAWAY_TICKS;
    [JsonIgnore] public bool CanCare => !IsEgg && !IsCeremony;
    [JsonIgnore] public int LevelProgressMinutes => AgeMinutes % MINUTES_PER_LEVEL;
    [JsonIgnore] public int MedalCount => MedalList.Count(m => (Medals & m.Bit) != 0);
    [JsonIgnore] public bool IsFinalEvolution => !IsEgg && Dex.Get(SpeciesId)?.EvolveTo == 0;
    [JsonIgnore] public bool CanFarewellNow => !IsEgg && !IsSleeping && !IsCeremony && AgeMinutes >= FAREWELL_AGE_DAYS * 24 * 60;
    [JsonIgnore] public bool CanShowEvolvePrompt => CanEvolveNow && !IsEvolutionPostponed;
    [JsonIgnore] public bool CanShowFarewellPrompt => CanFarewellNow && !IsFarewellPostponed;
    [JsonIgnore] public bool IsEvolving => Ceremony == CEREMONY_EVOLVE;
    [JsonIgnore] public bool IsEvolvingFlash => IsEvolving && CeremonyElapsedMs is >= EVOLVE_FLASH_START_MS and < EVOLVE_FLASH_END_MS;
    [JsonIgnore] public bool HasEvolved => IsEvolving && CeremonyElapsedMs >= EVOLVE_FLASH_END_MS;
    [JsonIgnore] public double PosY => IsEvolvingFlash ? -EVOLVE_FLOAT_PIXELS : 0;
    [JsonIgnore] public double GamePosY => _gamePosY;
    // 열매를 세 번에 나눠 베어 먹고, 마지막에 흐려집니다. (PC판과 같은 0.7초 간격)
    private double EatElapsedMs => EAT_MS - _tempActionMs;
    [JsonIgnore] public int FoodBites => EatingFood is null ? 0 : Math.Clamp((int)(EatElapsedMs / FOOD_BITE_MS), 0, 3);
    [JsonIgnore] public double FoodOpacity => EatingFood is null ? 0 : Math.Clamp((FOOD_END_MS - EatElapsedMs) / FOOD_FADE_MS, 0, 1);
    [JsonIgnore] public int PetSerial { get; private set; }

    [JsonIgnore]
    public bool CanEvolveNow
    {
        get
        {
            if (IsEgg || IsSleeping || IsCeremony || IsFinalEvolution) return false;
            if (SpeciesId == FEMALE_ONLY_EVOLUTION_SPECIES && !IsFemale) return false;
            if (Dex.GetEvolution(SpeciesId) is not { } evolution || evolution.NextSpeciesIds.Length == 0) return false;
            return Level >= evolution.EvolveLevel + CareMistakes && LowestStat >= EVOLVE_MIN_STAT;
        }
    }
    [JsonIgnore] public string SpritePath => IsEgg ? "0000/0004" : $"{SpeciesId:D4}";
    [JsonIgnore] public bool IsNight => IsSleeping || DateTime.Now.Hour is < 6 or >= 20;

    [JsonIgnore]
    public int CurrentAction
    {
        get
        {
            if (Ceremony == CEREMONY_FAREWELL) return PokemonAnim.POSE;
            if (Ceremony == CEREMONY_RUNAWAY) return PokemonAnim.WALK;
            if (Ceremony == CEREMONY_RELEASE) return PokemonAnim.HOP;
            if (Ceremony == CEREMONY_EVOLVE) return PokemonAnim.FLOAT;
            if (IsAnyMiniGameOpen) return IsCleanHit ? PokemonAnim.HURT : PokemonAnim.IDLE;
            if (IsBattleOpen) return _tempActionMs > 0 ? _tempAction : PokemonAnim.IDLE;
            if (IsEgg) return PokemonAnim.IDLE;
            if (IsSleeping) return PokemonAnim.SLEEP;

            // 유저가 직접 한 행동은 상태 모션보다 먼저 보여줍니다.
            if (_tempActionMs > 0 && !_isAutoAction) return _tempAction;

            if (Poops > 0) return PokemonAnim.PAIN;
            if (Hygiene < 30) return PokemonAnim.HURT;

            if (_tempActionMs > 0) return _tempAction;

            if (Fullness < 30) return PokemonAnim.CRINGE;
            if (Joy < 30) return PokemonAnim.SINK;
            if (Energy < 30) return PokemonAnim.EVENTSLEEP;
            return PokemonAnim.IDLE;
        }
    }

    [JsonIgnore] public int CurrentRow => IsBattleOpen ? BATTLE_PLAYER_ROW : SideViewActions.Contains(CurrentAction) ? PokemonAnim.ROW_LEFT : PokemonAnim.ROW_FRONT;

    [JsonIgnore]
    public string MoodKey
    {
        get
        {
            if (Ceremony == CEREMONY_FAREWELL) return "mood.farewell";
            if (Ceremony == CEREMONY_RUNAWAY) return "mood.runaway";
            if (Ceremony == CEREMONY_RELEASE) return "mood.release";
            if (IsBattleOpen) return "mood.battle";
            if (ActiveGame == MiniGame.Ball) return "mood.game.ball";
            if (ActiveGame == MiniGame.Catch) return "mood.game.catch";
            if (ActiveGame == MiniGame.Memo) return "mood.game.memo";
            if (ActiveGame == MiniGame.Clean) return "mood.game.clean";
            if (IsEgg) return "mood.egg";
            if (IsSleeping) return "mood.sleeping";
            if (CanRunawayNow) return "mood.lonely";
            if (CanFarewellNow) return "mood.wantsToTalk";
            if (Poops > 0) return "mood.poop";
            if (Hygiene < 30) return "mood.dirty";
            if (LowestStat < 30) return "mood.veryBad";
            if (LowestStat < 50) return "mood.tired";
            if (Fullness > 80 && Joy > 80) return "mood.veryHappy";
            return "mood.good";
        }
    }

    // 실제 경과 시간을 받아 움직임과 1초 단위 시간을 진행합니다.
    public void Update(double elapsedMs)
    {
        UpdateMotion(Math.Min(elapsedMs, MAX_MOTION_STEP_MS));

        // 연출, 미니게임, 배틀 중에는 나이를 먹지 않습니다.
        if (IsBattleOpen)
        {
            UpdateEnemyMotion(Math.Min(elapsedMs, MAX_MOTION_STEP_MS));
            return;
        }
        if (IsCeremony)
        {
            UpdateCeremony(elapsedMs);
            return;
        }
        if (IsAnyMiniGameOpen)
        {
            UpdateMiniGame(Math.Min(elapsedMs, MAX_MOTION_STEP_MS));
            return;
        }
        UpdateGameResult(elapsedMs);

        // 탭이 뒤로 가면 타이머가 느려지므로, 지난 시간만큼 틱을 채웁니다.
        _secondElapsedMs += elapsedMs;
        while (_secondElapsedMs >= 1000)
        {
            _secondElapsedMs -= 1000;
            Tick();
        }
    }

    private void UpdateCeremony(double elapsedMs)
    {
        double before = CeremonyElapsedMs;
        CeremonyElapsedMs += elapsedMs;

        if (!IsEvolving)
        {
            if (CeremonyElapsedMs >= CEREMONY_MS) PrepareNewEgg();
            return;
        }

        if (before < EVOLVE_FLASH_END_MS && CeremonyElapsedMs >= EVOLVE_FLASH_END_MS) ExecuteActualEvolve();
        if (CeremonyElapsedMs >= EVOLVE_END_MS)
        {
            Ceremony = 0;
            CeremonyElapsedMs = 0;
            NeedsSave = true;
        }
    }

    private void UpdateMotion(double ms)
    {
        if (_pettingMs > 0) _pettingMs -= ms;

        if (CurrentAction == PokemonAnim.WALK && (_tempActionMs > 0 || IsCeremony))
        {
            PosX -= FlipX * WALK_PIXELS_PER_SECOND * ms / 1000;
            if (PosX >= WALK_LIMIT) { PosX = WALK_LIMIT; FlipX = 1; }
            else if (PosX <= -WALK_LIMIT) { PosX = -WALK_LIMIT; FlipX = -1; }
        }

        if (_tempActionMs > 0)
        {
            _tempActionMs -= ms;
            if (_tempActionMs <= 0) FinishTempAction();
        }
    }

    private void FinishTempAction()
    {
        IsBathing = false;
        EatingFood = null;
        if (_nextAction < 0) return;

        PlayTempAction(_nextAction, _nextActionMs);
        _nextAction = -1;
    }

    private void PlayTempAction(int action, double ms)
    {
        _tempAction = action;
        _tempActionMs = ms;
        _isAutoAction = false;
    }

    private void PlayAutoAction(int action, int loopTicks)
    {
        PlayTempAction(action, loopTicks * LOOP_TICK_MS);
        _isAutoAction = true;
    }

    private void ResetPosition()
    {
        PosX = 0;
        FlipX = 1;
        _tempActionMs = 0;
        _nextAction = -1;
        IsBathing = false;
        EatingFood = null;
    }

    private void Tick()
    {
        // 프로필을 보는 동안에는 시간이 멈춥니다.
        if (IsProfileOpen) return;

        AgeSeconds++;
        if (DateTime.Now.Date > LastPlayedDate.Date) CheckDailyStreak();
        if (AgeSeconds % 10 == 0) NeedsSave = true;

        if (IsEgg)
        {
            if (AgeSeconds >= EGG_HATCH_SECONDS) Hatch();
            return;
        }

        if (AgeSeconds % 60 == 0) UpdateVitalsAndNeeds();

        // 응가가 생기거나 더러워지면 혼자 놀던 행동을 멈춥니다.
        if ((Poops > 0 || Hygiene < 30) && _tempActionMs > 0 && _isAutoAction) _tempActionMs = 0;

        ProcessIdleBehaviors();
        CheckMedals();
    }

    private void CheckDailyStreak()
    {
        int daysPassed = (DateTime.Now.Date - LastPlayedDate.Date).Days;
        if (daysPassed == 1) Streak++;
        else if (daysPassed > 1) Streak = 0;
        LastPlayedDate = DateTime.Now.Date;
        NeedsSave = true;
    }

    private void CheckMedals()
    {
        int medals = Medals;
        if (Level >= 10) medals |= MedalList[0].Bit;
        if (Level >= 25) medals |= MedalList[1].Bit;
        if (Level >= 50) medals |= MedalList[2].Bit;
        if (BerryKnown) medals |= MedalList[3].Bit;
        if (Streak >= 7) medals |= MedalList[4].Bit;
        if (Bond >= 100) medals |= MedalList[5].Bit;
        if (IsFinalEvolution) medals |= MedalList[6].Bit;
        if (Weight == 0 && Level >= 5 && CareMistakes == 0) medals |= MedalList[7].Bit;
        if (medals == Medals) return;

        Medals = medals;
        NeedsSave = true;
    }

    // 이전 세이브의 포켓몬도 도감에 올라가도록 불러온 뒤에 한 번 부릅니다.
    public void RegisterCurrentInDex()
    {
        if (IsEgg || UnlockedPokemon.Contains(SpeciesId)) return;
        UnlockedPokemon.Add(SpeciesId);
        UnlockedPokemon.Sort();
        NeedsSave = true;
    }

    private void UpdateVitalsAndNeeds()
    {
        if (AgeMinutes % MINUTES_PER_LEVEL == 0)
        {
            IsEvolutionPostponed = false;
            IsFarewellPostponed = false;
        }

        AgeMinutes++;
        if (AgeMinutes % MINUTES_PER_LEVEL == 0) IsEvolutionPostponed = false;
        if (LowestStat >= EVOLVE_MIN_STAT)
        {
            _goodTicks++;
            if (_goodTicks >= 720)
            {
                _goodTicks = 0;
                if (TrDef < 100) TrDef++;
            }
        }
        else
        {
            _goodTicks = 0;
        }
        NeedsSave = true;

        // 몸무게는 자는 중에도 3분마다 1씩 빠집니다.
        if (Weight > 0 && AgeMinutes % WEIGHT_BURN_EVERY_MINUTES == 0) Weight--;

        // 자는 동안에는 케어 실수가 생기지 않습니다.
        if (IsSleeping)
        {
            Energy = Clamp100(Energy + 6);
            if (AgeMinutes % 30 == 0) Hygiene = Clamp100(Hygiene - 1);
            if (Energy >= 100) WakeUp();
            return;
        }

        if (AgeMinutes % 20 == 0)
        {
            Fullness = Clamp100(Fullness - 2);
            Energy = Clamp100(Energy - 1);
        }

        if (Fullness > 40 && Poops < MAX_POOPS && Random.Shared.Next(100) < POOP_CHANCE)
        {
            Poops++;
            Hygiene = Clamp100(Hygiene - 10 * Poops);
        }

        if (AgeMinutes % 10 == 0)
        {
            int joyChange = 0;
            if (Fullness < 30) joyChange -= 2;
            if (Hygiene < 30) joyChange -= 3;
            Joy = Clamp100(Joy + joyChange);
        }

        CheckCareMistake();

        if (Fullness == 0 && Joy == 0 && Energy == 0 && Hygiene == 0)
        {
            if (NeglectTicks < RUNAWAY_TICKS) NeglectTicks++;
            if (CanRunawayNow) StartRunaway();
        }
        else
        {
            NeglectTicks = 0;
        }
    }

    // 스탯 하나라도 10 이하로 방치되면 1회. 같은 방치를 매분 세지 않도록 60분 쿨다운을 둡니다.
    private void CheckCareMistake()
    {
        if (MistakeCooldown > 0) MistakeCooldown--;
        if (LowestStat > CARE_MISTAKE_THRESHOLD || MistakeCooldown > 0) return;

        CareMistakes++;
        MistakeCooldown = CARE_MISTAKE_COOLDOWN_MINUTES;
        if (Bond > 1) Bond--;
    }

    private void ProcessIdleBehaviors()
    {
        if (IsSleeping || _tempActionMs > 0 || CurrentAction != PokemonAnim.IDLE) return;

        int roll = Random.Shared.Next(100);
        if (roll < 35)
        {
            if (Random.Shared.Next(10000) < WILD_ENCOUNTER_PER_10000)
            {
                StartWildBattle(autoEncounter: true);
                return;
            }
            FlipX = Random.Shared.Next(2) == 0 ? -1 : 1;
            PlayAutoAction(PokemonAnim.WALK, Random.Shared.Next(120, 240));
        }
        else if (roll < 60)
        {
            int[] flair = [PokemonAnim.POSE, PokemonAnim.NOD, PokemonAnim.DEEPBREATH];
            PlayAutoAction(flair[Random.Shared.Next(flair.Length)], 90);
        }
        else
        {
            PlayAutoAction(PokemonAnim.IDLE, Random.Shared.Next(60, 150));
        }
    }

    // foodType: 0 빨강, 1 파랑, 2 초록, 3 사탕
    public void Feed(int foodType)
    {
        IsFeedMenuOpen = false;
        if (!CanCare || IsSleeping) return;

        PlaySound?.Invoke(SoundEffects.Eat);
        if (foodType == 3)
        {
            Fullness = Clamp100(Fullness + 10);
            Joy = Clamp100(Joy + 12);
            Weight = Clamp100(Weight + 12);
        }
        else if (foodType == FavoriteBerry)
        {
            Fullness = Clamp100(Fullness + 35);
            Joy = Clamp100(Joy + 10);
            Bond = Clamp100(Bond + 2);
            BerryKnown = true;
        }
        else
        {
            Fullness = Clamp100(Fullness + 25);
        }

        NeglectTicks = 0;
        ResetPosition();
        EatingFood = foodType;
        PlayTempAction(PokemonAnim.EAT, EAT_MS);
        _nextAction = PokemonAnim.HOP;
        _nextActionMs = 30 * LOOP_TICK_MS;
        NeedsSave = true;
    }

    public void Clean()
    {
        if (!CanCare || IsSleeping) return;

        PlaySound?.Invoke(SoundEffects.Medal);
        Poops = 0;
        Hygiene = 100;
        Bond = Clamp100(Bond + 1);
        NeglectTicks = 0;

        ResetPosition();
        IsBathing = true;
        PlayTempAction(PokemonAnim.POSE, 60 * LOOP_TICK_MS);
        _nextAction = PokemonAnim.SHAKE;
        _nextActionMs = 30 * LOOP_TICK_MS;
        NeedsSave = true;
    }

    public void ToggleSleep()
    {
        if (!CanCare) return;

        PlaySound?.Invoke(SoundEffects.Tap);
        if (IsSleeping)
        {
            WakeUp();
        }
        else
        {
            IsSleeping = true;
            ResetPosition();
        }
        NeedsSave = true;
    }

    private void WakeUp()
    {
        IsSleeping = false;
        PlayTempAction(PokemonAnim.WAKE, 60 * LOOP_TICK_MS);
        NeedsSave = true;
    }

    // 포켓몬을 누르면: 알은 톡톡 두드리고, 포켓몬은 쓰다듬습니다.
    public void Tap()
    {
        if (IsCeremony) return;

        if (IsEgg)
        {
            PlaySound?.Invoke(SoundEffects.Tap);
            EggTaps++;
            NeedsSave = true;
            if (EggTaps >= EGG_HATCH_TAPS) Hatch();
            return;
        }

        if (IsSleeping) return;

        PlaySound?.Invoke(SoundEffects.Heart);
        IsFeedMenuOpen = false;
        IsPlayMenuOpen = false;
        Joy = Clamp100(Joy + 5);
        Bond = Clamp100(Bond + 1);
        _pettingMs = 1000;
        PetSerial++;
        ResetPosition();
        PlayTempAction(PokemonAnim.HOP, 30 * LOOP_TICK_MS);
        NeedsSave = true;
    }

    private void Hatch()
    {
        SpeciesId = Dex.PickHatchSpecies();
        (IsGenderless, IsFemale) = Dex.RollGender(SpeciesId);
        Genes = new PokemonGene
        {
            HpGene = Random.Shared.Next(0, 32), AtkGene = Random.Shared.Next(0, 32),
            DefGene = Random.Shared.Next(0, 32), SpeGene = Random.Shared.Next(0, 32),
        };
        RegisterCurrentInDex();
        BerryKnown = false;
        NeglectTicks = 0;
        AgeSeconds = 0;
        AgeMinutes = 0;
        RelearnFromLevel();
        IsEvolutionPostponed = false;
        IsFarewellPostponed = false;
        ResetPosition();
        Fullness = 80;
        Joy = 80;
        Energy = 100;
        Hygiene = 100;
        NeedsSave = true;
    }

    public void StartEvolution()
    {
        if (!CanEvolveNow) return;

        PlaySound?.Invoke(SoundEffects.Medal);
        IsFeedMenuOpen = false;
        IsEvolutionPostponed = false;
        ResetPosition();
        EvolvedFromSpeciesId = SpeciesId;
        Ceremony = CEREMONY_EVOLVE;
        CeremonyElapsedMs = 0;
        NeedsSave = true;
    }

    // 진화 갈래가 여러 개면 그중 하나로 무작위 진화합니다.
    private void ExecuteActualEvolve()
    {
        if (Dex.GetEvolution(SpeciesId) is not { } evolution || evolution.NextSpeciesIds.Length == 0) return;

        SpeciesId = evolution.NextSpeciesIds[Random.Shared.Next(evolution.NextSpeciesIds.Length)];
        RegisterCurrentInDex();
        IsEvolutionPostponed = false;
        NeedsSave = true;
    }

    public void PostponeEvolution()
    {
        IsEvolutionPostponed = true;
        NeedsSave = true;
    }

    public void PostponeFarewell()
    {
        IsFarewellPostponed = true;
        NeedsSave = true;
    }

    public void StartFarewell()
    {
        if (!CanFarewellNow) return;
        PlaySound?.Invoke(SoundEffects.Medal);
        StartEnding(CEREMONY_FAREWELL);
    }

    public void StartRelease()
    {
        if (IsEgg || IsCeremony) return;
        PlaySound?.Invoke(SoundEffects.Deny);
        StartEnding(CEREMONY_RELEASE);
    }

    private void StartRunaway()
    {
        PlaySound?.Invoke(SoundEffects.Deny);
        StartEnding(CEREMONY_RUNAWAY);
    }

    private void StartEnding(int ceremony)
    {
        IsFeedMenuOpen = false;
        IsSleeping = false;
        ResetPosition();
        LastEnd = ceremony;
        Ceremony = ceremony;
        CeremonyElapsedMs = 0;
        NeedsSave = true;
    }

    private void PrepareNewEgg()
    {
        SpeciesId = -1;
        EggTaps = 0;
        AgeSeconds = 0;
        AgeMinutes = 0;
        Fullness = 80;
        Joy = 80;
        Energy = 80;
        Hygiene = 100;
        Poops = 0;
        Weight = 0;
        CareMistakes = 0;
        MistakeCooldown = 0;
        Bond = 0;
        NeglectTicks = 0;
        IsSleeping = false;
        IsFemale = false;
        IsGenderless = false;
        IsEvolutionPostponed = false;
        IsFarewellPostponed = false;
        EvolvedFromSpeciesId = 0;
        Medals = 0;
        TrAtk = 0;
        TrDef = 0;
        TrSpeed = 0;
        Genes = new PokemonGene();
        Skills = new int[4];
        BerryKnown = false;
        Ceremony = 0;
        CeremonyElapsedMs = 0;
        IsFeedMenuOpen = false;
        ResetPosition();
        NeedsSave = true;
    }

    private static int Clamp100(int value) => Math.Clamp(value, 0, 100);
}
