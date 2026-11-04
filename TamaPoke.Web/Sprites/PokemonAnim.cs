namespace TamaPoke.Web.Sprites;

// 스프라이트 .bin의 모션 순서입니다. (PC판 PokemonState.Animation.cs와 같은 번호)
public static class PokemonAnim
{
    public const int WALK = 0;
    public const int SHAKE = 4;
    public const int SLEEP = 5;
    public const int HURT = 6;
    public const int IDLE = 7;
    public const int HOP = 10;
    public const int EVENTSLEEP = 13;
    public const int WAKE = 14;
    public const int EAT = 15;
    public const int POSE = 17;
    public const int PAIN = 19;
    public const int DEEPBREATH = 21;
    public const int NOD = 22;
    public const int SINK = 25;
    public const int CRINGE = 30;

    // 스프라이트 시트의 방향 줄: 0 아래(정면), 6 왼쪽
    public const int ROW_FRONT = 0;
    public const int ROW_LEFT = 6;
}
