using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.Demo;

/// <summary>Los tres astronautas ficticios del modo demostración (<c>CrewApi:Mode = Demo</c>).</summary>
public static class DemoAccounts
{
    public const string Password = "demo123";

    public static IReadOnlyList<SessionUser> All { get; } =
    [
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000001"), "ana", "Ana Rivera"),
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000002"), "luis", "Luis Ortega"),
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000003"), "mara", "Mara Sokolova"),
    ];
}
