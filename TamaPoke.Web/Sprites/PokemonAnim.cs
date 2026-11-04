namespace TamaPoke.Web.Sprites;

// 스프라이트 .bin의 모션 순서입니다. (PC판 PokemonState.Animation.cs와 같은 번호)
public static class PokemonAnim
{
    public const int WALK = 0;
    public const int IDLE = 7;
    public const int HOP = 10;

    // 스프라이트 시트의 방향 줄: 0 아래(정면), 6 왼쪽
    public const int ROW_FRONT = 0;
    public const int ROW_LEFT = 6;
}
