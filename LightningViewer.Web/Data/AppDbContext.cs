using Microsoft.EntityFrameworkCore;
using LightningViewer.Web.Models.Domain;

namespace LightningViewer.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UnidadeTomadora> UnidadesTomadoras { get; set; } = null!;
    public DbSet<LightningFlash> LightningFlashes { get; set; } = null!;
    public DbSet<ProcessedFile> ProcessedFiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnidadeTomadora>(entity =>
        {
            entity.ToTable("unidades_tomadoras");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.Numero).HasColumnName("numero");
            entity.Property(e => e.Municipio).HasColumnName("municipio").HasMaxLength(100);
            entity.Property(e => e.Nome).HasColumnName("nome").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.Cnpj).HasColumnName("cnpj").HasMaxLength(20);
            entity.Property(e => e.Endereco).HasColumnName("endereco");
        });

        modelBuilder.Entity<LightningFlash>(entity =>
        {
            entity.ToTable("lightning_flashes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.FlashTime)
                .HasColumnName("flash_time")
                .HasColumnType("timestamptz");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.FlashCount).HasColumnName("flash_count");
            entity.Property(e => e.ProductFile).HasColumnName("product_file").HasMaxLength(300);
            entity.Property(e => e.IngestedAt)
                .HasColumnName("ingested_at")
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("NOW()");

            // Performance indexes
            entity.HasIndex(e => e.FlashTime).HasDatabaseName("idx_flash_time");
            entity.HasIndex(e => new { e.Latitude, e.Longitude }).HasDatabaseName("idx_flash_latlon");
        });

        modelBuilder.Entity<ProcessedFile>(entity =>
        {
            entity.ToTable("processed_files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.FileName)
                .HasColumnName("file_name")
                .HasMaxLength(300)
                .IsRequired();
            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.FileName).IsUnique().HasDatabaseName("idx_processed_file_name");
        });
    }
}
