using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace AgentBlazor.Demo.Data.ValueGeneration;

/// <summary>
/// SQLite-specific client-side value generator for Guid primary keys in TPC hierarchies.
/// SQLite does not support sequences or identity-seed/increment with the TPC strategy,
/// so we generate client-side Guid values.
/// </summary>
/// <remarks>
/// This is a Demo-project workaround — not part of the AgentBlazor NuGet package.
/// Consumer apps using SQL Server or PostgreSQL should use standard
/// <c>ValueGeneratedOnAdd()</c> / HiLo / sequence strategies instead.
/// </remarks>
public sealed class SqliteTpcGuidValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.NewGuid();
}

/// <summary>
/// Factory for <see cref="SqliteTpcGuidValueGenerator"/>.
/// </summary>
public sealed class SqliteTpcGuidValueGeneratorFactory : ValueGeneratorFactory
{
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
    {
        if (property.ClrType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"SqliteTpcGuidValueGeneratorFactory requires a Guid property, but '{property.Name}' is '{property.ClrType.Name}'.");
        }

        return new SqliteTpcGuidValueGenerator();
    }
}
