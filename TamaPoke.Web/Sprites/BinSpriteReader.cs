namespace TamaPoke.Web.Sprites;

// 모션 하나의 스프라이트 시트. 가로는 프레임, 세로는 방향입니다.
public sealed class SpriteStrip
{
    public required byte[] Png { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required int SheetWidth { get; init; }
    public required int SheetHeight { get; init; }

    // 프레임별 재생 시간 (단위: 1/60초, AnimData.xml의 Durations). 구버전 bin은 빈 배열입니다.
    public required int[] Durations { get; init; }

    public int Columns => SheetWidth / FrameWidth;
    public int Rows => SheetHeight / FrameHeight;
}

// PC판의 스프라이트 .bin 파일을 읽습니다. 데이터가 없는 모션은 null로 채웁니다.
public static class BinSpriteReader
{
    // v2 bin 파일은 맨 앞에 이 값을 기록합니다. (구버전은 첫 값이 양수인 스트립 수)
    private const int FORMAT_V2_MARKER = -2;

    public static List<SpriteStrip?> Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));

        int first = reader.ReadInt32();
        bool isV2 = first == FORMAT_V2_MARKER;
        int totalStrips = isV2 ? reader.ReadInt32() : first;

        var strips = new List<SpriteStrip?>(Math.Max(totalStrips, 0));
        for (int i = 0; i < totalStrips; i++)
        {
            int frameWidth = reader.ReadInt32();
            int frameHeight = reader.ReadInt32();

            int[] durations = [];
            if (isV2)
            {
                durations = new int[reader.ReadInt32()];
                for (int d = 0; d < durations.Length; d++) durations[d] = reader.ReadInt32();
            }

            byte[] png = reader.ReadBytes(reader.ReadInt32());
            if (png.Length == 0 || frameWidth <= 0 || frameHeight <= 0)
            {
                strips.Add(null);
                continue;
            }

            var (sheetWidth, sheetHeight) = ReadPngSize(png);
            strips.Add(new SpriteStrip
            {
                Png = png,
                FrameWidth = frameWidth,
                FrameHeight = frameHeight,
                SheetWidth = sheetWidth,
                SheetHeight = sheetHeight,
                Durations = durations,
            });
        }
        return strips;
    }

    // PNG의 IHDR에서 크기만 읽습니다. (브라우저가 그릴 이미지를 C#에서 디코딩하지 않기 위해)
    private static (int Width, int Height) ReadPngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
}
