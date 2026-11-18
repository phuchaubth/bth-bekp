using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;
using Microsoft.EntityFrameworkCore;

namespace BEKP.Persistence.Seeding;

/// <summary>
/// Generates SYNTHETIC demo data for a hematology &amp; transfusion hospital.
/// Contains no real hospital or patient data: stay codes are sequential, diagnoses are drawn from ICD-10 pools.
/// </summary>
public static class SyntheticDataSeeder
{
    private sealed record DepartmentSeed(
        string Code,
        string Name,
        string Description,
        DepartmentGroup Group,
        int Floor,
        int Beds,
        int Occupied,
        int Cleaning,
        int Severe,
        int Neutropenic,
        int Transfusing,
        int Chemotherapy,
        int ExpectedDischarges,
        string? ZonePrefix,
        string[] Diagnoses);

    private static readonly DepartmentSeed[] Seeds =
    [
        new("DEP-0001", "Bạch cầu cấp người lớn", "Lơ-xê-mi cấp, hóa trị tấn công và củng cố", DepartmentGroup.MalignantHematology, 2, 48, 47, 1, 7, 18, 6, 14, 2, null, ["C92.0", "C92.0", "C92.0", "C91.0", "C91.0", "C92.5", "C93.0"]),
        new("DEP-0002", "U lympho – Đa u tủy", "U lympho, đa u tủy xương, hóa – miễn dịch", DepartmentGroup.MalignantHematology, 3, 44, 39, 1, 3, 6, 3, 17, 4, null, ["C83.3", "C83.3", "C81.1", "C90.0", "C90.0", "C85.9", "C82.0"]),
        new("DEP-0003", "Huyết học Nhi", "Bệnh máu ác tính và lành tính trẻ em", DepartmentGroup.PediatricHematology, 4, 50, 52, 0, 5, 12, 5, 15, 3, null, ["C91.0", "C91.0", "C91.0", "C92.0", "D61.9", "D69.3", "D56.1"]),
        new("DEP-0004", "Ghép tế bào gốc tạo máu", "Ghép tự thân và đồng loài, phòng vô trùng HEPA", DepartmentGroup.StemCellTransplant, 5, 12, 12, 0, 3, 9, 3, 2, 0, "VT", ["C92.0", "C91.0", "D61.9", "C90.0", "D46.9"]),
        new("DEP-0005", "Thalassemia & Bệnh lý hồng cầu", "Truyền máu định kỳ, thải sắt, thiếu máu tan máu", DepartmentGroup.BenignHematology, 6, 40, 33, 1, 1, 0, 11, 0, 6, null, ["D56.1", "D56.1", "D56.1", "D56.0", "D58.2", "D59.1", "D50.0"]),
        new("DEP-0006", "Hemophilia & Rối loạn đông máu", "Hemophilia, von Willebrand, rối loạn đông cầm máu", DepartmentGroup.BenignHematology, 7, 30, 22, 1, 2, 0, 2, 0, 3, null, ["D66", "D66", "D66", "D67", "D68.0", "D69.3"]),
        new("DEP-0007", "Suy tủy & Bệnh lý tủy lành tính", "Suy tủy xương, giảm tiểu cầu miễn dịch", DepartmentGroup.BenignHematology, 8, 32, 29, 1, 2, 8, 5, 0, 2, null, ["D61.9", "D61.9", "D69.3", "D69.3", "D46.9", "D45", "D59.5"]),
        new("DEP-0008", "Hồi sức tích cực Huyết học", "Hồi sức người bệnh huyết học nặng", DepartmentGroup.IntensiveCare, 1, 16, 15, 1, 11, 7, 4, 0, 0, "HS", ["C92.0", "C91.0", "D61.9", "C83.3", "D69.3"]),
    ];

    public static async Task<bool> SeedAsync(BekpDbContext db, DateTime nowUtc, DateTime dayStartUtc, DateOnly localToday, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await db.Departments.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var random = new Random(20261117);
        var stayNumber = 5000;
        var dayStart = dayStartUtc;
        var departments = new List<Department>();

        for (var index = 0; index < Seeds.Length; index++)
        {
            var seed = Seeds[index];
            var department = new Department(Guid.NewGuid(), seed.Code, seed.Name, seed.Description, seed.Group, seed.ZonePrefix == "VT", index + 1);
            departments.Add(department);
            db.Departments.Add(department);

            var beds = CreateBeds(department, seed, nowUtc);
            db.Beds.AddRange(beds);

            // Active stays: fill planned beds, then extra beds when the unit is over capacity.
            var planned = beds.Where(b => !b.IsExtra).ToList();
            var free = Shuffle(planned, random).Take(Math.Max(0, seed.Beds - seed.Occupied)).ToHashSet();
            var cleaning = free.Take(seed.Cleaning).ToHashSet();
            var occupiedBeds = planned.Where(b => !free.Contains(b)).Concat(beds.Where(b => b.IsExtra).Take(Math.Max(0, seed.Occupied - seed.Beds))).ToList();

            var stays = new List<InpatientStay>();
            foreach (var bed in occupiedBeds)
            {
                var admittedToday = stays.Count < 3 && random.NextDouble() < 0.25;
                var admittedAt = admittedToday ? dayStart.AddMinutes(random.Next(30, (int)Math.Max(31, (nowUtc - dayStart).TotalMinutes))) : nowUtc.AddHours(-random.Next(24, 24 * 30));
                var stay = new InpatientStay(Guid.NewGuid(), $"ADM-{stayNumber++:D6}", department.Id, bed.Id, admittedAt, CareLevel.Level3, Pick(seed.Diagnoses, random), null);
                bed.Occupy(admittedAt);
                stays.Add(stay);
            }

            foreach (var bed in cleaning)
            {
                bed.SetHousekeepingStatus(BedStatus.Cleaning, nowUtc.AddMinutes(-random.Next(5, 50)));
            }

            ApplyFlags(stays, seed, random, localToday);
            db.InpatientStays.AddRange(stays);

            AddHistory(db, department, seed, beds[0], nowUtc, dayStart, random, ref stayNumber);
            AddBloodRequests(db, department, stays, nowUtc, random);
        }

        AddFebrileNeutropenia(db, nowUtc);
        AddInventory(db, nowUtc);
        AddEvents(db, departments, nowUtc);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static List<Bed> CreateBeds(Department department, DepartmentSeed seed, DateTime nowUtc)
    {
        var beds = new List<Bed>();
        for (var i = 0; i < seed.Beds; i++)
        {
            var roomIndex = i / 6;
            string code;
            string room;
            if (seed.ZonePrefix is { } prefix)
            {
                code = $"{prefix}-{i + 1:D2}";
                room = (prefix == "VT" ? "Khu vô trùng " : "Khu hồi sức ") + (char)('A' + roomIndex);
            }
            else
            {
                var roomNumber = (seed.Floor * 100) + roomIndex + 1;
                code = $"{roomNumber}-{(i % 6) + 1}";
                room = $"Phòng {roomNumber}";
            }

            beds.Add(new Bed(Guid.NewGuid(), department.Id, code, room, false, i, nowUtc.AddDays(-60)));
        }

        for (var j = 1; j <= 3; j++)
        {
            beds.Add(new Bed(Guid.NewGuid(), department.Id, $"KT-{j}", "Kê thêm", true, 1000 + j, nowUtc.AddDays(-60)));
        }

        return beds;
    }

    private static void ApplyFlags(List<InpatientStay> stays, DepartmentSeed seed, Random random, DateOnly today)
    {
        var order = Shuffle(stays, random);
        var severe = order.Take(seed.Severe).ToHashSet();
        var expected = order.Skip(seed.Severe).Take(seed.ExpectedDischarges).ToHashSet();
        var neutropenic = Shuffle(stays, random).Take(seed.Neutropenic).ToHashSet();
        var transfusing = Shuffle(stays, random).Take(seed.Transfusing).ToHashSet();
        var chemo = Shuffle(stays, random).Take(seed.Chemotherapy).ToHashSet();

        foreach (var stay in stays)
        {
            var care = severe.Contains(stay) ? CareLevel.Level1 : neutropenic.Contains(stay) ? CareLevel.Level2 : CareLevel.Level3;
            stay.UpdateClinicalStatus(
                care,
                stay.PrincipalDiagnosisCode,
                neutropenic.Contains(stay),
                transfusing.Contains(stay),
                chemo.Contains(stay),
                expected.Contains(stay) ? today : today.AddDays(random.Next(2, 20)));
        }
    }

    private static void AddHistory(BekpDbContext db, Department department, DepartmentSeed seed, Bed anyBed, DateTime nowUtc, DateTime dayStart, Random random, ref int stayNumber)
    {
        // Discharged stays over the last 30 days feed the disease pattern and "discharged today".
        var historical = Math.Max(4, seed.Beds / 2);
        for (var i = 0; i < historical; i++)
        {
            var dischargedToday = i < Math.Max(1, seed.ExpectedDischarges / 2);
            var discharged = dischargedToday ? dayStart.AddMinutes(random.Next(10, (int)Math.Max(11, (nowUtc - dayStart).TotalMinutes))) : nowUtc.AddDays(-random.Next(1, 29));
            var admitted = discharged.AddDays(-random.Next(2, 20));
            var stay = new InpatientStay(Guid.NewGuid(), $"ADM-{stayNumber++:D6}", department.Id, anyBed.Id, admitted, CareLevel.Level3, Pick(seed.Diagnoses, random), null);
            stay.Discharge(discharged);
            db.InpatientStays.Add(stay);
        }

        // 30 days of measured process times around their targets.
        var standards = seed.ZonePrefix == "VT"
            ? new[] { TimeStandardType.BloodIssueRoutine, TimeStandardType.TransfusionStartAfterIssue, TimeStandardType.FebrileNeutropeniaAntibiotic, TimeStandardType.BedTurnaround }
            : Enum.GetValues<TimeStandardType>();
        foreach (var standard in standards)
        {
            var typical = standard switch
            {
                TimeStandardType.BloodIssueEmergency => 24,
                TimeStandardType.BloodIssueUrgent => 48,
                TimeStandardType.BloodIssueRoutine => 170,
                TimeStandardType.TransfusionStartAfterIssue => 22,
                TimeStandardType.FebrileNeutropeniaAntibiotic => 50,
                _ => 45,
            };
            var count = standard == TimeStandardType.FebrileNeutropeniaAntibiotic && seed.Neutropenic == 0 ? 0 : random.Next(6, 18);
            for (var i = 0; i < count; i++)
            {
                var completed = nowUtc.AddMinutes(-random.Next(30, 60 * 24 * 29));
                var minutes = Math.Max(3, (int)(typical * (0.5 + random.NextDouble())));
                db.ProcessTimings.Add(new ProcessTiming(Guid.NewGuid(), standard, department.Id, completed.AddMinutes(-minutes), completed, null));
            }
        }
    }

    private static void AddBloodRequests(BekpDbContext db, Department department, List<InpatientStay> stays, DateTime nowUtc, Random random)
    {
        var candidates = stays.Where(s => s.IsTransfusing || s.IsSevere).ToList();
        if (candidates.Count == 0)
        {
            candidates = stays;
        }

        var products = department.Group switch
        {
            DepartmentGroup.BenignHematology when department.Code == "DEP-0006" => new[] { BloodProductType.Cryoprecipitate, BloodProductType.Plasma },
            DepartmentGroup.BenignHematology => new[] { BloodProductType.RedCells, BloodProductType.RedCells, BloodProductType.Platelets },
            DepartmentGroup.IntensiveCare => new[] { BloodProductType.Plasma, BloodProductType.Platelets, BloodProductType.RedCells },
            _ => new[] { BloodProductType.RedCells, BloodProductType.Platelets },
        };

        var pending = Math.Min(candidates.Count, 2 + random.Next(0, 4));
        for (var i = 0; i < pending; i++)
        {
            var stay = candidates[i];
            var product = products[i % products.Length];
            var units = product switch
            {
                BloodProductType.Cryoprecipitate => 10,
                BloodProductType.Plasma => 4,
                BloodProductType.Platelets => 1,
                _ => 2,
            };
            var modifiers = department.IsSterileUnit
                ? BloodProductModifiers.Irradiated | BloodProductModifiers.Leukoreduced
                : product == BloodProductType.RedCells ? BloodProductModifiers.Leukoreduced : BloodProductModifiers.None;
            var urgency = stay.IsSevere && i == 0 ? BloodRequestUrgency.Urgent : BloodRequestUrgency.Routine;
            var request = new BloodRequest(Guid.NewGuid(), stay.Id, department.Id, product, (AboGroup)random.Next(0, 4), random.NextDouble() > 0.02, units, urgency, modifiers, nowUtc.AddMinutes(-random.Next(5, 40)));
            var steps = i % 4;
            var at = request.RequestedAtUtc;
            if (steps >= 1)
            {
                request.ChangeStatus(product == BloodProductType.RedCells ? BloodRequestStatus.CrossMatching : BloodRequestStatus.Approved, at = at.AddMinutes(2));
            }

            if (steps >= 2 && request.Status == BloodRequestStatus.CrossMatching)
            {
                request.ChangeStatus(BloodRequestStatus.Approved, at.AddMinutes(2));
            }

            db.BloodRequests.Add(request);
        }

        // Units issued earlier today.
        foreach (var stay in stays.Where(s => s.IsTransfusing))
        {
            var issued = new BloodRequest(Guid.NewGuid(), stay.Id, department.Id, products[0], (AboGroup)random.Next(0, 4), true, products[0] == BloodProductType.RedCells ? 2 : 1, BloodRequestUrgency.Routine, BloodProductModifiers.Leukoreduced, nowUtc.AddMinutes(-random.Next(120, 300)));
            issued.ChangeStatus(BloodRequestStatus.Approved, issued.RequestedAtUtc.AddMinutes(30));
            issued.ChangeStatus(BloodRequestStatus.Issued, issued.RequestedAtUtc.AddMinutes(60));
            issued.ChangeStatus(BloodRequestStatus.Transfusing, issued.RequestedAtUtc.AddMinutes(75));
            db.BloodRequests.Add(issued);
        }
    }

    private static void AddFebrileNeutropenia(BekpDbContext db, DateTime nowUtc)
    {
        var neutropenic = db.ChangeTracker.Entries<InpatientStay>()
            .Select(e => e.Entity)
            .Where(s => s.IsActive && s.IsNeutropenic)
            .GroupBy(s => s.DepartmentId)
            .Select(g => g.First())
            .Take(3)
            .ToList();
        var minutesAgo = new[] { 42, 18, 55 };
        for (var i = 0; i < neutropenic.Count; i++)
        {
            var stay = neutropenic[i];
            var episode = new FebrileNeutropeniaEpisode(Guid.NewGuid(), stay.Id, stay.DepartmentId, nowUtc.AddMinutes(-minutesAgo[i]), 38.6m + (0.3m * i));
            if (i == 2)
            {
                episode.RecordAntibioticStart(episode.FeverDetectedAtUtc.AddMinutes(35));
            }

            db.FebrileNeutropeniaEpisodes.Add(episode);
        }
    }

    private static void AddInventory(BekpDbContext db, DateTime nowUtc)
    {
        var data = new (BloodProductType Product, int[] Units, int[] Minimum)[]
        {
            (BloodProductType.RedCells, [186, 142, 96, 31], [150, 120, 110, 30]),
            (BloodProductType.Platelets, [9, 14, 18, 4], [20, 15, 15, 5]),
            (BloodProductType.Plasma, [210, 160, 150, 40], [150, 120, 120, 30]),
            (BloodProductType.Cryoprecipitate, [35, 28, 22, 6], [20, 15, 15, 5]),
        };
        foreach (var (product, units, minimum) in data)
        {
            for (var g = 0; g < 4; g++)
            {
                db.BloodInventory.Add(new BloodInventoryItem(Guid.NewGuid(), product, (AboGroup)g, units[g], minimum[g], nowUtc.AddMinutes(-2)));
            }
        }
    }

    private static void AddEvents(BekpDbContext db, List<Department> departments, DateTime nowUtc)
    {
        var stays = db.ChangeTracker.Entries<InpatientStay>().Select(e => e.Entity).Where(s => s.IsActive).ToList();
        var minute = 1;
        foreach (var department in departments.Take(6))
        {
            var stay = stays.First(s => s.DepartmentId == department.Id);
            db.OperationalEvents.Add(new OperationalEvent(Guid.NewGuid(), OperationalEventType.Admission, nowUtc.AddMinutes(-(minute += 4)), department.Id, department.Code, stay.Code, null, null));
        }
    }

    private static string Pick(string[] values, Random random) => values[random.Next(values.Length)];

    private static List<T> Shuffle<T>(IEnumerable<T> source, Random random) => source.OrderBy(_ => random.Next()).ToList();
}
