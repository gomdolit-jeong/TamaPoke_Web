using System.Buffers.Binary;
using System.Xml.Linq;

namespace TamaPoke.Web.Sprites;

// 모션 하나의 스프라이트 시트. 가로는 프레임, 세로는 방향입니다.
public sealed class SpriteStrip
{
    public required byte[] Png { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required int SheetWidth { get; init; }
    public required int SheetHeight { get; init; }

    // 프레임별 재생 시간 (단위: 1/60초, AnimData.xml의 Durations)
    public required int[] Durations { get; init; }

    public int Columns => SheetWidth / FrameWidth;
    public int Rows => SheetHeight / FrameHeight;
}

// PMDCollab/SpriteCollab에서 필요한 모션만 받아 옵니다.
// 커밋을 고정해서 원본이 바뀌어도 게임이 영향을 받지 않습니다. 다른 곳으로 옮길 때는 BaseUrl만 바꾸면 됩니다.
// jsDelivr는 저장소가 50 MB를 넘어서 일부 파일을 403으로 거부하므로 쓰지 않습니다.
public sealed class SpriteLoader(HttpClient http)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/PMDCollab/SpriteCollab/53074f14d6947daf17af7e21db709497169a0e76/sprite";

    // 처음 보여주기 전에 미리 받아 두는 자주 쓰는 모션
    private static readonly int[] CommonActions = [PokemonAnim.IDLE, PokemonAnim.WALK, PokemonAnim.EAT, PokemonAnim.HOP, PokemonAnim.SLEEP];

    private readonly record struct AnimInfo(string FileName, int FrameWidth, int FrameHeight, int[] Durations);

    private readonly Dictionary<string, Task<Dictionary<int, AnimInfo>>> _animData = [];
    private readonly Dictionary<(string Path, string FileName), Task<byte[]>> _pngs = [];

    // 요청한 모션이 없는 스프라이트는 대체 모션 → IDLE → 첫 번째 유효 모션 순으로 대체합니다.
    public async Task<SpriteStrip> GetStripAsync(string path, int action, bool continuous = false)
    {
        var anims = await GetAnimDataAsync(path);
        if (!anims.TryGetValue(PokemonAnim.Resolve(anims.ContainsKey, action, continuous), out var anim))
            anim = anims.OrderBy(a => a.Key).First().Value;

        byte[] png = await GetPngAsync(path, anim.FileName);
        var (sheetWidth, sheetHeight) = ReadPngSize(png);
        return new SpriteStrip
        {
            Png = png,
            FrameWidth = anim.FrameWidth,
            FrameHeight = anim.FrameHeight,
            SheetWidth = sheetWidth,
            SheetHeight = sheetHeight,
            Durations = anim.Durations,
        };
    }

    // 대기 모션 한 칸의 크기 (배틀 화면은 이 크기를 기준으로 배율을 정합니다)
    public async Task<(int Width, int Height)> GetIdleFrameSizeAsync(string path)
    {
        var anims = await GetAnimDataAsync(path);
        if (!anims.TryGetValue(PokemonAnim.IDLE, out var idle)) idle = anims.OrderBy(a => a.Key).First().Value;
        return (idle.FrameWidth, idle.FrameHeight);
    }

    public async Task PreloadAsync(string path)
    {
        try
        {
            await Task.WhenAll(CommonActions.Select(action => GetStripAsync(path, action)));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // 미리 받기는 실패해도 괜찮습니다. 실제로 필요할 때 다시 받습니다.
            Console.WriteLine($"스프라이트 미리 받기 실패 {path}: {ex.Message}");
        }
    }

    private Task<Dictionary<int, AnimInfo>> GetAnimDataAsync(string path) =>
        GetOrAdd(_animData, path, () => LoadAnimDataAsync(path));

    private Task<byte[]> GetPngAsync(string path, string fileName) =>
        GetOrAdd(_pngs, (path, fileName), () => http.GetByteArrayAsync($"{BaseUrl}/{path}/{fileName}"));

    private async Task<Dictionary<int, AnimInfo>> LoadAnimDataAsync(string path)
    {
        var doc = XDocument.Parse(await http.GetStringAsync($"{BaseUrl}/{path}/AnimData.xml"));
        var anims = doc.Descendants("Anim").ToList();
        var result = new Dictionary<int, AnimInfo>();

        foreach (var anim in anims)
        {
            if (!int.TryParse(anim.Element("Index")?.Value, out int index)) continue;

            // CopyOf는 다른 모션의 그림과 재생 시간을 그대로 씁니다.
            string name = anim.Element("CopyOf")?.Value ?? anim.Element("Name")?.Value ?? "";
            var source = anims.FirstOrDefault(a => a.Element("Name")?.Value == name);
            if (source is null) continue;

            int frameWidth = (int?)source.Element("FrameWidth") ?? 0;
            int frameHeight = (int?)source.Element("FrameHeight") ?? 0;
            if (frameWidth <= 0 || frameHeight <= 0) continue;

            int[] durations = source.Element("Durations")?.Elements("Duration")
                .Select(d => (int)d).Where(ticks => ticks > 0).ToArray() ?? [];
            result[index] = new AnimInfo($"{name}-Anim.png", frameWidth, frameHeight, durations);
        }

        if (result.Count == 0) throw new InvalidOperationException($"AnimData.xml에 모션이 없습니다: {path}");
        return result;
    }

    // 같은 파일을 두 번 받지 않습니다. 실패한 요청은 다음에 다시 시도하도록 지웁니다.
    private static Task<T> GetOrAdd<TKey, T>(Dictionary<TKey, Task<T>> cache, TKey key, Func<Task<T>> load) where TKey : notnull
    {
        if (cache.TryGetValue(key, out var existing)) return existing;

        var task = load();
        cache[key] = task;
        _ = task.ContinueWith(_ => cache.Remove(key), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return task;
    }

    // PNG의 IHDR에서 크기만 읽습니다. (브라우저가 그릴 이미지를 C#에서 디코딩하지 않기 위해)
    private static (int Width, int Height) ReadPngSize(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
}
