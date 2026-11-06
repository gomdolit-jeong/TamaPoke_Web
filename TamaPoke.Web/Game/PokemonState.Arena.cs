using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 다른 트레이너가 등록한 포켓몬과 하는 대전. 각자 키운 레벨 그대로 싸우고(최대 100), 상대는 컴퓨터가 조종합니다.
public sealed partial class PokemonState
{
    private ArenaEntry? _arenaMe;
    private bool _arenaReported;

    // 대전 닉네임 (영어 글자와 숫자). 세이브에 남겨서 시즌이 바뀌어도 그대로 씁니다.
    public string? ArenaNickname { get; set; }

    [JsonIgnore] public ArenaEntry? ArenaFoe { get; private set; }
    [JsonIgnore] public bool IsArenaBattle => ArenaFoe is not null;

    // 대전이 끝나면 새 기록(점수, 승패)을 넘깁니다. 화면 쪽에서 서버에 저장합니다.
    [JsonIgnore] public Action<ArenaEntry>? ArenaResultReady { get; set; }

    [JsonIgnore] public int PlayerBattleLevel => BattleLevel;
    private int BattleLevel => IsArenaBattle ? ArenaRules.ArenaLevel(Level) : Level;

    // 상대 능력치: 대전에서는 상대가 키운 유전자와 훈련을 내 포켓몬과 같은 공식으로 씁니다.
    private int EnemyAttackStat(PokemonInfo? info, bool special)
    {
        if (ArenaFoe is not { } foe || info is null) return WildStat(special ? info?.BaseSpA : info?.BaseAtk);
        return special
            ? BattleCalculator.Stat(info.BaseSpA, foe.Genes[1] - 80, foe.Level) + foe.Training[0]
            : BattleCalculator.Stat(info.BaseAtk, foe.Genes[1] + foe.Training[0] / 4, foe.Level);
    }

    private int EnemyDefenseStat(PokemonInfo? info, bool special)
    {
        if (ArenaFoe is not { } foe || info is null) return WildStat(special ? info?.BaseSpD : info?.BaseDef);
        return special
            ? BattleCalculator.Stat(info.BaseSpD, foe.Genes[2] - 80, foe.Level) + foe.Training[1] / 2
            : BattleCalculator.Stat(info.BaseDef, foe.Genes[2] + foe.Training[1] / 4, foe.Level);
    }

    private int EnemySpeedStat()
    {
        var info = Dex.Get(EnemySpeciesId);
        if (ArenaFoe is not { } foe || info is null) return WildStat(info?.BaseSpeed);
        return BattleCalculator.Stat(info.BaseSpeed, foe.Genes[3] + foe.Training[2] / 4, foe.Level);
    }

    public async void StartArenaBattle(ArenaEntry foe, ArenaEntry me)
    {
        if (!CanStartActivity || IsEgg) return;
        BeginBattleSession();
        PrepareBattleScreen();
        ArenaFoe = foe;
        _arenaMe = me;
        _arenaReported = false;

        EnemySpeciesId = foe.SpeciesId;
        EnemyLevel = foe.Level;
        _enemyForm = foe.Form;
        IsEnemyShiny = foe.Shiny;
        IsEnemyFemale = foe.Female;
        IsEnemyGenderless = false;

        PlayerMaxHp = CombatMaxHp;
        PlayerHp = PlayerMaxHp;
        var info = Dex.Get(EnemySpeciesId);
        EnemyMaxHp = BattleCalculator.MaxHp(info?.BaseHp ?? 50, foe.Genes[0] + foe.Training[1] / 4, foe.Level);
        EnemyHp = EnemyMaxHp;

        for (int i = 0; i < 4; i++) EnemySkills[i] = foe.Skills[i];
        if (EnemySkills.All(id => id == 0)) GenerateEnemySkills();
        _enemyTempActionTimer = 0;
        UpdateEnemyAnimation(PokemonAnim.IDLE);

        IsBattleOpen = true;
        IsPlayerTurn = false;
        BattleMessage = L.F("web.arena.intro", ArenaNames.Display(foe), EnemyName);

        await Task.Delay(2000);
        if (!IsArenaBattle) return;
        BattleMessage = L.T("battle.choose");
        IsPlayerTurn = true;
    }

    // 점수를 계산해서 보여 주고, 저장은 화면 쪽에 맡깁니다. (한 대전에 한 번만)
    private async Task ShowArenaResultAsync(bool won)
    {
        if (_arenaReported || ArenaFoe is not { } foe || _arenaMe is not { } me) return;
        _arenaReported = true;

        int change = won ? ArenaRules.WinPoints(me.Points, foe.Points) : -ArenaRules.LossPoints(me.Points, foe.Points);
        var result = me with
        {
            Points = Math.Max(0, me.Points + change),
            Wins = me.Wins + (won ? 1 : 0),
            Losses = me.Losses + (won ? 0 : 1),
        };
        ArenaResultReady?.Invoke(result);

        BattleMessage = L.F("web.arena.points", (change >= 0 ? "+" : "") + change, result.Points);
        await Task.Delay(2500);
    }

    // 도망은 기권으로, 진 것과 같습니다.
    private async Task ForfeitArenaAsync()
    {
        IsBattleResolved = true;
        BattleMessage = L.T("web.arena.forfeit");
        await Task.Delay(1500);
        await ShowArenaResultAsync(won: false);
        CloseBattle();
        IsPlayerTurn = true;
    }
}
