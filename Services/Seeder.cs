using HealthCare.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace HealthCare.Services;
public static class Seeder
{
    public static async Task Initialize(IServiceProvider services, bool demo, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        if (await db.Staff.AnyAsync()) return;
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Staff>>();
        var password = demo ? "ClinicDemo!2026" : config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) throw new InvalidOperationException("首次生产启动须设置 Bootstrap__AdminPassword，至少 12 位。");
        Staff Make(string username, string name, string role, string department)
        {
            var s = new Staff { Username = username, Name = name, Role = role, Department = department };
            s.PasswordHash = hasher.HashPassword(s, password); return s;
        }
        db.Staff.Add(Make("admin", demo ? "林管理员" : "系统管理员", "Admin", "运营管理"));
        if (!demo) { await db.SaveChangesAsync(); return; }
        var doctors = new[] { Make("doctor", "陈医生", "Doctor", "全科医学"), Make("doctor2", "周医生", "Doctor", "内科"), Make("doctor3", "许医生", "Doctor", "健康管理") };
        db.Staff.AddRange(doctors); db.Staff.Add(Make("reception", "王前台", "Reception", "门诊服务"));
        var today = scope.ServiceProvider.GetRequiredService<ClinicClock>().Today;
        var names = new[] { "李晓（演示）", "王明（演示）", "张悦（演示）", "刘晨（演示）", "陈安（演示）", "赵宁（演示）", "周晴（演示）", "吴桐（演示）", "孙禾（演示）", "郑一（演示）", "林可（演示）", "许乐（演示）" };
        var patients = names.Select((n, i) => new Patient { Name = n, Gender = i % 2 == 0 ? "女" : "男", BirthDate = today.AddYears(-25-i*3), Phone = $"1390000{i:0000}", Address = "演示社区 · 虚构地址", Allergies = i == 2 ? "过敏信息待医生核实" : "未提供", CreatedUtc = DateTime.UtcNow.AddDays(-20+i) }).ToArray();
        db.Patients.AddRange(patients); await db.SaveChangesAsync();
        for (int day = -6; day <= 1; day++)
        for (int i = 0; i < (day == 0 ? 10 : 4); i++)
        {
            var completed = day < 0 || (day == 0 && i < 3);
            var a = new Appointment { PatientId = patients[(i-day+12)%12].Id, DoctorId = doctors[i%3].Id, Slot = today.AddDays(day).AddHours(8+i/2.0), Reason = new[] { "门诊咨询", "复诊随访", "健康检查" }[i%3], Status = completed ? "Completed" : day == 0 && i < 6 ? "CheckedIn" : "Scheduled" };
            db.Appointments.Add(a);
            if (completed)
            {
                a.Encounter = new Encounter { Complaint = "演示记录：患者咨询健康管理。", Assessment = "虚构演示内容，不代表医学判断。", Plan = "演示随访记录，由医务人员填写实际处置。", FinalizedUtc = DateTime.UtcNow.AddDays(day), FollowUpDate = today.AddDays(7+i) };
                a.Invoice = new Invoice { AmountCents = config.GetValue<int>("Clinic:ConsultationFeeCents"), Status = i%3 == 0 ? "Unpaid" : "Paid", PaymentMethod = i%3 == 0 ? null : "现金", PaidUtc = i%3 == 0 ? null : DateTime.UtcNow.AddDays(day), CreatedUtc = DateTime.UtcNow.AddDays(day) };
            }
        }
        db.Audit.Add(new AuditEntry { Action = "初始化演示数据", Target = "Development" }); await db.SaveChangesAsync();
    }
}
