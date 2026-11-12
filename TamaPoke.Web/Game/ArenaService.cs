using System.Text.Json;
using Microsoft.JSInterop;

namespace TamaPoke.Web.Game;

public sealed record ArenaBoard(string Season, ArenaEntry Me, int? MyRank, IReadOnlyList<ArenaEntry> Top);

// 대전 기록을 Firestore에서 읽고 씁니다. 내 문서만 쓸 수 있고, 점수 변화 폭은 Firestore 규칙이 다시 확인합니다.
public sealed class ArenaService(CloudSave cloud, PokemonDex dex, SkillDex skills)
{
    public const int TOP_COUNT = 100;

    public static string CurrentSeason => ArenaRules.SeasonKey(DateTime.UtcNow);

    // 순위표를 읽고, 내 대표 포켓몬을 지금 포켓몬으로 맞춰 둡니다. 실패하면 null입니다.
    public async Task<ArenaBoard?> LoadAsync(PokemonState pet)
    {
        if (cloud.UserId is not { } uid) return null;
        string season = CurrentSeason;
        try
        {
            string raw = await cloud.CallAsync<string>("arenaLoad", season, TOP_COUNT);
            var data = JsonSerializer.Deserialize(raw, ArenaJsonContext.Default.ArenaBoardData) ?? new ArenaBoardData();

            var record = data.Me is { } stored ? ArenaRules.Sanitize(stored, dex, skills) : null;
            var me = ArenaRules.Snapshot(pet, ArenaRules.TagFor(uid), record) with { Uid = uid };
            if (record is null || !ArenaRules.SameSnapshot(record, me))
            {
                if (!await SaveAsync(me, season)) return null;
            }

            // 순위표는 내 기록을 저장하기 전에 읽었으므로, 처음 등록했을 때도 내 자리가 보이도록 직접 넣습니다.
            var others = data.Top.Select(e => ArenaRules.Sanitize(e, dex, skills)).OfType<ArenaEntry>()
                .Where(e => e.Uid != uid).ToList();
            var top = others.Count < TOP_COUNT || me.Points >= others[^1].Points ? [.. others, me] : others;
            top = [.. top.OrderByDescending(e => e.Points).Take(TOP_COUNT)];
            int index = top.FindIndex(e => e.Uid == uid);
            return new ArenaBoard(season, me, index >= 0 ? index + 1 : null, top);
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"대전 정보 읽기 실패: {ex.Message}");
            return null;
        }
    }

    // 시작 화면용: 로그인하지 않아도 이번 시즌 상위 count명을 읽습니다. 실패하면 null입니다.
    public async Task<List<ArenaEntry>?> LoadTopAsync(int count)
    {
        try
        {
            string raw = await cloud.CallPublicAsync<string>("arenaTop", CurrentSeason, count);
            var data = JsonSerializer.Deserialize(raw, ArenaJsonContext.Default.ArenaBoardData) ?? new ArenaBoardData();
            return [.. data.Top.Select(e => ArenaRules.Sanitize(e, dex, skills)).OfType<ArenaEntry>().OrderByDescending(e => e.Points)];
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"대전 순위 읽기 실패: {ex.Message}");
            return null;
        }
    }

    // 닉네임을 바꿔 저장하고, 순위표 속 내 줄도 함께 바꿉니다. 저장하지 못하면 null입니다. (확인은 부르는 쪽에서)
    public async Task<ArenaBoard?> ChangeNicknameAsync(ArenaBoard board, string nickname)
    {
        var me = board.Me with { Nickname = nickname };
        if (!await SaveAsync(me, board.Season)) return null;
        return board with { Me = me, Top = [.. board.Top.Select(e => e.Uid == me.Uid ? me : e)] };
    }

    public async Task<bool> SaveAsync(ArenaEntry entry, string? season = null)
    {
        try
        {
            string json = JsonSerializer.Serialize(entry with { Uid = null }, ArenaJsonContext.Default.ArenaEntry);
            string error = await cloud.CallAsync<string>("arenaSave", season ?? CurrentSeason, json);
            if (error.Length == 0) return true;
            Console.WriteLine($"대전 기록 저장 실패: {error}");
            return false;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            Console.WriteLine($"대전 기록 저장 실패: {ex.Message}");
            return false;
        }
    }
}
