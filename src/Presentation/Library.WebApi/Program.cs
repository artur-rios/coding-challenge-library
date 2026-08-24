namespace Library.WebApi;

/// <summary>
///     Entry point. Everything it does lives in <see cref="Startup" />, so the composition is one
///     readable class rather than a hundred lines of top-level statements - and so the functional
///     tests can boot the same host through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var startup = new Startup(args);

        await startup.BuildAndRunAsync();
    }
}
