namespace TamaPoke.Web.Game;

// 배틀 기술 효과: 서바이벌과 같은 기준(SurvivalRules.KindFor / FxFor)으로 기술마다 효과 그림을 고르고,
// 기술 타입 색으로 그린 시트(wwwroot/js/battle-fx.js)를 공격 모양에 맞게 재생합니다.
//   bolt(탄환): 쓴 쪽에서 번쩍이고 날아가서 터짐 / spray(부채꼴): 쓴 쪽 입에서 상대까지 뿜음
//   slash(베기): 상대 위에서 휘두름 / nova(충격파)·orbit·chain(번개): 상대 자리에서 터짐 / aura(변화 기술): 효과를 받는 쪽에
public sealed partial class PokemonState
{
    // 효과 시트 주소를 얻는 함수 (배틀 화면이 붙여 줌): (그림 이름, 타입, 색) → 그림 주소. 없거나 실패하면 null
    [System.Text.Json.Serialization.JsonIgnore] public Func<string, string, string, Task<string?>>? BattleFxSheet { get; set; }

    // 지금 화면에 보이는 프레임의 머리 위치를 얻는 함수 (배틀 화면이 붙여 줌): 상대 쪽인지 → 배틀 화면 좌표. 못 찾으면 null
    [System.Text.Json.Serialization.JsonIgnore] public Func<bool, Task<double[]?>>? BattleFxHead { get; set; }
    private const int HEAD_WAIT_MS = 300;

    private const int SHEET_WAIT_MS = 1500;     // 시트가 아직 안 그려졌을 때 기다리는 최대 시간
    private const double BOLT_TRAVEL_MS = 380;  // 탄환이 날아가는 시간

    // 베기 그림이 원래 향한 방향(라디안). 공격 방향에서 이만큼 빼서 돌립니다. (survival.js의 SLASH_TURN과 같은 값)
    private static readonly Dictionary<string, double> SlashTurn = new()
    {
        ["w_slash"] = -0.8, ["sl_sword"] = -0.7, ["sl_punch"] = -1.1, ["sl_double"] = -0.6, ["sl_scythe"] = -1.57,
    };

    // 옆에서 본 납작한 베기 그림: 돌리지 않고 좌우만 뒤집습니다.
    private static readonly HashSet<string> SlashFlat = ["sl_spin", "sl_hammer", "sl_cleave"];

    // 쏘는 순간 효과는 앞부분(번쩍임)만 씁니다. (survival.js의 PFX_CUT)
    private const double MUZZLE_CUT = 0.35;

    private static (string Kind, string Fx, string Type, string Color) MoveLook(SkillInfo skill)
    {
        string kind = SurvivalRules.KindFor(skill);
        return (kind, SurvivalRules.FxFor(skill, kind), skill.Type.ToString().ToLowerInvariant(), SurvivalRules.ColorFor(skill.Type));
    }

    // 이 기술이 쓰는 효과 그림들 (미리 그려 두기용)
    private static IEnumerable<string> SheetsFor(SkillInfo skill)
    {
        var (kind, fx, _, _) = MoveLook(skill);
        if (fx == "s_wave") { yield return "s_jet"; yield return "n_splash"; }
        else if (PfxSheets.All.ContainsKey(fx)) yield return fx;
        if (kind == "bolt") { yield return "c_muzzle"; yield return "h_burst"; }
    }

    // 지금 배틀에서 양쪽 기술이 쓸 효과 그림 목록: (그림 이름, 타입, 색)
    public List<(string Name, string Type, string Color)> BattleFxNeeded()
    {
        var skills = Skills.Concat(EnemySkills).Where(id => id != 0).Distinct()
            .Select(SkillBook.GetSkill).OfType<SkillInfo>();
        var list = new List<(string, string, string)>();
        foreach (var skill in skills)
        {
            var (_, _, type, color) = MoveLook(skill);
            foreach (string name in SheetsFor(skill))
                if (!name.StartsWith("st_") && !list.Contains((name, type, color))) list.Add((name, type, color));
        }
        return list;
    }

    // 효과 시트 주소 (st_는 원래 색 그대로). 그릴 수 없거나 너무 오래 걸리면 null
    private async Task<string?> SheetAsync(string name, string type, string color)
    {
        if (!PfxSheets.All.ContainsKey(name)) return null;
        if (name.StartsWith("st_")) return $"pfx/{name}.png";
        if (BattleFxSheet is null) return null;
        try
        {
            var load = BattleFxSheet(name, type, color);
            return await Task.WhenAny(load, Task.Delay(SHEET_WAIT_MS)) == load ? await load : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 맞는 쪽이 멀리(상대) 있으면 작게, 가까이(내 포켓몬) 있으면 크게
    private static double Near(bool atEnemy) => atEnemy ? 0.8 : 1.1;

    // 입 위치: 스프라이트에 찍힌 지금 프레임의 머리 위치(BattleFxHead). 못 찾으면 몸 가운데에서 머리 쪽으로 대강 옮긴 곳.
    // 부채꼴과 탄환은 여기서 나갑니다. (가운데에서 나가면 배에서 뿜는 것처럼 보임)
    private const double PLAYER_MOUTH_DX = 14, PLAYER_MOUTH_DY = -34, ENEMY_MOUTH_DX = -8, ENEMY_MOUTH_DY = -14;
    private const double BOLT_SCALE = 0.8, MUZZLE_SCALE = 0.75;

    private async Task<(double X, double Y)> MouthOfAsync(bool enemy)
    {
        if (BattleFxHead is not null)
        {
            try
            {
                var find = BattleFxHead(enemy);
                if (await Task.WhenAny(find, Task.Delay(HEAD_WAIT_MS)) == find && await find is [var hx, var hy]) return (hx, hy);
            }
            catch (Exception) { /* 대강 옮긴 곳을 씁니다 */ }
        }
        var (x, y) = FxCenter(enemy);
        return enemy ? (x + ENEMY_MOUTH_DX, y + ENEMY_MOUTH_DY) : (x + PLAYER_MOUTH_DX, y + PLAYER_MOUTH_DY);
    }

    private static int WaitMs(BattlePfx fx, double max) => (int)Math.Min(fx.LengthMs, max);

    // 머리 점은 얼굴 가운데라서, 효과가 얼굴을 덮지 않게 공격 방향으로 이만큼(px, 가까운 쪽은 크게) 띄웁니다.
    private const double MOUTH_GAP = 14;

    // 쓴 쪽 입 앞에서 맞는 쪽 가운데까지: 시작 위치, 방향(단위 벡터), 거리, 각도(도)
    private async Task<(double X, double Y, double Ux, double Uy, double Dist, double AngleDeg)> FromMouthAsync(bool targetIsEnemy, double tx, double ty)
    {
        var (mx, my) = await MouthOfAsync(!targetIsEnemy);
        double dx = tx - mx, dy = ty - my, dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double ux = dx / dist, uy = dy / dist;
        double gap = Math.Min(MOUTH_GAP * Near(!targetIsEnemy), dist / 3);
        return (mx + ux * gap, my + uy * gap, ux, uy, dist - gap, Math.Atan2(dy, dx) * 180 / Math.PI);
    }

    // 공격 기술 효과. 재생했으면 true (그림을 못 구하면 false → 예전 이펙트)
    private async Task<bool> PlayMoveFxAsync(SkillInfo skill, bool targetIsEnemy)
    {
        var (kind, fx, type, color) = MoveLook(skill);
        string main = fx == "s_wave" ? "s_jet" : fx;
        string? image = await SheetAsync(main, type, color);
        if (image is null) return false;

        var (ax, ay) = FxCenter(!targetIsEnemy);
        var (tx, ty) = FxCenter(targetIsEnemy);
        double dx = tx - ax, dy = ty - ay, dist = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / dist, uy = dy / dist;
        double angleRad = Math.Atan2(dy, dx), angle = angleRad * 180 / Math.PI;
        double near = Near(targetIsEnemy);
        var sheet = PfxSheets.All[main];

        switch (kind)
        {
            case "bolt":
            {
                // 입에서 번쩍 → 날아감 → 맞는 쪽에서 터짐
                var (mx, my, mux, muy, mdist, mangle) = await FromMouthAsync(targetIsEnemy, tx, ty);
                if (await SheetAsync("c_muzzle", type, color) is { } muzzle)
                    AddBattlePfx("c_muzzle", mx, my, MUZZLE_SCALE * Near(!targetIsEnemy), angleDeg: mangle, image: muzzle, cut: MUZZLE_CUT);
                double sx = mx + mux * 4, sy = my + muy * 4;
                double travel = mdist - 4;
                AddBattlePfx(main, sx, sy, BOLT_SCALE, angleDeg: mangle, image: image,
                    vx: mux * travel / BOLT_TRAVEL_MS, vy: muy * travel / BOLT_TRAVEL_MS, lifeMs: BOLT_TRAVEL_MS);
                await Task.Delay((int)BOLT_TRAVEL_MS);
                if (await SheetAsync("h_burst", type, color) is { } burst)
                    AddBattlePfx("h_burst", tx, ty, 0.32 * near, speed: 1.4, image: burst);
                return true;
            }
            case "spray":
            {
                // 입에서 상대까지 닿게 늘려서 뿜습니다.
                var (mx, my, _, _, mdist, mangle) = await FromMouthAsync(targetIsEnemy, tx, ty);
                double reach = Math.Max(20, sheet.Fw - sheet.Ox);
                double scale = Math.Clamp(mdist * 0.95 / reach, 0.5, 1.6);
                var pfx = new BattlePfx(main, mx, my, scale, false, mangle, 1, BattleClockMs, image);
                AddBattlePfx(main, pfx.X, pfx.Y, scale, angleDeg: mangle, image: image);
                if (fx == "s_wave" && await SheetAsync("n_splash", type, color) is { } splash)
                    AddBattlePfx("n_splash", tx, ty + 18, 0.7 * near, image: splash, delayMs: 250);
                await Task.Delay(WaitMs(pfx, 650));
                return true;
            }
            case "slash":
            {
                // 상대 쪽에서 휘두릅니다. 납작한 그림은 좌우만 뒤집고, 나머지는 공격 방향으로 돌립니다.
                double scale = 0.75 * near;
                BattlePfx pfx;
                if (SlashFlat.Contains(main))
                    pfx = new BattlePfx(main, tx, ty + 10, scale, dx < 0, 0, 1, BattleClockMs, image);
                else
                {
                    double turn = SlashTurn.GetValueOrDefault(main, 0);
                    pfx = new BattlePfx(main, tx - ux * 26 * near, ty - uy * 26 * near, scale, false, (angleRad - turn) * 180 / Math.PI, 1, BattleClockMs, image);
                }
                AddBattlePfx(main, pfx.X, pfx.Y, scale, pfx.Flip, pfx.AngleDeg, image: image);
                // pixelfx 타격 타이밍: 맞는 순간 멈추고, 흔들리고, 상대가 밀려납니다.
                double stopMs = QueueActHits(main, targetIsEnemy, BattleClockMs, 1);
                await Task.Delay(WaitMs(pfx, 450) + (int)stopMs);
                return true;
            }
            case "nova":
            case "orbit":
            case "chain":
            {
                // 상대 자리에서 터집니다. 큰 그림은 상대 크기에 맞춰 줄입니다. (번개는 위에서 떨어지므로 발밑 기준)
                double width = (kind == "nova" ? 120 : 80) * near;
                double scale = Math.Min(1.0 * near, width / sheet.Fw);
                double y = main is "c_strike" or "n_slam" or "n_fire" or "n_frost" or "n_splash" ? ty + 22 * near : ty;
                double turn = main == "w_chain" ? angle : 0;
                var pfx = new BattlePfx(main, tx, y, scale, false, turn, 1.2, BattleClockMs, image);
                AddBattlePfx(main, tx, y, scale, angleDeg: turn, speed: 1.2, image: image);
                double stopMs = QueueActHits(main, targetIsEnemy, BattleClockMs, 1.2);
                await Task.Delay(WaitMs(pfx, 650) + (int)stopMs);
                return true;
            }
            default:
                return await PlayAuraFxAsync(skill, main, image, onPlayer: !targetIsEnemy);
        }
    }

    // 변화 기술 효과: 자기 능력치를 올리거나 회복·방어하는 기술은 쓴 쪽에, 나머지는 상대 쪽에 보입니다. 재생했으면 true
    private async Task<bool> PlayStatusMoveFxAsync(SkillInfo skill, bool userIsPlayer)
    {
        var (_, fx, type, color) = MoveLook(skill);
        string? image = await SheetAsync(fx, type, color);
        if (image is null) return false;
        bool selfMove = fx is "a_heal" or "a_field" or "a_shield" or "st_rage" or "st_haste" or "st_bloom" && skill.Ailment == SkillAilment.None
                        || (skill.StatTarget == StatTarget.User && skill.Ailment == SkillAilment.None);
        bool onPlayer = selfMove ? userIsPlayer : !userIsPlayer;
        return await PlayAuraFxAsync(skill, fx, image, onPlayer);
    }

    private async Task<bool> PlayAuraFxAsync(SkillInfo skill, string name, string image, bool onPlayer)
    {
        var (x, y) = FxCenter(!onPlayer);
        double near = Near(!onPlayer);
        // 머리 위 상태 표시(st_)는 위쪽에, 바닥에 깔리는 효과는 발밑에, 나머지는 가운데에
        if (name.StartsWith("st_")) y -= 22 * near;
        else if (name is "a_puddle" or "a_whirl" or "a_heal" or "a_field") y += 20 * near;
        double scale = Math.Min(0.9 * near, 110 * near / PfxSheets.All[name].Fw);
        var pfx = new BattlePfx(name, x, y, scale, false, 0, 1.3, BattleClockMs, image);
        AddBattlePfx(name, x, y, scale, speed: 1.3, image: image);
        await Task.Delay(WaitMs(pfx, 900));
        return true;
    }
}
