using Microsoft.JSInterop;

namespace TamaPoke.Web.Game;

// 음표는 PC판 SoundManager.cs와 같습니다. (주파수 Hz, 길이 ms. 주파수 0은 쉼표)
public static class SoundEffects
{
    public static readonly int[][] Tap = [[880, 35]];
    public static readonly int[][] Eat = [[660, 45], [0, 12], [660, 45]];
    public static readonly int[][] Play = [[784, 45], [988, 60]];
    public static readonly int[][] Heart = [[1047, 55], [1319, 90]];
    public static readonly int[][] Medal = [[784, 70], [0, 25], [784, 70], [0, 25], [1047, 200]];
    public static readonly int[][] Deny = [[300, 110], [200, 170]];
}

// 브라우저에서 사각파를 합성해 재생합니다. (wwwroot/js/app.js)
public sealed class SoundPlayer(IJSRuntime js)
{
    public async void Play(int[][] notes)
    {
        try
        {
            await js.InvokeVoidAsync("tamaPoke.playNotes", (object)notes);
        }
        catch (JSException ex)
        {
            // 소리가 안 나도 게임은 계속됩니다.
            Console.WriteLine($"소리 재생 실패: {ex.Message}");
        }
    }
}
