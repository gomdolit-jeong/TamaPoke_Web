using System.Text.Json.Serialization;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 서바이벌: 잡은 포켓몬 하나를 골라 10분 동안 버팁니다. 능력치는 모두 같은 기준(SurvivalService)이고, 게임 안에서 성장합니다. (게임은 wwwroot/js/survival.js, 화면은 Components/SurvivalLayer.razor)
// 게임 중에는 산책처럼 다른 시간이 멈춥니다.
public sealed partial class PokemonState
{
    [JsonIgnore] public bool IsSurviving { get; private set; }
    [JsonIgnore] public bool CanStartSurvival => CanStartActivity && Energy >= SurvivalRules.MIN_ENERGY;

    // 지금까지의 최고 기록 (주간 랭킹과 따로, 이 세이브에 남습니다)
    public int SurvivalBestScore { get; set; }
    public int SurvivalBestTime { get; set; }
    public int SurvivalBestKills { get; set; }

    // 마지막으로 고른 포켓몬 (다음에 창을 열면 먼저 골라 둡니다)
    public int SurvivalSpecies { get; set; }

    // 고를 수 있는 포켓몬: 잡은 포켓몬과 지금 포켓몬의 첫 모습 (진화한 포켓몬을 잡았으면 진화 전 모습으로). 게임 안에서 진화합니다.
    [JsonIgnore] public IEnumerable<int> SurvivalCandidates =>
        (IsEgg ? UnlockedPokemon : UnlockedPokemon.Append(SpeciesId)).Select(Dex.BaseOf).Distinct()
            .Where(id => Dex.Get(id)?.HasSprite == true).OrderBy(id => id);

    public bool BeginSurvival(int speciesId)
    {
        if (!CanStartSurvival || !SurvivalCandidates.Contains(speciesId)) return false;
        SurvivalSpecies = speciesId;
        IsFeedMenuOpen = false;
        IsPlayMenuOpen = false;
        IsProfileOpen = false;
        IsSurviving = true;
        return true;
    }

    public void EndSurvival()
    {
        IsSurviving = false;
        ResetPosition();
        NeedsSave = true;
    }

    // 결과에 따라 보상을 주고, 받은 내용을 줄마다 돌려줍니다. 새 최고 기록이면 isBest가 true입니다.
    public (List<string> Rewards, bool IsBest) FinishSurvival(SurvivalResult result)
    {
        result = SurvivalRules.Clamp(result);
        int score = SurvivalRules.Score(result);
        int minutes = result.TimeSec / 60;
        var rewards = new List<string>();

        // 예전 점수 방식(초 × 10 + KO)의 기록은 새 방식으로 바꿔서 비교합니다.
        if (SurvivalBestScore > 0 && SurvivalBestScore < SurvivalRules.SCORE_TIME_UNIT)
            SurvivalBestScore = SurvivalRules.Score(SurvivalBestTime, SurvivalBestKills, false);
        bool isBest = score > SurvivalBestScore;
        if (isBest)
        {
            SurvivalBestScore = score;
            SurvivalBestTime = result.TimeSec;
            SurvivalBestKills = result.Kills;
        }

        // 놀이처럼 즐거워지고, 뛰어다닌 만큼 배고프고 지칩니다.
        Joy = Clamp100(Joy + 10);
        Energy = Math.Max(5, Energy - (8 + minutes * 3));
        Fullness = Math.Max(5, Fullness - (5 + minutes * 2));
        Weight = Math.Max(0, Weight - minutes);

        int atk = Math.Min(10, result.Kills / 80), def = Math.Min(8, minutes), spd = Math.Min(8, minutes);
        int bond = Math.Min(10, 2 + minutes);
        TrAtk = Math.Min(100, TrAtk + atk);
        TrDef = Math.Min(100, TrDef + def);
        TrSpeed = Math.Min(100, TrSpeed + spd);
        Bond = Clamp100(Bond + bond);
        if (atk + def + spd > 0) rewards.Add(L.F("web.survival.reward.training", atk, def, spd));
        rewards.Add(L.F("web.survival.reward.bond", bond));

        // 2분마다 상처약 상자 하나, 다 버티면 좋은 상처약과 메가스톤 조각 기회
        for (int i = 0; i < result.TimeSec / 120; i++)
        {
            int roll = Random.Shared.Next(100);
            string item = roll < 55 ? ItemDex.Potion : roll < 80 ? ItemDex.MonsterBall : roll < 95 ? ItemDex.SuperPotion : ItemDex.HyperPotion;
            AddItem(item, 1);
            rewards.Add(L.F("web.survival.reward.item", L.T($"item.{item}.name")));
        }
        if (result.Cleared)
        {
            AddItem(ItemDex.HyperPotion, 1);
            rewards.Add(L.F("web.survival.reward.item", L.T($"item.{ItemDex.HyperPotion}.name")));
            if (Random.Shared.Next(100) < 40 && GiveMegaShards(1) is { } shard) rewards.Add(shard);
        }

        NeedsSave = true;
        return (rewards, isBest);
    }
}
