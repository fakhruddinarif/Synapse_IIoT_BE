using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Core.Entities;
using Core.Enums;
using DataType = Core.Enums.DataType;

namespace Infrastructure.Data
{
	public class AppDbContext : DbContext
	{
		private const string VARCHAR_50 = "varchar(50)";

		public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

		// List of tables in the database
		public DbSet<User> Users { get; set; }
		public DbSet<Device> Devices { get; set; }
		public DbSet<Tag> Tags { get; set; }
		public DbSet<MasterTable> MasterTables { get; set; }
		public DbSet<MasterTableFields> MasterTableFields { get; set; }
		public DbSet<StorageFlow> StorageFlows { get; set; }
		public DbSet<StorageFlowDevice> StorageFlowDevices { get; set; }
		public DbSet<StorageFlowMapping> StorageFlowMappings { get; set; }
		public DbSet<FileMetadata> FileMetadata { get; set; }
		public DbSet<AuditLog> AuditLogs { get; set; }

		// Fluent API configurations
		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			modelBuilder.Entity<User>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
				entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
				entity.Property(e => e.Role)
					.HasConversion<string>()
					.HasColumnType(VARCHAR_50)
					.HasDefaultValue(UserRole.VIEWER);
			});

			modelBuilder.Entity<Device>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Description).HasMaxLength(255);
				entity.Property(e => e.IsEnabled).HasDefaultValue(false);
				entity.Property(e => e.Protocol)
					.HasConversion<string>()
					.HasColumnType(VARCHAR_50)
					.HasDefaultValue(Protocol.HTTP);
				entity.Property(e => e.ConnectionConfigJson).HasColumnType("json").IsRequired();
				entity.Property(e => e.PollingInterval).HasDefaultValue(1000);
			});

			modelBuilder.Entity<Tag>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.DeviceId).IsRequired();
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Address).IsRequired().HasMaxLength(100);
				entity.Property(e => e.DataType)
					.HasConversion<string>()
					.HasColumnType(VARCHAR_50)
					.HasDefaultValue(DataType.FLOAT);
				entity.Property(e => e.AccessMode)
					.HasConversion<string>()
					.HasColumnType(VARCHAR_50)
					.HasDefaultValue(AccessMode.READONLY);
				entity.Property(e => e.IsScaled).HasDefaultValue(false);
				entity.Property(e => e.Unit).HasMaxLength(20);
				entity.Property(e => e.OpcUaNodeId).HasMaxLength(100);

				entity.HasOne(e => e.Device)
					.WithMany()
					.HasForeignKey(e => e.DeviceId)
					.OnDelete(DeleteBehavior.Cascade);
			});

			modelBuilder.Entity<MasterTable>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Description).HasMaxLength(255);
			});

			modelBuilder.Entity<MasterTableFields>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.MasterTableId).IsRequired();
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.DataType)
					.HasConversion<string>()
					.HasColumnType(VARCHAR_50)
					.HasDefaultValue(DataTypeTable.STRING);

				entity.HasOne(e => e.MasterTable)
					.WithMany(m => m.Fields)
					.HasForeignKey(e => e.MasterTableId)
					.OnDelete(DeleteBehavior.Cascade);
			});

			modelBuilder.Entity<StorageFlow>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Description).HasMaxLength(255);
				entity.Property(e => e.IsActive).HasDefaultValue(false);
				entity.Property(e => e.StorageInterval).HasDefaultValue(1000);

				entity.HasOne(e => e.MasterTable)
					.WithMany(m => m.StorageFlows)
					.HasForeignKey(e => e.MasterTableId)
					.OnDelete(DeleteBehavior.Restrict);
			});

			modelBuilder.Entity<StorageFlowDevice>(entity =>
			{
				entity.HasKey(e => e.Id);

				entity.HasOne(e => e.StorageFlow)
					.WithMany(sf => sf.StorageFlowDevices)
					.HasForeignKey(e => e.StorageFlowId)
					.OnDelete(DeleteBehavior.Cascade);

				entity.HasOne(e => e.Device)
					.WithMany()
					.HasForeignKey(e => e.DeviceId)
					.OnDelete(DeleteBehavior.Restrict);

				// Ensure unique combination of StorageFlowId and DeviceId
				entity.HasIndex(e => new { e.StorageFlowId, e.DeviceId }).IsUnique();
			});

			modelBuilder.Entity<StorageFlowMapping>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.SourcePath).IsRequired().HasMaxLength(500);

				entity.HasOne(e => e.StorageFlow)
					.WithMany(sf => sf.StorageFlowMappings)
					.HasForeignKey(e => e.StorageFlowId)
					.OnDelete(DeleteBehavior.Cascade);

				entity.HasOne(e => e.MasterTableField)
					.WithMany()
					.HasForeignKey(e => e.MasterTableFieldId)
					.OnDelete(DeleteBehavior.Restrict);

				entity.HasOne(e => e.Tag)
					.WithMany()
					.HasForeignKey(e => e.TagId)
					.OnDelete(DeleteBehavior.SetNull);
			});

			modelBuilder.Entity<FileMetadata>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
				entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
				entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
				entity.Property(e => e.ContentType).HasMaxLength(100);
				entity.Property(e => e.EntityType).HasMaxLength(50);
				entity.Property(e => e.FieldName).HasMaxLength(100);
				entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
				
				// Index for faster queries
				entity.HasIndex(e => new { e.EntityType, e.EntityId, e.FieldName });
				entity.HasIndex(e => e.DeletedAt);
			});
		}
	}
}
