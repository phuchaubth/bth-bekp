using BEKP.Domain.Neutropenia;
using BEKP.Domain.Occupancy;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Dashboard;

/// <summary>Derives operational alerts from a dashboard snapshot. Pure and deterministic.</summary>
public static class AlertEngine
{
    /// <summary>Share of neutropenic inpatients above which infection-control load is flagged.</summary>
    public const decimal HighNeutropeniaShare = 0.30m;

    public const int HighNeutropeniaMinimumOccupied = 5;

    public const int PendingBloodRequestsThreshold = 4;

    public static IReadOnlyList<AlertDto> Evaluate(
        IEnumerable<DepartmentSummaryDto> departments,
        IEnumerable<FebrileNeutropeniaDto> febrileNeutropenia,
        IEnumerable<BloodInventoryDto> inventory)
    {
        ArgumentNullException.ThrowIfNull(departments);
        ArgumentNullException.ThrowIfNull(febrileNeutropenia);
        ArgumentNullException.ThrowIfNull(inventory);

        var alerts = new List<AlertDto>();

        foreach (var episode in febrileNeutropenia)
        {
            if (episode.AntibioticStartedAtUtc is not null)
            {
                continue;
            }

            var severity = episode.Status == FebrileNeutropeniaStatus.OnTrack ? AlertSeverity.Warning : AlertSeverity.Critical;
            alerts.Add(new AlertDto(AlertType.FebrileNeutropeniaAntibioticDelay, severity, episode.DepartmentCode, episode.DepartmentName, episode.StayCode, episode.ElapsedMinutes, FebrileNeutropeniaEpisode.TargetMinutes, episode.FeverDetectedAtUtc));
        }

        foreach (var item in inventory)
        {
            var subject = $"{item.Product}:{item.AboGroup}";
            if (item.Level == StockLevel.Critical)
            {
                alerts.Add(new AlertDto(AlertType.BloodStockCritical, AlertSeverity.Critical, null, null, subject, item.UnitsAvailable, item.MinimumUnits, item.UpdatedAtUtc));
            }
            else if (item.Level == StockLevel.Low)
            {
                alerts.Add(new AlertDto(AlertType.BloodStockLow, AlertSeverity.Warning, null, null, subject, item.UnitsAvailable, item.MinimumUnits, item.UpdatedAtUtc));
            }
        }

        foreach (var d in departments)
        {
            if (d.OccupancyLevel == OccupancyLevel.OverCapacity)
            {
                alerts.Add(new AlertDto(AlertType.DepartmentOverCapacity, AlertSeverity.Critical, d.Code, d.Name, null, d.OccupancyPercent, d.ExtraBedsInUse, null));
            }
            else if (d.Capacity > 0 && d.Available == 0)
            {
                var type = d.IsSterileUnit ? AlertType.SterileRoomsFull : AlertType.DepartmentFull;
                alerts.Add(new AlertDto(type, AlertSeverity.Warning, d.Code, d.Name, null, d.Occupied, d.Capacity, null));
            }

            if (d.Occupied >= HighNeutropeniaMinimumOccupied && d.Neutropenic > d.Occupied * HighNeutropeniaShare)
            {
                alerts.Add(new AlertDto(AlertType.HighNeutropeniaLoad, AlertSeverity.Warning, d.Code, d.Name, null, d.Neutropenic, d.Occupied, null));
            }

            if (d.PendingBloodRequests >= PendingBloodRequestsThreshold)
            {
                alerts.Add(new AlertDto(AlertType.PendingBloodRequests, AlertSeverity.Warning, d.Code, d.Name, null, d.PendingBloodRequests, PendingBloodRequestsThreshold, null));
            }
        }

        return alerts
            .OrderBy(a => a.Severity)
            .ThenBy(a => a.Type)
            .ThenByDescending(a => a.Value)
            .ToList();
    }
}
