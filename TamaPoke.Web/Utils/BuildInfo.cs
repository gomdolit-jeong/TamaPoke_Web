using System.Reflection;

namespace TamaPoke.Web.Utils;

// GitHub Pages는 파일을 10분까지 캐시합니다. 배포 직후 새 코드가 이전 CSS나 번역을 읽지 않도록
// 지문이 붙지 않는 파일의 주소에 빌드 번호(커밋 포함)를 붙입니다.
public static class BuildInfo
{
    private static readonly string Version = Uri.EscapeDataString(
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev");

    public static string Versioned(string path) => $"{path}?v={Version}";
}
