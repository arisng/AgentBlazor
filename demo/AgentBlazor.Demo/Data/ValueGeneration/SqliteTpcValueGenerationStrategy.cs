using System.Threading;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace AgentBlazor.Demo.Data.ValueGeneration;

/// <summary>
/// SQLite-specific client-side value generator for integer primary keys in TPC hierarchies.
/// SQLite does not support sequences or identity-seed/increment with the TPC strategy,
/// so we generate sequential integers client-side using a thread-safe counter.
/// </summary>
/// <remarks>
/// This is a Demo-project workaround — not part of the AgentBlazor NuGet package.
/// Consumer apps using SQL Server or PostgreSQL should use standard
/// <c>UseAutoincrement()</c> / HiLo / sequence strategies instead.
/// </remarks>
public sealed class SqliteTpcValueGenerationStrategy : ValueGenerator<int>
{
    private static long _counter;

    public override bool GeneratesTemporaryValues => false;

    public override int Next(EntityEntry entry)
        => (int)Interlocked.Increment(ref _counter);
}

/// <summary>
/// Factory for <see cref="SqliteTpcValueGenerationStrategy"/>.
/// </summary>
public sealed class SqliteTpcValueGeneratorFactory : ValueGeneratorFactory
{
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
        => new SqliteTpcValueGenerationStrategy();
}
