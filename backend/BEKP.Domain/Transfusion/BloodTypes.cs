namespace BEKP.Domain.Transfusion;

public enum BloodProductType
{
    RedCells,
    Platelets,
    Plasma,
    Cryoprecipitate,
}

public enum AboGroup
{
    O,
    A,
    B,
    AB,
}

[Flags]
public enum BloodProductModifiers
{
    None = 0,
    Leukoreduced = 1,
    Irradiated = 2,
    Washed = 4,
    ExtendedPhenotypeMatch = 8,
}

public enum BloodRequestUrgency
{
    Routine,
    Urgent,
    Emergency,
}

public enum BloodRequestStatus
{
    Requested,
    CrossMatching,
    Approved,
    Issued,
    Transfusing,
    Completed,
    Cancelled,
}

public enum StockLevel
{
    Adequate,
    Low,
    Critical,
}
