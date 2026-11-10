using AdmissionSystem.Domain.Classes;
using AdmissionSystem.Domain.Holds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdmissionSystem.Infrastructure.Persistence.Configurations;

public sealed class AdmissionHoldConfiguration : IEntityTypeConfiguration<AdmissionHold>
{
    public void Configure(EntityTypeBuilder<AdmissionHold> builder)
    {
        builder.ToTable("admission_holds");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.ClassId)
            .HasColumnName("class_id")
            .IsRequired();

        builder.Property(x => x.StudentId)
            .HasColumnName("student_id")
            .HasMaxLength(17)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.HasOne<AdmissionClass>()
            .WithMany()
            .HasForeignKey(x => x.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ClassId, x.StudentId })
            .HasDatabaseName("ux_admission_holds_active_student_per_class")
            .IsUnique()
            .HasFilter("status IN ('HELD', 'CONFIRMED')");

        builder.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_admission_holds_class_status_expires");

        builder.HasIndex(x => new { x.StudentId, x.CreatedAt })
            .HasDatabaseName("ix_admission_holds_student_created");

        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_admission_holds_status_expires");
    }
}

