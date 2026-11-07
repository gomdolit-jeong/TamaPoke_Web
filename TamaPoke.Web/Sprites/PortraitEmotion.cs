using TamaPoke.Web.Game;

namespace TamaPoke.Web.Sprites;

// SpriteCollab 초상화(40x40 얼굴 그림)의 표정 이름. 파일 이름과 같습니다. (portrait/번호/표정.png)
public static class PortraitEmotion
{
    public const string Normal = "Normal";
    public const string Happy = "Happy";
    public const string Joyous = "Joyous";
    public const string Inspired = "Inspired";
    public const string Determined = "Determined";
    public const string Surprised = "Surprised";
    public const string Sad = "Sad";
    public const string Crying = "Crying";
    public const string Worried = "Worried";
    public const string Pain = "Pain";
    public const string Sigh = "Sigh";
    public const string Stunned = "Stunned";

    // 받아 올 주소. 스프라이트와 같은 커밋에 고정합니다.
    public static string Url(string path, string emotion) => $"{SpriteLoader.RepoUrl}/portrait/{path}/{emotion}.png";

    // 말풍선 옆 얼굴: 지금 기분(PokemonState.MoodKey)에 맞는 표정입니다.
    public static string For(PokemonState pet)
    {
        if (pet.EventMessage is not null) return Happy;
        if (pet.IsEvolving) return pet.HasEvolved ? Joyous : Surprised;

        return pet.MoodKey switch
        {
            "mood.farewell" or "mood.release" => Sad,
            "mood.runaway" => Crying,
            "mood.battle" or "web.mood.arena" or "web.mood.gym" => Determined,
            "mood.game.ball" or "mood.game.catch" or "mood.game.memo" or "mood.game.clean" => Joyous,
            "mood.lonely" => Sad,
            "mood.wantsToTalk" => Inspired,
            "mood.poop" => Stunned,
            "mood.dirty" => Sigh,
            "mood.veryBad" => Pain,
            "mood.tired" => Worried,
            "mood.veryHappy" => Joyous,
            "mood.good" => Happy,
            _ => Normal,
        };
    }
}
