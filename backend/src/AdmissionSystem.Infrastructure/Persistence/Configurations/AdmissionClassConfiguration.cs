using AdmissionSystem.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdmissionSystem.Infrastructure.Persistence.Configurations;

public sealed class AdmissionClassConfiguration : IEntityTypeConfiguration<AdmissionClass>
{
    public void Configure(EntityTypeBuilder<AdmissionClass> builder)
    {
        builder.ToTable("admission_classes", table =>
        {
            table.HasCheckConstraint("ck_admission_classes_total_seats_positive", "total_seats > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(AdmissionClass.MaxNameLength)
            .IsRequired();

        builder.Property(x => x.NameNormalized)
            .HasColumnName("name_normalized")
            .HasMaxLength(AdmissionClass.MaxNameLength)
            .IsRequired();

        builder.Property(x => x.TotalSeats)
            .HasColumnName("total_seats")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(x => x.NameNormalized)
            .HasDatabaseName("ix_admission_classes_name_normalized")
            .IsUnique();
    }
}

