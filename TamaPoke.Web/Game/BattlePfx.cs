namespace TamaPoke.Web.Game;

// pixelfx(MIT, https://github.com/patch-gg/pixelfx)로 미리 그린 효과 시트 (wwwroot/pfx/이름.png).
// Fw x Fh 프레임이 가로로 Frames장 이어져 있고, (Ox, Oy)가 효과의 기준점입니다. (tools/make_pfx_sheets.js가 만든 값)
public sealed record PfxSheet(int Fw, int Fh, int Frames, int Fps, int Ox, int Oy)
{
    public double DurationMs => Frames * 1000.0 / Fps;
}

public static class PfxSheets
{
    public static readonly Dictionary<string, PfxSheet> All = new()
    {
        ["poof"] = new(82, 74, 24, 15, 37, 62),
        ["levelup"] = new(73, 81, 27, 20, 34, 78),
        ["hit_normal"] = new(101, 51, 15, 30, 25, 35),
        ["hit_fire"] = new(102, 51, 15, 30, 26, 35),
        ["hit_water"] = new(101, 51, 15, 30, 25, 35),
        ["hit_electric"] = new(102, 51, 15, 30, 26, 35),
        ["hit_grass"] = new(101, 51, 15, 30, 25, 35),
        ["hit_ice"] = new(101, 51, 15, 30, 25, 35),
        ["hit_fighting"] = new(101, 51, 15, 30, 25, 35),
        ["hit_poison"] = new(101, 51, 15, 30, 25, 35),
        ["hit_ground"] = new(101, 51, 15, 30, 25, 35),
        ["hit_flying"] = new(101, 51, 15, 30, 25, 35),
        ["hit_psychic"] = new(101, 51, 15, 30, 25, 35),
        ["hit_bug"] = new(100, 51, 15, 30, 24, 35),
        ["hit_rock"] = new(101, 51, 15, 30, 25, 35),
        ["hit_ghost"] = new(100, 51, 15, 30, 24, 35),
        ["hit_dragon"] = new(101, 51, 15, 30, 25, 35),
        ["hit_dark"] = new(99, 51, 15, 30, 23, 35),
        ["hit_steel"] = new(100, 51, 15, 30, 24, 35),
        ["hit_fairy"] = new(101, 51, 15, 30, 25, 35),
        ["crit"] = new(160, 94, 27, 30, 64, 64),

        // 서바이벌과 같은 기술 효과 (회색 그림, wwwroot/js/battle-fx.js가 기술 타입 색으로 그림. st_는 원래 색)
        ["w_bolt"] = new(114, 51, 16, 20, 91, 25),
        ["w_spray"] = new(146, 101, 23, 15, 12, 66),
        ["w_slash"] = new(66, 56, 18, 20, 5, 46),
        ["w_orbit"] = new(78, 100, 36, 12, 39, 92),
        ["w_chain"] = new(114, 76, 18, 20, 76, 38),
        ["w_aura"] = new(112, 81, 48, 12, 56, 61),
        ["nova"] = new(158, 144, 27, 15, 83, 118),
        ["a_puddle"] = new(100, 55, 40, 10, 50, 41),
        ["a_sun"] = new(102, 101, 40, 10, 51, 50),
        ["a_whirl"] = new(116, 70, 36, 12, 58, 35),
        ["b_arrow"] = new(92, 14, 12, 20, 88, 7),
        ["b_disc"] = new(92, 48, 10, 20, 72, 24),
        ["b_fireball"] = new(122, 56, 18, 15, 98, 28),
        ["b_ice"] = new(148, 33, 16, 20, 137, 12),
        ["b_laser"] = new(95, 27, 10, 20, 83, 13),
        ["c_strike"] = new(223, 262, 36, 12, 111, 226),
        ["n_dark"] = new(246, 240, 38, 10, 123, 201),
        ["n_fire"] = new(202, 105, 39, 10, 101, 69),
        ["n_frost"] = new(248, 122, 41, 12, 125, 90),
        ["n_plasma"] = new(199, 186, 26, 15, 101, 93),
        ["n_slam"] = new(237, 191, 38, 12, 119, 147),
        ["n_splash"] = new(135, 100, 30, 12, 62, 78),
        ["n_thunder"] = new(251, 135, 29, 15, 124, 103),
        ["o_arcane"] = new(78, 101, 29, 12, 39, 93),
        ["o_flame"] = new(74, 96, 29, 12, 37, 86),
        ["o_leaf"] = new(97, 90, 29, 12, 48, 83),
        ["s_flame"] = new(177, 93, 24, 15, 30, 58),
        ["s_frost"] = new(166, 52, 18, 15, 12, 25),
        ["s_jet"] = new(182, 58, 15, 15, 14, 18),
        ["sl_double"] = new(85, 105, 16, 20, 2, 89),
        ["sl_hammer"] = new(161, 100, 24, 12, 58, 80),
        ["sl_punch"] = new(117, 107, 17, 20, 46, 106),
        ["sl_scythe"] = new(122, 63, 25, 12, 54, 58),
        ["sl_sword"] = new(121, 79, 15, 20, 44, 70),
        ["sl_thrust"] = new(137, 64, 15, 20, 4, 52),
        ["sl_whip"] = new(153, 60, 17, 20, -3, 52),
        ["sl_flurry"] = new(103, 85, 19, 20, 15, 69),
        ["sl_spin"] = new(186, 88, 16, 20, 82, 68),
        ["sl_cleave"] = new(157, 102, 23, 12, 45, 82),
        ["h_spark"] = new(113, 58, 15, 30, 25, 45),
        ["h_burst"] = new(200, 184, 34, 20, 102, 92),
        ["h_blunt"] = new(78, 74, 18, 20, 36, 58),
        ["h_parry"] = new(164, 90, 20, 20, 86, 75),
        ["c_muzzle"] = new(77, 64, 27, 15, 30, 58),
        ["a_heal"] = new(224, 107, 29, 10, 112, 75),
        ["a_shield"] = new(71, 72, 29, 12, 35, 59),
        ["a_field"] = new(156, 89, 29, 12, 50, 74),
        ["a_hex"] = new(136, 128, 42, 10, 68, 112),
        ["a_spore"] = new(103, 106, 32, 10, 49, 92),
        ["st_rage"] = new(53, 58, 14, 12, 26, 54),
        ["st_haste"] = new(71, 42, 14, 12, 52, 39),
        ["st_sleep"] = new(43, 55, 24, 10, 14, 69),
        ["st_confused"] = new(48, 37, 24, 10, 24, 68),
        ["st_stunned"] = new(51, 33, 20, 10, 26, 56),
        ["st_charmed"] = new(42, 60, 24, 10, 21, 67),
        ["st_burning"] = new(38, 63, 14, 12, 19, 60),
        ["st_bloom"] = new(97, 103, 33, 10, 49, 93),
    };
}

// 배틀 화면에 겹쳐 그리는 효과 하나. StartMs는 배틀 시계(BattleClockMs) 기준입니다.
// Image: 타입 색으로 그린 시트 주소 (없으면 pfx/이름.png). Vx·Vy: 움직이는 효과의 속도(px/ms)
// LifeMs: 0이면 한 번 재생, 아니면 그 시간 동안 반복 재생. Cut: 앞에서부터 이 비율의 프레임만 재생
public sealed record BattlePfx(string Name, double X, double Y, double Scale, bool Flip, double AngleDeg, double Speed, double StartMs,
    string? Image = null, double Vx = 0, double Vy = 0, double LifeMs = 0, double Cut = 1)
{
    public PfxSheet Sheet => PfxSheets.All[Name];

    private int PlayedFrames => Math.Max(1, (int)Math.Ceiling(Sheet.Frames * Cut));

    // 한 번 재생에 걸리는 시간 (반복이면 LifeMs)
    public double LengthMs => LifeMs > 0 ? LifeMs : PlayedFrames * 1000.0 / Sheet.Fps / Speed;

    // 지금 보여 줄 프레임. 아직 시작 전이거나 끝났으면 -1
    public int FrameAt(double clockMs)
    {
        double elapsed = clockMs - StartMs;
        if (elapsed < 0 || elapsed >= LengthMs) return -1;
        int frame = (int)(elapsed * Speed / 1000 * Sheet.Fps);
        return LifeMs > 0 ? frame % PlayedFrames : Math.Min(frame, PlayedFrames - 1);
    }

    public bool IsDone(double clockMs) => clockMs - StartMs >= LengthMs;

    // 움직이는 효과의 지금 위치
    public (double X, double Y) PositionAt(double clockMs)
    {
        double t = Math.Max(0, clockMs - StartMs);
        return (X + Vx * t, Y + Vy * t);
    }
}
