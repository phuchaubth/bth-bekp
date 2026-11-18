using BEKP.Application.Abstractions;
using BEKP.Application.Common;
using BEKP.Application.Inpatient;
using BEKP.Domain.Beds;
using BEKP.Domain.Common;
using BEKP.Domain.Transfusion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BEKP.Infrastructure.Simulation;

public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    /// <summary>Generates synthetic inpatient events for demos. Never runs in Production.</summary>
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 8;
}

/// <summary>Drives the realtime demo by sending synthetic events through the same application commands as integrations.</summary>
internal sealed partial class SyntheticActivitySimulator(
    IServiceScopeFactory scopeFactory,
    IOptions<SimulationOptions> options,
    IHostEnvironment environment,
    ILogger<SyntheticActivitySimulator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (environment.IsProduction())
        {
            LogRefused(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.IntervalSeconds, 2, 300));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await StepAsync(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DomainException or RequestValidationException or ResourceNotFoundException or InvalidOperationException)
            {
                LogStepSkipped(logger, ex.Message);
            }
        }
    }

    private static async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var repository = services.GetRequiredService<IInpatientRepository>();
        var commands = services.GetRequiredService<IInpatientCommandService>();
        var clock = services.GetRequiredService<IClock>();
        var random = Random.Shared;

        var stays = await repository.GetActiveStaysAsync(null, ct).ConfigureAwait(false);
        var departments = await repository.GetDepartmentsAsync(ct).ConfigureAwait(false);
        if (stays.Count == 0 || departments.Count == 0)
        {
            return;
        }

        switch (random.Next(0, 9))
        {
            case 0:
            {
                var stay = stays[random.Next(stays.Count)];
                await commands.DischargeAsync(new DischargeStayCommand(stay.Code, null), ct).ConfigureAwait(false);
                break;
            }

            case 1:
            {
                var beds = await repository.GetBedsAsync(null, ct).ConfigureAwait(false);
                var cleaning = beds.Where(b => b.Status == BedStatus.Cleaning).OrderBy(b => b.StatusChangedAtUtc).FirstOrDefault();
                if (cleaning is not null)
                {
                    var department = departments.First(d => d.Id == cleaning.DepartmentId);
                    await commands.UpdateBedStatusAsync(new UpdateBedStatusCommand(department.Code, cleaning.Code, BedStatus.Available), ct).ConfigureAwait(false);
                }

                break;
            }

            case 2:
            {
                var beds = await repository.GetBedsAsync(null, ct).ConfigureAwait(false);
                var free = beds.Where(b => b.Status == BedStatus.Available && !b.IsExtra).ToList();
                if (free.Count > 0)
                {
                    var bed = free[random.Next(free.Count)];
                    var department = departments.First(d => d.Id == bed.DepartmentId);
                    var peer = stays.FirstOrDefault(s => s.DepartmentId == department.Id);
                    var code = $"ADM-{random.Next(100000, 999999):D6}";
                    await commands.AdmitAsync(new AdmitStayCommand(department.Code, bed.Code, code, Domain.Stays.CareLevel.Level3, peer?.PrincipalDiagnosisCode ?? "C92.0", null, null), ct).ConfigureAwait(false);
                }

                break;
            }

            case 3:
            {
                var stay = stays[random.Next(stays.Count)];
                await commands.UpdateClinicalStatusAsync(
                    new UpdateClinicalStatusCommand(stay.Code, stay.CareLevel, stay.PrincipalDiagnosisCode, stay.IsNeutropenic, !stay.IsTransfusing, stay.HasChemotherapyToday, stay.ExpectedDischargeDate),
                    ct).ConfigureAwait(false);
                break;
            }

            case 4:
            case 5:
            {
                var requests = await repository.GetPendingBloodRequestsAsync(null, ct).ConfigureAwait(false);
                if (requests.Count > 0)
                {
                    var request = requests[random.Next(requests.Count)];
                    var next = request.Status switch
                    {
                        BloodRequestStatus.Requested => request.Product == BloodProductType.RedCells ? BloodRequestStatus.CrossMatching : BloodRequestStatus.Approved,
                        BloodRequestStatus.CrossMatching => BloodRequestStatus.Approved,
                        BloodRequestStatus.Approved => BloodRequestStatus.Issued,
                        _ => BloodRequestStatus.Transfusing,
                    };
                    await commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(request.Id, next), ct).ConfigureAwait(false);
                }

                break;
            }

            case 6:
            {
                var candidates = stays.Where(s => s.IsSevere || s.IsTransfusing).ToList();
                if (candidates.Count > 0)
                {
                    var stay = candidates[random.Next(candidates.Count)];
                    var product = (BloodProductType)random.Next(0, 3);
                    await commands.CreateBloodRequestAsync(
                        new CreateBloodRequestCommand(stay.Code, product, (AboGroup)random.Next(0, 4), true, product == BloodProductType.RedCells ? 2 : 1, BloodRequestUrgency.Routine, BloodProductModifiers.Leukoreduced),
                        ct).ConfigureAwait(false);
                }

                break;
            }

            case 7:
            {
                var episodes = await repository.GetActiveEpisodesAsync(null, clock.UtcNow, ct).ConfigureAwait(false);
                var open = episodes.FirstOrDefault(e => e.AwaitingAntibiotic && e.ElapsedMinutes(clock.UtcNow) >= random.Next(15, 70));
                if (open is not null)
                {
                    await commands.RecordAntibioticStartAsync(new RecordAntibioticStartCommand(open.Id, null), ct).ConfigureAwait(false);
                }
                else if (episodes.Count(e => e.AwaitingAntibiotic) < 3)
                {
                    var neutropenic = stays.Where(s => s.IsNeutropenic).ToList();
                    if (neutropenic.Count > 0)
                    {
                        var stay = neutropenic[random.Next(neutropenic.Count)];
                        await commands.RecordFeverAsync(new RecordFeverCommand(stay.Code, 38.0m + (random.Next(0, 15) / 10m), null), ct).ConfigureAwait(false);
                    }
                }

                break;
            }

            default:
            {
                var inventory = await repository.GetBloodInventoryAsync(ct).ConfigureAwait(false);
                if (inventory.Count > 0)
                {
                    var item = inventory[random.Next(inventory.Count)];
                    var units = Math.Max(0, item.UnitsAvailable + random.Next(-3, 6));
                    await commands.SetBloodInventoryAsync(new SetBloodInventoryCommand(item.Product, item.AboGroup, units, item.MinimumUnits), ct).ConfigureAwait(false);
                }

                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic activity simulation is disabled in the Production environment.")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Simulation step skipped: {Reason}")]
    private static partial void LogStepSkipped(ILogger logger, string reason);
}
