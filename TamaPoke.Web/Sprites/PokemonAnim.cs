namespace TamaPoke.Web.Sprites;

// 스프라이트 .bin의 모션 순서입니다. (PC판 PokemonState.Animation.cs와 같은 번호)
public static class PokemonAnim
{
    public const int WALK = 0;
    public const int ATTACK = 1;
    public const int SHOOT = 3;
    public const int STRIKE = 2;
    public const int SHAKE = 4;
    public const int SLEEP = 5;
    public const int HURT = 6;
    public const int IDLE = 7;
    public const int SWING = 8;
    public const int DOUBLE = 9;
    public const int HOP = 10;
    public const int CHARGE = 11;
    public const int ROTATE = 12;
    public const int EVENTSLEEP = 13;
    public const int WAKE = 14;
    public const int EAT = 15;
    public const int TUMBLE = 16;
    public const int POSE = 17;
    public const int PULL = 18;
    public const int PAIN = 19;
    public const int FLOAT = 20;
    public const int DEEPBREATH = 21;
    public const int NOD = 22;
    public const int SINK = 25;
    public const int TRIP = 26;
    public const int LAYING = 27;
    public const int LEAPFORTH = 28;
    public const int HEAD = 29;
    public const int CRINGE = 30;
    public const int LOSTBALANCE = 31;
    public const int TUMBLEBACK = 32;
    public const int FAINT = 33;
    public const int HITGROUND = 34;

    // 스프라이트 시트의 방향 줄: 0 아래(정면), 6 왼쪽
    public const int ROW_FRONT = 0;
    public const int ROW_LEFT = 6;

    // 모션이 없는 스프라이트용 대체 모션. 0~12번은 대부분 있지만 13번 이후는 약 20%의 스프라이트에만 있습니다.
    // PC판과 같지만, 먹기는 대체하지 않고 대기로 둡니다. (PC판은 연속 공격으로 바꾸는데, 밥 먹는 동안 빠르게 연타해서 어지럽습니다)
    private static readonly Dictionary<int, int> Fallbacks = new()
    {
        [POSE] = HOP, [NOD] = HOP, [WAKE] = HOP, [DEEPBREATH] = CHARGE,
        [PAIN] = HURT, [CRINGE] = HURT, [SINK] = HURT, [TRIP] = HURT, [LOSTBALANCE] = HURT,
        [TUMBLEBACK] = HURT, [HITGROUND] = HURT, [FAINT] = HURT,
        [EVENTSLEEP] = SLEEP, [LAYING] = SLEEP,
        [LEAPFORTH] = ATTACK, [HEAD] = ATTACK, [STRIKE] = ATTACK, [SWING] = ATTACK, [DOUBLE] = ATTACK, [SHOOT] = ATTACK,
        [FLOAT] = ROTATE, [TUMBLE] = ROTATE, [SHAKE] = ROTATE, [PULL] = SWING,
    };

    // 응가, 배고픔처럼 계속 유지되는 상태의 모션: 전용 모션이 없으면 대체 모션을 끝없이 반복하지 않고 대기로 둡니다.
    private static readonly HashSet<int> ContinuousStateMotions = [PAIN, CRINGE, SINK, EVENTSLEEP];

    // 실제로 재생할 모션: 요청한 모션 → 대체 모션 → 대기 (대기도 없으면 부르는 쪽에서 첫 번째 모션을 씁니다)
    public static int Resolve(Func<int, bool> has, int action, bool continuous = false)
    {
        if (continuous && ContinuousStateMotions.Contains(action) && !has(action)) return IDLE;
        while (!has(action) && Fallbacks.TryGetValue(action, out int next)) action = next;
        return has(action) ? action : IDLE;
    }
}
