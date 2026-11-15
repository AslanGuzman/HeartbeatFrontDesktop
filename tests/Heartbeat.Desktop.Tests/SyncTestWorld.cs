using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Sync;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>El motor de sincronización real sobre una base SQLite en memoria y una Crew API guionizada.</summary>
internal sealed class SyncTestWorld : IDisposable
{
    public SyncTestWorld(string? user = "ana", bool online = true, int batchSize = 500, TimeSpan? interval = null, bool withTokens = true)
    {
        // demoSync: false → nada marca lo pendiente como enviado salvo el motor real.
        World = new TestWorld(user, online, demoSync: false);
        Api = new ScriptedCrewApiClient();
        if (withTokens)
        {
            Api.SetTokens("acceso", "refresco", "equipo-1");
            Api.TokenSets.Clear();
        }

        Ranges = new RangesSource(ReferenceRanges.Json, ReferenceRanges.Version);
        Engine = new SyncEngine(
            Api,
            World.Connectivity,
            World.Session,
            World.Services.GetRequiredService<IServiceScopeFactory>(),
            World.ProfileStore,
            World.Store,
            Ranges,
            new FixedDeviceId("equipo-1"),
            World.Clock,
            new SyncOptions(interval ?? TimeSpan.FromMinutes(10), batchSize));
    }

    public TestWorld World { get; }

    public ScriptedCrewApiClient Api { get; }

    public RangesSource Ranges { get; }

    public SyncEngine Engine { get; }

    public Guid Id(string user) => TestWorld.IdOf(user);

    /// <summary>Guarda un check-up con un pulso, a la hora indicada (un instante propio por check-up).</summary>
    public CheckUpRecord AddCheckUp(string user, DateTimeOffset at, decimal heartRate = 70, string? note = null)
    {
        DateTimeOffset previous = World.Clock.Now;
        World.Clock.Now = at;
        ReadingInput[] inputs = [new(IndicatorType.HeartRate, heartRate)];
        CheckUpRecord record = World.Store.AddCheckUp(Id(user), inputs, World.Evaluator.Evaluate(inputs), note);
        World.Clock.Now = previous;
        return record;
    }

    public List<LocalCheckUp> CheckUps() => World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().CheckUps.ToList();

    public List<LocalMessage> Messages() => World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().Messages.ToList();

    public SyncCursor? Cursor(string user) =>
        World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().SyncCursors.FirstOrDefault(c => c.AstronautId == Id(user));

    public void Dispose()
    {
        Engine.Dispose();
        World.Dispose();
    }
}
