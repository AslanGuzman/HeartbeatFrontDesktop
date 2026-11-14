using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.Tests;

public sealed class ShellUrlLauncherTests
{
    // Solo se prueban los rechazos: aceptar una URL abriría de verdad el navegador durante las pruebas.
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("ftp://example.com/archivo")]
    public void Refuses_anything_that_is_not_http_or_https(string url)
    {
        Assert.False(new ShellUrlLauncher().TryOpen(new Uri(url)));
    }

    [Fact]
    public void Refuses_a_relative_uri()
    {
        Assert.False(new ShellUrlLauncher().TryOpen(new Uri("docs/info.html", UriKind.Relative)));
    }
}
