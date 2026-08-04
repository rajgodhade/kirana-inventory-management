using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed <see cref="ISequenceGenerator"/>. Kirana is a single-user, single-process
/// desktop app, so a plain read-increment-save (no explicit transaction) is sufficient —
/// SQLite serializes writers at the file level, so there is no meaningful concurrent-writer
/// race to guard against here.
/// </summary>
public sealed class EfSequenceGenerator(IKiranaDbContext db) : ISequenceGenerator
{
    public async Task<string> NextAsync(string sequenceKey, string prefix, int padding, CancellationToken cancellationToken = default)
    {
        var value = await NextNumericAsync(sequenceKey, cancellationToken);
        return $"{prefix}-{value.ToString().PadLeft(padding, '0')}";
    }

    public async Task<long> NextNumericAsync(string sequenceKey, CancellationToken cancellationToken = default)
    {
        var counter = await db.SequenceCounters.FirstOrDefaultAsync(c => c.Key == sequenceKey, cancellationToken);
        if (counter is null)
        {
            counter = new SequenceCounter { Key = sequenceKey, NextValue = 1 };
            db.SequenceCounters.Add(counter);
        }

        var value = counter.NextValue;
        counter.NextValue++;

        await db.SaveChangesAsync(cancellationToken);

        return value;
    }
}
