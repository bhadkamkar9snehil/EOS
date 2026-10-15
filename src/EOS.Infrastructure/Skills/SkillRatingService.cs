using EOS.Domain.Audit;
using EOS.Domain.Settings;
using EOS.Domain.Skills;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Skills;

public sealed class SkillRatingException(string message) : Exception(message);

public sealed class SkillRatingService(EosDbContext db)
{
    public async Task SetRatingAsync(
        int personId, int skillId, int rating, string actor, string? reason,
        SkillTransactionKind kind = SkillTransactionKind.ManualAdjustment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new SkillRatingException("A rating change needs an actor.");
        if (kind == SkillTransactionKind.EvidenceNote) throw new SkillRatingException("Use AddEvidenceNoteAsync for evidence notes.");
        if (kind == SkillTransactionKind.ManualAdjustment && string.IsNullOrWhiteSpace(reason))
            throw new SkillRatingException("A manual adjustment needs a reason.");

        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var levels = resolver.Get<int>(SettingKeys.SkillLevelCount, SettingsResolver.Chain(), today);
        if (rating < 1 || rating > levels)
            throw new SkillRatingException($"Rating must be between 1 and {levels} (setting {SettingKeys.SkillLevelCount}).");

        if (!await db.People.AnyAsync(p => p.Id == personId, ct)) throw new SkillRatingException($"Person {personId} not found.");
        if (!await db.Skills.AnyAsync(s => s.Id == skillId, ct)) throw new SkillRatingException($"Skill {skillId} not found.");

        var current = await db.PersonSkills.SingleOrDefaultAsync(x => x.PersonId == personId && x.SkillId == skillId, ct);
        if (current is not null && current.Rating == rating && kind != SkillTransactionKind.Assessment) return;

        var old = current?.Rating;
        if (current is null)
        {
            current = new PersonSkill { PersonId = personId, SkillId = skillId };
            db.PersonSkills.Add(current);
        }
        current.Rating = rating;
        current.AssessedOn = today;
        current.AssessedBy = actor;

        db.SkillTransactions.Add(new SkillTransaction
        {
            PersonId = personId, SkillId = skillId, Kind = kind, OldRating = old, NewRating = rating,
            Reason = reason, ChangedBy = actor, AtUtc = DateTime.UtcNow, Source = "manual",
        });
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = "skill.rate", SubjectKind = "person",
            SubjectId = personId.ToString(), Detail = $"skill {skillId}: {old?.ToString() ?? "-"} -> {rating}; {reason}",
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task AddEvidenceNoteAsync(int personId, int skillId, string note, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) throw new SkillRatingException("An evidence note cannot be empty.");
        var rating = await db.PersonSkills
            .Where(x => x.PersonId == personId && x.SkillId == skillId).Select(x => (int?)x.Rating).SingleOrDefaultAsync(ct);
        db.SkillTransactions.Add(new SkillTransaction
        {
            PersonId = personId, SkillId = skillId, Kind = SkillTransactionKind.EvidenceNote,
            OldRating = rating, NewRating = rating, Reason = note, ChangedBy = actor, AtUtc = DateTime.UtcNow, Source = "manual",
        });
        await db.SaveChangesAsync(ct);
    }
}
