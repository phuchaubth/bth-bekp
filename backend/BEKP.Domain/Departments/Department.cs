using BEKP.Domain.Common;

namespace BEKP.Domain.Departments;

public enum DepartmentGroup
{
    MalignantHematology,
    BenignHematology,
    PediatricHematology,
    StemCellTransplant,
    IntensiveCare,
}

/// <summary>An inpatient clinical department (khoa lâm sàng nội trú).</summary>
public sealed class Department : Entity
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 250;

    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Department(Guid id, string code, string name, string description, DepartmentGroup group, bool isSterileUnit, int sortOrder)
        : base(id)
    {
        if (!BusinessCodes.IsDepartmentCode(code))
        {
            throw new DomainException("Department code must match DEP-0000.");
        }

        Code = code;
        Name = Guard.Text(name, nameof(Name), NameMaxLength);
        Description = Guard.Text(description, nameof(Description), DescriptionMaxLength);
        Group = group;
        IsSterileUnit = isSterileUnit;
        SortOrder = sortOrder;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public DepartmentGroup Group { get; private set; }

    /// <summary>True for units whose beds are sterile (HEPA) rooms, e.g. stem cell transplant.</summary>
    public bool IsSterileUnit { get; private set; }

    public int SortOrder { get; private set; }
}
