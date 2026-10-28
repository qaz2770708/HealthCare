using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
namespace HealthCare.Models;

public class Staff
{
    public int Id { get; set; }
    [MaxLength(40)] public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(40)] public string Name { get; set; } = "";
    public string Role { get; set; } = "Reception";
    public string Department { get; set; } = "门诊服务";
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
}
public class Patient
{
    public int Id { get; set; }
    [MaxLength(50)] public string Name { get; set; } = "";
    public string Gender { get; set; } = "未知";
    public DateTime BirthDate { get; set; }
    [MaxLength(20)] public string Phone { get; set; } = "";
    [MaxLength(200)] public string Address { get; set; } = "";
    [MaxLength(500)] public string Allergies { get; set; } = "未提供";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [ConcurrencyCheck] public int Version { get; set; } = 1;
}
public class Appointment
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int DoctorId { get; set; }
    public Staff Doctor { get; set; } = null!;
    // Clinic wall-clock time, interpreted using Clinic:TimeZone.
    public DateTime Slot { get; set; }
    public string Status { get; set; } = "Scheduled";
    [MaxLength(200)] public string Reason { get; set; } = "";
    [ConcurrencyCheck] public int Version { get; set; } = 1;
    public Encounter? Encounter { get; set; }
    public Invoice? Invoice { get; set; }
}
public class Encounter
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    [MaxLength(2000)] public string Complaint { get; set; } = "";
    [MaxLength(2000)] public string Assessment { get; set; } = "";
    [MaxLength(3000)] public string Plan { get; set; } = "";
    public DateTime? FollowUpDate { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedUtc { get; set; }
    [ConcurrencyCheck] public int Version { get; set; } = 1;
}
public class Invoice
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    // Money is integer cents, never floating point.
    public int AmountCents { get; set; }
    public string Status { get; set; } = "Unpaid";
    public string? PaymentMethod { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidUtc { get; set; }
    [ConcurrencyCheck] public int Version { get; set; } = 1;
}
public class AuditEntry
{
    public int Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "系统";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
}
public class ClinicDb(DbContextOptions<ClinicDb> options) : DbContext(options)
{
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Staff>().HasIndex(x => x.Username).IsUnique();
        b.Entity<Patient>().HasIndex(x => x.Name);
        b.Entity<Patient>().HasIndex(x => x.Phone);
        b.Entity<Appointment>().HasIndex(x => new { x.DoctorId, x.Slot }).IsUnique().HasFilter("Status <> 'Cancelled'");
        b.Entity<Appointment>().HasIndex(x => new { x.PatientId, x.Slot }).IsUnique().HasFilter("Status <> 'Cancelled'");
        b.Entity<Appointment>().HasOne(x => x.Patient).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Appointment>().HasOne(x => x.Doctor).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Encounter>().HasIndex(x => x.AppointmentId).IsUnique();
        b.Entity<Invoice>().HasIndex(x => x.AppointmentId).IsUnique();
        b.Entity<Appointment>().HasOne(x => x.Encounter).WithOne(x => x.Appointment).HasForeignKey<Encounter>(x => x.AppointmentId);
        b.Entity<Appointment>().HasOne(x => x.Invoice).WithOne(x => x.Appointment).HasForeignKey<Invoice>(x => x.AppointmentId);
        b.Entity<AuditEntry>().HasIndex(x => x.AtUtc);
    }
}
