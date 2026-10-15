using EOS.Domain.Audit;
using EOS.Domain.Configuration;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Configuration;

public sealed class AdminException(string message) : Exception(message);

public sealed class AdminService(EosDbContext db)
{

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<VocabularyItem>>> VocabulariesAsync(CancellationToken ct = default) =>
        (await db.Vocabulary.AsNoTracking().OrderBy(v => v.Kind).ThenBy(v => v.SortOrder).ToListAsync(ct))
        .GroupBy(v => v.Kind).ToDictionary(g => g.Key, g => (IReadOnlyList<VocabularyItem>)g.ToList());

    public async Task<VocabularyItem> AddValueAsync(string kind, string value, string? color, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(value)) throw new AdminException("A vocabulary value needs a kind and a value.");
        value = value.Trim();
        var existing = await db.Vocabulary.SingleOrDefaultAsync(v => v.Kind == kind && v.Value == value, ct);
        if (existing is not null) { existing.IsActive = true; existing.Color = color ?? existing.Color; await db.SaveChangesAsync(ct); return existing; }
        var order = (await db.Vocabulary.Where(v => v.Kind == kind).Select(v => (int?)v.SortOrder).MaxAsync(ct) ?? 0) + 1;
        var item = new VocabularyItem { Kind = kind, Value = value, SortOrder = order, Color = color };
        db.Vocabulary.Add(item);
        Audit("vocabulary.add", actor, $"{kind}={value}");
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task SetActiveAsync(int id, bool active, string actor, CancellationToken ct = default)
    {
        var v = await db.Vocabulary.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminException($"Vocabulary value {id} not found.");
        v.IsActive = active;
        Audit(active ? "vocabulary.enable" : "vocabulary.disable", actor, $"{v.Kind}={v.Value}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(WorkCalendarDef Calendar, IReadOnlyList<CalendarException> Exceptions)>> CalendarsAsync(CancellationToken ct = default)
    {
        var cals = await db.Calendars.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var ex = await db.CalendarExceptions.AsNoTracking().OrderBy(e => e.Date).ToListAsync(ct);
        return cals.Select(c => (c, (IReadOnlyList<CalendarException>)ex.Where(e => e.CalendarId == c.Id).ToList())).ToList();
    }

    public async Task<WorkCalendarDef> SaveCalendarAsync(WorkCalendarDef cal, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cal.Name)) throw new AdminException("A calendar needs a name.");
        if (cal.HoursPerDay <= 0 || cal.HoursPerDay > 24) throw new AdminException("Hours per day must be between 0 and 24.");
        var days = cal.WorkDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (days.Length == 0 || days.Any(d => !int.TryParse(d, out var n) || n is < 0 or > 6)) throw new AdminException("Work days must be weekday numbers 0 (Sunday) to 6, comma separated.");
        cal.WorkDays = string.Join(',', days.Select(int.Parse).Distinct().OrderBy(x => x));

        var current = cal.Id == 0 ? null : await db.Calendars.SingleOrDefaultAsync(c => c.Id == cal.Id, ct) ?? throw new AdminException($"Calendar {cal.Id} not found.");
        if (current is null) db.Calendars.Add(cal); else db.Entry(current).CurrentValues.SetValues(cal);
        if (cal.IsDefault)
            foreach (var other in await db.Calendars.Where(c => c.IsDefault && c.Id != cal.Id).ToListAsync(ct)) other.IsDefault = false;
        Audit("calendar.save", actor, cal.Name);
        await db.SaveChangesAsync(ct);
        return current ?? cal;
    }

    public async Task SetHolidayAsync(int calendarId, DateOnly date, bool isWorking, string? note, string actor, CancellationToken ct = default)
    {
        if (!await db.Calendars.AnyAsync(c => c.Id == calendarId, ct)) throw new AdminException($"Calendar {calendarId} not found.");
        var e = await db.CalendarExceptions.SingleOrDefaultAsync(x => x.CalendarId == calendarId && x.Date == date, ct);
        if (e is null) db.CalendarExceptions.Add(new CalendarException { CalendarId = calendarId, Date = date, IsWorking = isWorking, Note = note });
        else { e.IsWorking = isWorking; e.Note = note; }
        Audit("calendar.exception", actor, $"{calendarId} {date:yyyy-MM-dd} {(isWorking ? "working" : "holiday")}");
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveHolidayAsync(int exceptionId, string actor, CancellationToken ct = default)
    {
        var e = await db.CalendarExceptions.SingleOrDefaultAsync(x => x.Id == exceptionId, ct) ?? throw new AdminException($"Exception {exceptionId} not found.");
        db.CalendarExceptions.Remove(e);
        Audit("calendar.exception.remove", actor, exceptionId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CustomFieldDefinition>> FieldsAsync(string? entityKind = null, CancellationToken ct = default) =>
        await db.CustomFieldDefinitions.AsNoTracking().Where(f => entityKind == null || f.EntityKind == entityKind).OrderBy(f => f.EntityKind).ThenBy(f => f.SortOrder).ToListAsync(ct);

    public async Task<CustomFieldDefinition> AddFieldAsync(CustomFieldDefinition f, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(f.EntityKind) || string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Label)) throw new AdminException("A custom field needs an entity kind, a key and a label.");
        if (await db.CustomFieldDefinitions.AnyAsync(x => x.EntityKind == f.EntityKind && x.Key == f.Key, ct)) throw new AdminException($"Field {f.EntityKind}.{f.Key} already exists.");
        if (f.Type == CustomFieldType.Choice && string.IsNullOrWhiteSpace(f.Options)) throw new AdminException("A choice field needs options separated by '|'.");
        db.CustomFieldDefinitions.Add(f);
        Audit("customfield.add", actor, $"{f.EntityKind}.{f.Key}");
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task SetFieldValueAsync(string entityKind, int entityId, string key, string? value, string actor, CancellationToken ct = default)
    {
        var def = await db.CustomFieldDefinitions.SingleOrDefaultAsync(f => f.EntityKind == entityKind && f.Key == key, ct) ?? throw new AdminException($"No custom field {entityKind}.{key}.");
        if (!string.IsNullOrEmpty(value))
        {
            switch (def.Type)
            {
                case CustomFieldType.Number when !decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _): throw new AdminException($"'{value}' is not a number.");
                case CustomFieldType.Date when !DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _): throw new AdminException($"'{value}' is not a date (yyyy-mm-dd).");
                case CustomFieldType.Choice when !(def.Options ?? "").Split('|').Contains(value): throw new AdminException($"'{value}' is not one of: {def.Options}.");
            }
        }
        var row = await db.CustomFieldValues.SingleOrDefaultAsync(v => v.EntityKind == entityKind && v.EntityId == entityId && v.Key == key, ct);
        if (string.IsNullOrEmpty(value)) { if (row is not null) db.CustomFieldValues.Remove(row); }
        else if (row is null) db.CustomFieldValues.Add(new CustomFieldValue { EntityKind = entityKind, EntityId = entityId, Key = key, Value = value });
        else row.Value = value;
        Audit("customfield.set", actor, $"{entityKind}#{entityId}.{key}", value);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> FieldValuesAsync(string entityKind, int entityId, CancellationToken ct = default) =>
        await db.CustomFieldValues.AsNoTracking().Where(v => v.EntityKind == entityKind && v.EntityId == entityId).ToDictionaryAsync(v => v.Key, v => v.Value, ct);

    private void Audit(string action, string actor, string subject, string? detail = null) =>
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = action, SubjectKind = "config", SubjectId = subject, Detail = detail });
}
