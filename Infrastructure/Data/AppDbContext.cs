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
		public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

		// List of tables in the database
		public DbSet<User> Users { get; set; }
		public DbSet<Device> Devices { get; set; }
		public DbSet<Tag> Tags { get; set; }

		// Fluent API configurations
		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			modelBuilder.Entity<User>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
				entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
				entity.Property(e => e.Role).HasDefaultValue(UserRole.VIEWER);
			});

			modelBuilder.Entity<Device>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Description).HasMaxLength(255);
				entity.Property(e => e.IsEnabled).HasDefaultValue(false);
				entity.Property(e => e.Protocol).HasDefaultValue(Protocol.HTTP);
				entity.Property(e => e.ConnectionConfigJson).HasColumnType("json").HasDefaultValue("{}");
			});

			modelBuilder.Entity<Tag>(entity =>
			{
				entity.HasKey(e => e.Id);
				entity.Property(e => e.DeviceId).IsRequired();
				entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
				entity.Property(e => e.Address).IsRequired().HasMaxLength(100);
				entity.Property(e => e.DataType).HasDefaultValue(DataType.FLOAT);
				entity.Property(e => e.AccessMode).HasDefaultValue(AccessMode.READONLY);
				entity.Property(e => e.IsScaled).HasDefaultValue(false);
				entity.Property(e => e.Unit).HasMaxLength(20);
				entity.Property(e => e.OpcUaNodeId).HasMaxLength(100);

				entity.HasOne(e => e.Device)
					.WithMany()
					.HasForeignKey(e => e.DeviceId)
					.OnDelete(DeleteBehavior.Cascade);
			});
		}
	}
}
