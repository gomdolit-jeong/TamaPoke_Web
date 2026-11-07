using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

// 프로필에서 기술을 누르면 보여 줄 설명. 이 게임의 배틀 규칙(PokemonState.BattleTurns)대로만 적습니다.
public static class SkillText
{
    public static string Category(SkillInfo skill) => L.T($"web.skill.category.{skill.Category.ToString().ToLowerInvariant()}");

    public static string Power(SkillInfo skill) => skill.Power > 0 ? skill.Power.ToString() : "-";

    public static string Accuracy(SkillInfo skill) => skill.Accuracy == 0 ? L.T("web.skill.alwaysHits") : $"{skill.Accuracy}%";

    // 효과 설명 줄들. 아무 효과가 없으면 빈 목록입니다.
    public static List<string> Effects(SkillInfo skill)
    {
        var lines = new List<string>();
        if (!skill.IsSupported)
        {
            lines.Add(L.T("web.skill.unsupported"));
            return lines;
        }

        if (skill.Priority > 0) lines.Add(L.F("web.skill.priorityUp", skill.Priority));
        if (skill.Priority < 0) lines.Add(L.F("web.skill.priorityDown", -skill.Priority));

        if (skill.MaxHits > 1)
            lines.Add(skill.MinHits >= skill.MaxHits ? L.F("web.skill.hitsFixed", skill.MaxHits) : L.F("web.skill.hitsRange", skill.MinHits, skill.MaxHits));

        if (skill.Ailment != SkillAilment.None)
        {
            string ailment = L.T($"ailment.name.{AilmentKey(skill.Ailment)}");
            lines.Add(skill.AilmentChance is <= 0 or >= 100
                ? L.F("web.skill.ailmentAlways", ailment)
                : L.F("web.skill.ailmentChance", skill.AilmentChance, ailment));
        }

        if (skill.StatChanges is { Count: > 0 } changes)
        {
            string who = L.T(skill.StatTarget == StatTarget.User ? "web.skill.self" : "web.skill.target");
            string list = string.Join(", ", changes.Select(c =>
                L.F(c.Change > 0 ? "web.skill.statUp" : "web.skill.statDown", L.T($"battle.stat.{c.Stat}"), Math.Abs(c.Change))));
            bool always = skill.Category == SkillCategory.Status || skill.StatChance is <= 0 or >= 100;
            lines.Add(always ? L.F("web.skill.statAlways", who, list) : L.F("web.skill.statChance", skill.StatChance, who, list));
        }

        if (skill.Drain > 0) lines.Add(L.F("web.skill.drain", skill.Drain));
        if (skill.Drain < 0) lines.Add(L.F("web.skill.recoil", -skill.Drain));
        if (skill.CritRate > 0) lines.Add(L.T("web.skill.highCrit"));
        if (skill.FlinchChance > 0) lines.Add(L.F("web.skill.flinch", skill.FlinchChance));

        if (skill.IsCharging) lines.Add(L.T("web.skill.charging"));
        if (skill.IsHiding) lines.Add(L.T("web.skill.hiding"));
        if (skill.NeedsRecharge) lines.Add(L.T("web.skill.recharge"));
        if (skill.IsRolling) lines.Add(L.T("web.skill.rolling"));
        else if (skill.IsUproar) lines.Add(L.T("web.skill.uproar"));
        else if (skill.IsLocking) lines.Add(L.T("web.skill.locking"));
        if (skill.IsTrapping) lines.Add(L.T("web.skill.trapping"));

        if (skill.IsFirstTurnOnly) lines.Add(L.T("web.skill.firstTurn"));
        if (skill.NeedsTargetAsleep) lines.Add(L.T("web.skill.targetAsleep"));
        if (skill.NeedsUserAsleep) lines.Add(L.T("web.skill.userAsleep"));
        if (skill.FailsIfHitFirst) lines.Add(L.T("web.skill.failsIfHit"));
        if (skill.NeedsPhysicalHitFirst) lines.Add(L.T("web.skill.afterPhysicalHit"));
        if (skill.NeedsTargetAttacking) lines.Add(L.T("web.skill.targetAttacking"));
        if (skill.NeedsSharedType) lines.Add(L.T("web.skill.sharedType"));
        if (skill.LosesOwnType) lines.Add(L.F("web.skill.losesType", skill.Type));
        if (skill.IsLastResort) lines.Add(L.T("web.skill.lastResort"));

        if (lines.Count == 0 && skill.Category != SkillCategory.Status) lines.Add(L.T("web.skill.plainAttack"));
        return lines;
    }

    private static string AilmentKey(SkillAilment ailment) => ailment switch
    {
        SkillAilment.BadPoison => "badPoison",
        _ => ailment.ToString().ToLowerInvariant(),
    };
}
