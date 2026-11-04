using System.Text.Json.Serialization;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 산책 규칙. PC판 PokemonState.Roaming.cs와 같습니다. (돌아다니는 모습은 Components/WalkLayer.razor)
public sealed partial class PokemonState
{
    private const int WALK_STAT_FLOOR = 30;
    private const int WALK_ITEM_CHANCE_PER_MIN = 5;
    private const int WALK_MAX_ITEMS = 5;
    private const int WALK_MINUTES_PER_AGE = 3;
    private const double EVENT_MESSAGE_MS = 6000;

    private static readonly (string ItemId, int Weight)[] WalkItemTable =
    [
        (ItemDex.Potion, 50), (ItemDex.MonsterBall, 35), (ItemDex.SuperPotion, 12), (ItemDex.HyperPotion, 3),
    ];

    private readonly List<string> _walkFoundItems = [];
    private int _walkSeconds, _walkMinutes, _walkStartLevel;
    private double _eventMessageMs;

    [JsonIgnore] public bool IsWalking { get; private set; }
    [JsonIgnore] public bool CanStartWalk => !IsBattleOpen && !IsAnyMiniGameOpen && !IsCeremony;
    [JsonIgnore] public string? EventMessage { get; private set; }
    [JsonIgnore] public Action<string, string>? SendNotification { get; set; }

    // 다시 불러온 뒤 보여 줄 안내 (공장 초기화 등)
    public string? PendingNoticeKey { get; set; }

    // 주운 아이템: 산책 화면이 포켓몬 하나가 풀숲을 뒤적이게 한 뒤 말풍선으로 보여줍니다. (그림 파일 이름)
    [JsonIgnore] public Queue<string> WalkFoundIcons { get; } = new();

    public void BeginWalk()
    {
        if (!CanStartWalk) return;
        IsFeedMenuOpen = false;
        IsPlayMenuOpen = false;
        IsProfileOpen = false;
        IsSleeping = false;
        IsWalking = true;
        _walkSeconds = 0;
        _walkMinutes = 0;
        _walkStartLevel = Level;
        _walkFoundItems.Clear();
        WalkFoundIcons.Clear();
    }

    public void EndWalk()
    {
        IsWalking = false;
        ResetPosition();
        NeedsSave = true;
        if (_walkMinutes == 0) return;

        string summary = Level > _walkStartLevel
            ? L.F("walk.summary.levelup", _walkStartLevel, Level)
            : L.F("walk.summary.minutes", _walkMinutes);
        if (_walkFoundItems.Count > 0)
        {
            summary += _walkFoundItems.Count == 1
                ? L.F("walk.summary.item.one", _walkFoundItems[0])
                : L.F("walk.summary.item.many", _walkFoundItems[0], _walkFoundItems.Count - 1);
        }
        ShowEvent(summary);
    }

    // 잠깐 보여 주는 안내. 평소에는 기분 말풍선, 산책 중에는 산책 화면 위에 나옵니다.
    public void ShowEvent(string message, double ms = EVENT_MESSAGE_MS)
    {
        EventMessage = message;
        _eventMessageMs = ms;
    }

    private void UpdateEventMessage(double ms)
    {
        if (_eventMessageMs <= 0) return;
        _eventMessageMs -= ms;
        if (_eventMessageMs <= 0) EventMessage = null;
    }

    private void WalkTick()
    {
        if (DateTime.Now.Date > LastPlayedDate.Date) CheckDailyStreak();
        if (++_walkSeconds % 60 != 0) return;
        _walkMinutes++;

        // 나이(레벨)는 평소의 ⅓ 속도로 증가합니다.
        bool ageThisMinute = _walkMinutes % WALK_MINUTES_PER_AGE == 0;

        if (!IsEgg)
        {
            if (ageThisMinute)
            {
                AgeMinutes++;
                AgeSeconds = AgeMinutes * 60;
                if (AgeMinutes % MINUTES_PER_LEVEL == 0)
                {
                    IsEvolutionPostponed = false;
                    IsFarewellPostponed = false;
                }
            }
            NeglectTicks = 0;

            if (Weight > 0 && _walkMinutes % WEIGHT_BURN_EVERY_MINUTES == 0) Weight--;
            Joy = ApplyWalkStat(Joy, _walkMinutes, 10, +1);
            Fullness = ApplyWalkStat(Fullness, _walkMinutes, 20, -1);
            Energy = ApplyWalkStat(Energy, _walkMinutes, 20, -1);
            Hygiene = ApplyWalkStat(Hygiene, _walkMinutes, 30, -1);
        }

        foreach (var member in Party)
        {
            if (ageThisMinute) member.AgeMinutes++;
            if (member.Weight > 0 && _walkMinutes % WEIGHT_BURN_EVERY_MINUTES == 0) member.Weight--;
            member.Joy = ApplyWalkStat(member.Joy, _walkMinutes, 10, +1);
            member.Fullness = ApplyWalkStat(member.Fullness, _walkMinutes, 20, -1);
            member.Energy = ApplyWalkStat(member.Energy, _walkMinutes, 20, -1);
            member.Hygiene = ApplyWalkStat(member.Hygiene, _walkMinutes, 30, -1);
        }

        TryFindWalkItem();
        NeedsSave = true;
    }

    // everyMinutes마다 delta만큼 변화. 감소는 하한선(30) 아래로 내려가지 않고, 이미 그보다 낮으면 그대로 둡니다.
    private static int ApplyWalkStat(int value, int walkMinutes, int everyMinutes, int delta)
    {
        if (walkMinutes % everyMinutes != 0) return value;
        if (delta > 0) return Math.Min(100, value + delta);
        return value <= WALK_STAT_FLOOR ? value : Math.Max(WALK_STAT_FLOOR, value + delta);
    }

    private void TryFindWalkItem()
    {
        if (_walkFoundItems.Count >= WALK_MAX_ITEMS) return;
        if (Random.Shared.Next(100) >= WALK_ITEM_CHANCE_PER_MIN) return;

        // 메가링이 있으면 가끔 메가스톤 조각을 찾습니다.
        if (Random.Shared.Next(100) < WALK_SHARD_CHANCE && GiveMegaShards(1) is not null)
        {
            _walkFoundItems.Add(L.T("item.mega_shard.name"));
            AnnounceWalkFind(L.T("item.mega_shard.name"));
            WalkFoundIcons.Enqueue("icon_mega.png");
            return;
        }

        int roll = Random.Shared.Next(WalkItemTable.Sum(x => x.Weight));
        string itemId = WalkItemTable[^1].ItemId;
        foreach (var (id, weight) in WalkItemTable)
        {
            if (roll < weight)
            {
                itemId = id;
                break;
            }
            roll -= weight;
        }

        AddItem(itemId, 1);
        _walkFoundItems.Add(L.T($"item.{itemId}.name"));
        AnnounceWalkFind(L.T($"item.{itemId}.name"));
        WalkFoundIcons.Enqueue(ItemDex.All.First(item => item.Id == itemId).Icon);
    }

    private void AnnounceWalkFind(string itemName)
    {
        string message = L.F("walk.found.message", itemName);
        ShowEvent(message);
        Notify(L.T("walk.found.title"), message);
    }

    // 브라우저 알림. 사용자가 켰을 때, 탭이 보이지 않을 때만 뜹니다. (app.js)
    private void Notify(string title, string body)
    {
        if (Settings.UseNotifications) SendNotification?.Invoke(title, body);
    }

    // 산책 나갈 포켓몬: 대표(알이면 제외)와 파티 순서대로, 설정한 마릿수까지
    public List<(int SpeciesId, string Form, bool IsShiny, bool IsFemale)> WalkMembers()
    {
        var members = new List<(int, string, bool, bool)>();
        if (!IsEgg) members.Add((SpeciesId, SpriteForm, IsShiny, IsFemale));
        members.AddRange(Party.Select(m => (m.SpeciesId, m.SpriteForm, m.IsShiny, m.IsFemale)));
        return members.Take(Math.Clamp(Settings.RoamingPokemonCount, 1, 6)).ToList();
    }
}
