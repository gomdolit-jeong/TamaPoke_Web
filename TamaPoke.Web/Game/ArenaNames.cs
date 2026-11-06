using System.Text.RegularExpressions;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public enum NicknameProblem { None, Length, Characters, Blocked }

// 대전 닉네임: 로그인한 사람이 영어 글자와 숫자로 정합니다. 기본적인 욕설과 운영자 사칭은 막습니다.
// 글자 규칙은 Firestore 규칙에서도 다시 확인합니다. (욕설 목록은 앱에서만 확인)
public static partial class ArenaNames
{
    public const int MIN_LENGTH = 3;
    public const int MAX_LENGTH = 12;

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex AllowedPattern();

    // 이 말이 이름 어디에든 들어 있으면 막습니다. (다른 단어 속에 우연히 들어가기 어려운 것만)
    private static readonly string[] BlockedParts =
    [
        "fuck", "fuk", "fck", "fack", "phuk", "shit", "bitch", "biatch", "cunt", "asshole", "bastard", "whore", "slut",
        "nigg", "niga", "faggot", "retard", "pussy", "penis", "vagina", "porn", "dildo", "boob", "nazi", "hitler",
        "motherf", "jerkoff", "wank", "twat", "bollock", "kkk", "admin", "moderator", "official", "tamapoke",
    ];

    // 짧아서 다른 단어 속에도 흔한 말은 이름 전체가 이 말일 때만 막습니다. (예: class 속 ass)
    private static readonly HashSet<string> BlockedWords =
    [
        "ass", "arse", "cum", "sex", "sexy", "anal", "dick", "cock", "tit", "tits", "fag", "rape", "rapist",
        "piss", "crap", "damn", "hoe", "gay", "kill", "killer", "die", "suck", "sucks", "gm", "staff", "dev",
    ];

    public static string? Normalize(string? input) => input?.Trim();

    public static NicknameProblem Check(string? nickname)
    {
        if (nickname is null || nickname.Length < MIN_LENGTH || nickname.Length > MAX_LENGTH) return NicknameProblem.Length;
        if (!AllowedPattern().IsMatch(nickname)) return NicknameProblem.Characters;

        // 숫자로 바꿔 쓴 글자(f4ck, sh1t)도 같은 말로 봅니다.
        string plain = new(nickname.ToLowerInvariant().Select(c => c switch
        {
            '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '8' => 'b', '9' => 'g',
            _ => c,
        }).ToArray());
        string lettersOnly = new(plain.Where(char.IsLetter).ToArray());

        if (BlockedParts.Any(word => plain.Contains(word) || lettersOnly.Contains(word))) return NicknameProblem.Blocked;
        if (BlockedWords.Contains(plain) || BlockedWords.Contains(lettersOnly)) return NicknameProblem.Blocked;
        return NicknameProblem.None;
    }

    public static string ProblemKey(NicknameProblem problem) => problem switch
    {
        NicknameProblem.Length => "web.arena.nickname.length",
        NicknameProblem.Characters => "web.arena.nickname.characters",
        NicknameProblem.Blocked => "web.arena.nickname.blocked",
        _ => "",
    };

    // 닉네임이 없으면 "트레이너 #AB12"로 보여 줍니다.
    public static string Display(ArenaEntry entry) =>
        string.IsNullOrEmpty(entry.Nickname) ? L.F("web.arena.trainer", entry.Tag) : entry.Nickname;
}
