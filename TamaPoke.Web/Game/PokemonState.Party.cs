using System.Text.Json.Serialization;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 파티의 다른 포켓몬. 대표는 PokemonState 자신이고, Party에는 나머지만 둡니다. (대표 포함 최대 6마리)
public sealed class PartyMember
{
    public int SpeciesId { get; set; }
    public string SpriteForm { get; set; } = "";
    public bool IsShiny { get; set; }
    public int AgeMinutes { get; set; }
    public bool IsFemale { get; set; }
    public bool IsGenderless { get; set; }
    public int TrAtk { get; set; }
    public int TrDef { get; set; }
    public int TrSpeed { get; set; }
    public int Fullness { get; set; } = 80;
    public int Joy { get; set; } = 80;
    public int Energy { get; set; } = 80;
    public int Hygiene { get; set; } = 100;
    public int Bond { get; set; }
    public int Weight { get; set; }
    public int CareMistakes { get; set; }
    public bool BerryKnown { get; set; }
    public bool IsEvolutionPostponed { get; set; }
    public int Medals { get; set; }
    public int[] Skills { get; set; } = new int[4];
    public PokemonGene Genes { get; set; } = new();

    [JsonIgnore] public int Level => 1 + AgeMinutes / 60;
}

public sealed partial class PokemonState
{
    private const int MAX_OTHER_PARTY_MEMBERS = 5;

    // 파티가 꽉 찬 상태에서 잡은 포켓몬. 내보낼 멤버를 고르거나 포기할 때까지 기다립니다.
    [JsonIgnore] public PartyMember? PendingCatch { get; private set; }
    [JsonIgnore] public bool IsSwapMode => PendingCatch is not null;
    [JsonIgnore] public bool CanChangeLeader => !IsEgg && !IsCeremony && !IsAnyMiniGameOpen && !IsBattleOpen;

    // 이 포켓몬을 대표로 부릅니다. 지금 대표는 그 자리로 갑니다.
    public void MakeLeader(PartyMember member)
    {
        int index = Party.IndexOf(member);
        if (index < 0 || !CanChangeLeader) return;

        Party[index] = CreateLeaderSnapshot();
        LoadFromPartyMember(member);
        NeedsSave = true;
    }

    // 파티가 꽉 찼을 때: 고른 멤버를 내보내고 새 포켓몬을 넣습니다.
    public void ReplaceWithPendingCatch(PartyMember member)
    {
        int index = Party.IndexOf(member);
        if (index < 0 || PendingCatch is null) return;

        Party[index] = PendingCatch;
        PendingCatch = null;
        NeedsSave = true;
    }

    public void ReleasePendingCatch()
    {
        PendingCatch = null;
        ShowEvent(L.T("party.released.message"));
        NeedsSave = true;
    }

    private void AddCaughtMember(PartyMember member)
    {
        if (Party.Count >= MAX_OTHER_PARTY_MEMBERS) PendingCatch = member;
        else Party.Add(member);
        NeedsSave = true;
    }

    private PartyMember CreateLeaderSnapshot() => new()
    {
        SpeciesId = SpeciesId,
        SpriteForm = SpriteForm,
        IsShiny = IsShiny,
        AgeMinutes = AgeMinutes,
        IsFemale = IsFemale,
        IsGenderless = IsGenderless,
        TrAtk = TrAtk,
        TrDef = TrDef,
        TrSpeed = TrSpeed,
        Fullness = Fullness,
        Joy = Joy,
        Energy = Energy,
        Hygiene = Hygiene,
        Bond = Bond,
        Weight = Weight,
        CareMistakes = CareMistakes,
        BerryKnown = BerryKnown,
        IsEvolutionPostponed = IsEvolutionPostponed,
        Medals = Medals,
        Skills = (int[])Skills.Clone(),
        Genes = Genes,
    };

    private void LoadFromPartyMember(PartyMember member)
    {
        SpeciesId = member.SpeciesId;
        SpriteForm = member.SpriteForm;
        IsShiny = member.IsShiny;
        AgeMinutes = member.AgeMinutes;
        AgeSeconds = member.AgeMinutes * 60;
        IsFemale = member.IsFemale;
        IsGenderless = member.IsGenderless;
        TrAtk = member.TrAtk;
        TrDef = member.TrDef;
        TrSpeed = member.TrSpeed;
        Fullness = member.Fullness;
        Joy = member.Joy;
        Energy = member.Energy;
        Hygiene = member.Hygiene;
        Bond = member.Bond;
        Weight = member.Weight;
        CareMistakes = member.CareMistakes;
        BerryKnown = member.BerryKnown;
        IsEvolutionPostponed = member.IsEvolutionPostponed;
        Medals = member.Medals;
        Skills = (int[])member.Skills.Clone();
        Genes = member.Genes;

        // 교체되어 나온 포켓몬은 깨어 있는 상태로 시작합니다.
        IsSleeping = false;
        IsFarewellPostponed = false;
        NeglectTicks = 0;
        Poops = 0;
        MistakeCooldown = 0;
        ResetPosition();
        if (SkillCount == 0) RelearnFromLevel();
    }
}
