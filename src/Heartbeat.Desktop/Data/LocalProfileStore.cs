using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Data;

/// <summary>Perfil en <c>LocalAccount.ProfileJson</c> (ver <see cref="ProfileCache"/>); funciona sin conexión.</summary>
public sealed class LocalProfileStore : IProfileStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _uiContext;

    /// <param name="uiContext">Contexto al que se entrega <see cref="Changed"/>; por defecto, el del hilo que crea el almacén.</param>
    public LocalProfileStore(IServiceScopeFactory scopes, TimeProvider clock, SynchronizationContext? uiContext = null)
    {
        _scopes = scopes;
        _clock = clock;
        _uiContext = uiContext ?? SynchronizationContext.Current;
    }

    public event EventHandler? Changed;

    public int PendingCount
    {
        get
        {
            using IServiceScope scope = _scopes.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            return db.LocalAccounts.Select(a => a.ProfileJson).ToList().Count(json => ProfileCache.Read(json)?.PendingEdit is not null);
        }
    }

    public ProfileView? Get(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        CachedProfile? cached = ProfileCache.Read(db.LocalAccounts.Where(a => a.AstronautId == astronautId).Select(a => a.ProfileJson).FirstOrDefault());
        return cached is null ? null : new ProfileView(cached.Effective, cached.PendingEdit is not null);
    }

    public ProfileView Save(Guid astronautId, string displayName, string? callSign, string? notes)
    {
        string name = (displayName ?? string.Empty).Trim();
        string? call = Clean(callSign);
        string? note = Clean(notes);

        if (name.Length == 0)
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(displayName));
        }

        if (name.Length > ProfileLimits.DisplayName)
        {
            throw new ArgumentException($"El nombre no puede superar {ProfileLimits.DisplayName} caracteres.", nameof(displayName));
        }

        if (call?.Length > ProfileLimits.CallSign)
        {
            throw new ArgumentException($"El indicativo no puede superar {ProfileLimits.CallSign} caracteres.", nameof(callSign));
        }

        if (note?.Length > ProfileLimits.Notes)
        {
            throw new ArgumentException($"Las notas no pueden superar {ProfileLimits.Notes} caracteres.", nameof(notes));
        }

        ProfileView result;
        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            LocalAccount account = db.LocalAccounts.FirstOrDefault(a => a.AstronautId == astronautId)
                ?? throw new InvalidOperationException("No hay una cuenta local para este astronauta.");
            CachedProfile cached = ProfileCache.Read(account.ProfileJson)
                ?? throw new InvalidOperationException("La cuenta local no tiene un perfil guardado.");

            // «Gana la edición más reciente»: la marca nunca retrocede aunque el reloj del equipo vaya atrasado.
            DateTimeOffset now = _clock.GetUtcNow();
            DateTimeOffset previous = cached.Effective.UpdatedAt;
            DateTimeOffset updatedAt = now > previous ? now : previous.AddMilliseconds(1);

            CachedProfile updated = cached with { PendingEdit = new UpdateProfileRequest(name, call, note, updatedAt) };
            account.ProfileJson = ProfileCache.Write(updated);
            db.SaveChanges();
            result = new ProfileView(updated.Effective, true);
        }

        RaiseChanged();
        return result;
    }

    public UpdateProfileRequest? GetPendingEdit(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return ProfileCache.Read(db.LocalAccounts.Where(a => a.AstronautId == astronautId).Select(a => a.ProfileJson).FirstOrDefault())?.PendingEdit;
    }

    public void MarkSynced(Guid astronautId, AstronautProfile serverProfile)
    {
        ArgumentNullException.ThrowIfNull(serverProfile);

        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            LocalAccount? account = db.LocalAccounts.FirstOrDefault(a => a.AstronautId == astronautId);
            if (account is null)
            {
                return;
            }

            // El servidor puede haber aceptado la edición o tener otra más nueva; en ambos casos su perfil es el de referencia.
            account.ProfileJson = ProfileCache.Write(ProfileCache.MergeServerProfile(account.ProfileJson, serverProfile));
            db.SaveChanges();
        }

        RaiseChanged();
    }

    public int MarkAllSynced()
    {
        int synced = 0;
        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            foreach (LocalAccount account in db.LocalAccounts.ToList())
            {
                CachedProfile? cached = ProfileCache.Read(account.ProfileJson);
                if (cached?.PendingEdit is null)
                {
                    continue;
                }

                // El "servidor" simulado acepta la edición tal cual.
                account.ProfileJson = ProfileCache.Write(new CachedProfile(cached.Effective, null));
                synced++;
            }

            if (synced > 0)
            {
                db.SaveChanges();
            }
        }

        if (synced > 0)
        {
            RaiseChanged();
        }

        return synced;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void RaiseChanged()
    {
        if (_uiContext is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _uiContext.Post(static state => ((LocalProfileStore)state!).Changed?.Invoke(state, EventArgs.Empty), this);
        }
    }
}
