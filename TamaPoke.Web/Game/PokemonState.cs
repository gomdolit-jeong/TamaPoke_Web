using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;

namespace TamaPoke.Web.Game;

// 돌보기와 시간 흐름. 수치와 규칙은 PC판 PokemonState.Care.cs / LifeCycle.cs와 같습니다.
// PC판처럼 페이지가 열려 있는 동안만 시간이 흐릅니다. (닫혀 있던 시간을 따라잡지 않음)
public sealed class PokemonState
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
    private const int CEREMONY_RUNAWAY = 2;
    private const double CEREMONY_MS = 10_000;

    // PC판 메인 루프 한 칸. 모션 길이는 PC판의 칸 수를 그대로 곱해서 정합니다.
    private const double LOOP_TICK_MS = 33;
    private const double MAX_MOTION_STEP_MS = 100;

    private const double WALK_LIMIT = 100;
    private const double WALK_PIXELS_PER_SECOND = 68;

    // 아직 포켓몬 데이터를 옮기지 않아서, 부화하면 항상 파이리입니다.
    private const int HATCH_SPECIES_ID = 4;

    private static readonly HashSet<int> SideViewActions =
    [
        PokemonAnim.WALK, PokemonAnim.SLEEP, PokemonAnim.EVENTSLEEP, PokemonAnim.HOP,
        PokemonAnim.HURT, PokemonAnim.PAIN, PokemonAnim.CRINGE,
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

    [JsonIgnore] public bool IsEgg => SpeciesId < 0;
    [JsonIgnore] public bool IsCeremony => Ceremony != 0;
    [JsonIgnore] public bool IsPetting => _pettingMs > 0;
    [JsonIgnore] public int Level => 1 + AgeMinutes / MINUTES_PER_LEVEL;
    [JsonIgnore] public int LowestStat => Math.Min(Math.Min(Fullness, Joy), Math.Min(Energy, Hygiene));
    [JsonIgnore] public int FavoriteBerry => Math.Abs(SpeciesId) % 3;
    [JsonIgnore] public bool CanRunawayNow => !IsEgg && !IsSleeping && !IsCeremony && NeglectTicks >= RUNAWAY_TICKS;
    [JsonIgnore] public bool CanCare => !IsEgg && !IsCeremony;
    [JsonIgnore] public string SpritePath => IsEgg ? "0000/0004" : $"{SpeciesId:D4}";
    [JsonIgnore] public string BiomeName => IsEgg ? "TALL GRASS" : "MOUNTAIN"; // 타입 데이터를 옮기면 PC판처럼 타입별로 나눕니다.
    [JsonIgnore] public bool IsNight => IsSleeping || DateTime.Now.Hour is < 6 or >= 20;

    [JsonIgnore]
    public int CurrentAction
    {
        get
        {
            if (Ceremony == CEREMONY_RUNAWAY) return PokemonAnim.WALK;
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

    [JsonIgnore] public int CurrentRow => SideViewActions.Contains(CurrentAction) ? PokemonAnim.ROW_LEFT : PokemonAnim.ROW_FRONT;

    [JsonIgnore]
    public string MoodKey
    {
        get
        {
            if (Ceremony == CEREMONY_RUNAWAY) return "mood.runaway";
            if (IsEgg) return "mood.egg";
            if (IsSleeping) return "mood.sleeping";
            if (CanRunawayNow) return "mood.lonely";
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

        // 탭이 뒤로 가면 타이머가 느려지므로, 지난 시간만큼 틱을 채웁니다.
        _secondElapsedMs += elapsedMs;
        while (_secondElapsedMs >= 1000)
        {
            _secondElapsedMs -= 1000;
            Tick();
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
        if (IsCeremony)
        {
            CeremonyElapsedMs += 1000;
            NeedsSave = true;
            if (CeremonyElapsedMs >= CEREMONY_MS) PrepareNewEgg();
            return;
        }

        AgeSeconds++;
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
    }

    private void UpdateVitalsAndNeeds()
    {
        AgeMinutes++;
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
        PlayTempAction(PokemonAnim.EAT, 65 * LOOP_TICK_MS);
        _nextAction = PokemonAnim.HOP;
        _nextActionMs = 30 * LOOP_TICK_MS;
        NeedsSave = true;
    }

    public void Clean()
    {
        if (!CanCare || IsSleeping) return;

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
            EggTaps++;
            NeedsSave = true;
            if (EggTaps >= EGG_HATCH_TAPS) Hatch();
            return;
        }

        Joy = Clamp100(Joy + 5);
        Bond = Clamp100(Bond + 1);
        _pettingMs = 1000;

        if (!IsSleeping)
        {
            ResetPosition();
            PlayTempAction(PokemonAnim.HOP, 30 * LOOP_TICK_MS);
        }
        NeedsSave = true;
    }

    private void Hatch()
    {
        SpeciesId = HATCH_SPECIES_ID;
        BerryKnown = false;
        NeglectTicks = 0;
        AgeSeconds = 0;
        AgeMinutes = 0;
        ResetPosition();
        Fullness = 80;
        Joy = 80;
        Energy = 100;
        Hygiene = 100;
        NeedsSave = true;
    }

    private void StartRunaway()
    {
        ResetPosition();
        Ceremony = CEREMONY_RUNAWAY;
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
        BerryKnown = false;
        Ceremony = 0;
        CeremonyElapsedMs = 0;
        IsFeedMenuOpen = false;
        ResetPosition();
        NeedsSave = true;
    }

    private static int Clamp100(int value) => Math.Clamp(value, 0, 100);
}
