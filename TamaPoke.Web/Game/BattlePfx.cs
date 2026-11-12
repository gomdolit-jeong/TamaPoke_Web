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
    };
}

// 배틀 화면에 겹쳐 그리는 효과 하나. StartMs는 배틀 시계(BattleClockMs) 기준입니다.
public sealed record BattlePfx(string Name, double X, double Y, double Scale, bool Flip, double AngleDeg, double Speed, double StartMs)
{
    public PfxSheet Sheet => PfxSheets.All[Name];

    // 지금 보여 줄 프레임. 아직 시작 전이거나 끝났으면 -1
    public int FrameAt(double clockMs)
    {
        double t = (clockMs - StartMs) * Speed;
        if (t < 0) return -1;
        int frame = (int)(t / 1000 * Sheet.Fps);
        return frame < Sheet.Frames ? frame : -1;
    }

    public bool IsDone(double clockMs) => (clockMs - StartMs) * Speed >= Sheet.DurationMs;
}
