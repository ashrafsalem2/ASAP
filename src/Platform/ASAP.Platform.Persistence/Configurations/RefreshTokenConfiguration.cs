using ASAP.Platform.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ASAP.Platform.Persistence.Configurations;

/// <summary>Maps <see cref="RefreshToken"/>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens");

        builder.Property(t => t.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(200);
        builder.Property(t => t.IssuedToIp).HasMaxLength(64);

        // Redemption looks a token up by its hash and by nothing else, so this index carries
        // every refresh.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revoking a whole session walks the chain by SessionId.
        builder.HasIndex(t => t.SessionId);

        builder.HasOne(t => t.User!)
               .WithMany()
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
