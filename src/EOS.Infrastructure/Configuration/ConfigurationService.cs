using EOS.Domain.Configuration;
using EOS.Domain.Settings;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Configuration;

/// <summary>Seeds default vocabularies and calendars (all editable afterwards) and builds engine calendars from the database.</summary>
public static class ConfigurationService
{
    public const string DefaultCalendarName = "Standard Mon-Sat 8h";

    /// <summary>Idempotent: adds only what is missing, never touches values a person has edited.</summary>
    public static void SeedDefaults(EosDbContext db)
    {
        foreach (var (kind, values) in Vocabularies.Seed)
        {
            var existing = db.Vocabulary.Where(v => v.Kind == kind).Select(v => v.Value).ToHashSet();
            var order = existing.Count;
            foreach (var value in values.Where(v => !existing.Contains(v)))
                db.Vocabulary.Add(new VocabularyItem { Kind = kind, Value = value, SortOrder = ++order });
        }

        if (!db.Calendars.Any())
        {
            db.Calendars.AddRange(
                new WorkCalendarDef { Name = DefaultCalendarName, HoursPerDay = 8m, WorkDays = "1,2,3,4,5,6", IsDefault = true },
                new WorkCalendarDef { Name = "Standard Mon-Fri 8h", HoursPerDay = 8m, WorkDays = "1,2,3,4,5" });
        }
        db.SaveChanges();
    }

    /// <summary>The engine calendar for a project: its own calendar, else the default, else one built from settings.</summary>
    public static async Task<WorkCalendar> LoadCalendarAsync(EosDbContext db, int? calendarId, CancellationToken ct = default)
    {
        var def = calendarId is { } id
            ? await db.Calendars.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            : null;
        def ??= await db.Calendars.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct);

        if (def is null)
        {
            var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var hours = resolver.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(), today);
            var days = resolver.Get<int[]>(SettingKeys.WorkDays, SettingsResolver.Chain(), today);
            return new WorkCalendar(hours, days.Select(d => (DayOfWeek)d));
        }

        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == def.Id).ToListAsync(ct);
        return new WorkCalendar(
            def.HoursPerDay,
            def.WorkDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => (DayOfWeek)int.Parse(d)),
            exceptions.Where(e => !e.IsWorking).Select(e => e.Date),
            exceptions.Where(e => e.IsWorking).Select(e => e.Date));
    }

    /// <summary>True when <paramref name="value"/> is an active member of the vocabulary (an empty vocabulary accepts anything).</summary>
    public static async Task<bool> IsValidAsync(EosDbContext db, string kind, string value, CancellationToken ct = default)
    {
        if (!await db.Vocabulary.AnyAsync(v => v.Kind == kind, ct)) return true;
        return await db.Vocabulary.AnyAsync(v => v.Kind == kind && v.Value == value && v.IsActive, ct);
    }
}
