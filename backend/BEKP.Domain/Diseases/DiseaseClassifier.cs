using System.Text.RegularExpressions;

namespace BEKP.Domain.Diseases;

/// <summary>Hematology disease groups used for the disease pattern (mô hình bệnh tật) dashboard.</summary>
public enum DiseaseGroup
{
    AcuteMyeloidLeukemia,
    AcuteLymphoblasticLeukemia,
    ChronicLeukemia,
    HodgkinLymphoma,
    NonHodgkinLymphoma,
    MultipleMyeloma,
    MyelodysplasticSyndrome,
    MyeloproliferativeNeoplasm,
    Thalassemia,
    AplasticAnemia,
    OtherAnemia,
    Hemophilia,
    OtherCoagulationDisorder,
    ImmuneThrombocytopenia,
    Other,
}

/// <summary>Maps an ICD-10 principal diagnosis code to a hematology disease group.</summary>
public static partial class DiseaseClassifier
{
    // Ordered: the first matching prefix wins, so specific codes come before their 3-character category.
    private static readonly (string Prefix, DiseaseGroup Group)[] Rules =
    [
        ("C91.0", DiseaseGroup.AcuteLymphoblasticLeukemia),
        ("C92.0", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C92.4", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C92.5", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C92.6", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C92.8", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C93.0", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C94.0", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C94.2", DiseaseGroup.AcuteMyeloidLeukemia),
        ("C91", DiseaseGroup.ChronicLeukemia),
        ("C92", DiseaseGroup.ChronicLeukemia),
        ("C93", DiseaseGroup.ChronicLeukemia),
        ("C95", DiseaseGroup.ChronicLeukemia),
        ("C81", DiseaseGroup.HodgkinLymphoma),
        ("C82", DiseaseGroup.NonHodgkinLymphoma),
        ("C83", DiseaseGroup.NonHodgkinLymphoma),
        ("C84", DiseaseGroup.NonHodgkinLymphoma),
        ("C85", DiseaseGroup.NonHodgkinLymphoma),
        ("C86", DiseaseGroup.NonHodgkinLymphoma),
        ("C88", DiseaseGroup.NonHodgkinLymphoma),
        ("C90", DiseaseGroup.MultipleMyeloma),
        ("D46", DiseaseGroup.MyelodysplasticSyndrome),
        ("D45", DiseaseGroup.MyeloproliferativeNeoplasm),
        ("D47", DiseaseGroup.MyeloproliferativeNeoplasm),
        ("D56", DiseaseGroup.Thalassemia),
        ("D60", DiseaseGroup.AplasticAnemia),
        ("D61", DiseaseGroup.AplasticAnemia),
        ("D50", DiseaseGroup.OtherAnemia),
        ("D51", DiseaseGroup.OtherAnemia),
        ("D52", DiseaseGroup.OtherAnemia),
        ("D53", DiseaseGroup.OtherAnemia),
        ("D55", DiseaseGroup.OtherAnemia),
        ("D57", DiseaseGroup.OtherAnemia),
        ("D58", DiseaseGroup.OtherAnemia),
        ("D59", DiseaseGroup.OtherAnemia),
        ("D64", DiseaseGroup.OtherAnemia),
        ("D66", DiseaseGroup.Hemophilia),
        ("D67", DiseaseGroup.Hemophilia),
        ("D68", DiseaseGroup.OtherCoagulationDisorder),
        ("D69.3", DiseaseGroup.ImmuneThrombocytopenia),
    ];

    public static bool IsValidIcd10(string? code) => code is not null && Icd10Pattern().IsMatch(code);

    public static DiseaseGroup Classify(string icd10Code)
    {
        ArgumentNullException.ThrowIfNull(icd10Code);
        foreach (var (prefix, group) in Rules)
        {
            if (icd10Code.StartsWith(prefix, StringComparison.Ordinal))
            {
                return group;
            }
        }

        return DiseaseGroup.Other;
    }

    [GeneratedRegex(@"^[A-TV-Z][0-9]{2}(\.[0-9A-Z]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Icd10Pattern();
}
