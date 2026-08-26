namespace Kh2RandoMac.Core;

/// <summary>
/// Build identity for log files. Field logs are useless without knowing which build
/// wrote them; bump the tag with every build that goes to anyone else's machine.
/// </summary>
public static class AppInfo
{
    public const string Build = "0.2.1-beta.5";
}
