using Consent.Application.Abstractions;
using Consent.Domain.Patients;
using FluentValidation;

namespace Consent.Application.Patients;

internal static class PatientRules
{
    public const string PhonePattern = @"^(0|\+84)[0-9]{9,10}$";

    /// <summary>Mã thẻ BHYT 15 ký tự (2 chữ + 13 số) hoặc mã số BHXH 10 chữ số.</summary>
    public const string HealthInsurancePattern = "^([A-Za-z]{2}[0-9]{13}|[0-9]{10})$";

    public static readonly DateOnly MinimumDate = new(1900, 1, 1);
}

public sealed class PatientRequestValidator : AbstractValidator<PatientRequest>
{
    public PatientRequestValidator(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var today = () => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Vui lòng nhập họ tên người bệnh.")
            .MaximumLength(Patient.FullNameMaxLength).WithMessage($"Họ tên tối đa {Patient.FullNameMaxLength} ký tự.");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d >= PatientRules.MinimumDate).WithMessage("Ngày sinh không hợp lệ.")
            .Must(d => d <= today()).WithMessage("Ngày sinh không được sau ngày hiện tại.");
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Giới tính không hợp lệ.");

        RuleFor(x => x.CitizenId)
            .Must(CitizenIdNumber.HasValidFormat).WithMessage("Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.CitizenId));
        RuleFor(x => x)
            .Custom((request, context) =>
            {
                if (!string.IsNullOrWhiteSpace(request.CitizenId) && CitizenIdNumber.HasValidFormat(request.CitizenId.Trim())
                    && CitizenIdNumber.CheckConsistency(request.CitizenId.Trim(), request.DateOfBirth, request.Gender) is { } error)
                {
                    context.AddFailure(nameof(PatientRequest.CitizenId), error);
                }
            });
        RuleFor(x => x.OldIdNumber)
            .Matches("^([0-9]{9}|[0-9]{12})$").WithMessage("Số CMND cũ phải gồm 9 hoặc 12 chữ số.")
            .When(x => !string.IsNullOrWhiteSpace(x.OldIdNumber));
        RuleFor(x => x.CitizenIdIssueDate)
            .Must((request, issued) => issued >= request.DateOfBirth && issued <= today())
            .WithMessage("Ngày cấp CCCD phải sau ngày sinh và không sau ngày hiện tại.")
            .When(x => x.CitizenIdIssueDate is not null);
        RuleFor(x => x.PermanentAddress)
            .MaximumLength(Patient.AddressMaxLength).WithMessage($"Địa chỉ tối đa {Patient.AddressMaxLength} ký tự.");
        RuleFor(x => x.CurrentAddress)
            .MaximumLength(Patient.AddressMaxLength).WithMessage($"Địa chỉ tối đa {Patient.AddressMaxLength} ký tự.");
        RuleFor(x => x.PhoneNumber)
            .Matches(PatientRules.PhonePattern).WithMessage("Số điện thoại không hợp lệ (ví dụ 0912345678).")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Ethnicity)
            .MaximumLength(Patient.EthnicityMaxLength).WithMessage($"Dân tộc tối đa {Patient.EthnicityMaxLength} ký tự.");
        RuleFor(x => x.Occupation)
            .MaximumLength(Patient.OccupationMaxLength).WithMessage($"Nghề nghiệp tối đa {Patient.OccupationMaxLength} ký tự.");
        RuleFor(x => x.Nationality)
            .MaximumLength(Patient.NationalityMaxLength).WithMessage($"Quốc tịch tối đa {Patient.NationalityMaxLength} ký tự.");
        RuleFor(x => x.HealthInsuranceNumber)
            .Matches(PatientRules.HealthInsurancePattern)
            .WithMessage("Mã BHYT phải gồm 15 ký tự (2 chữ cái + 13 chữ số) hoặc mã số BHXH 10 chữ số.")
            .When(x => !string.IsNullOrWhiteSpace(x.HealthInsuranceNumber));
        RuleFor(x => x.PatientCode)
            .MaximumLength(Patient.PatientCodeMaxLength).WithMessage($"Mã người bệnh tối đa {Patient.PatientCodeMaxLength} ký tự.")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Mã người bệnh chỉ gồm chữ không dấu, số, gạch ngang hoặc gạch dưới.")
            .When(x => !string.IsNullOrWhiteSpace(x.PatientCode));
    }
}

public sealed class RepresentativeRequestValidator : AbstractValidator<RepresentativeRequest>
{
    public RepresentativeRequestValidator(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var today = () => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Vui lòng nhập họ tên người đại diện.")
            .MaximumLength(Patient.FullNameMaxLength).WithMessage($"Họ tên tối đa {Patient.FullNameMaxLength} ký tự.");
        RuleFor(x => x.Relationship).IsInEnum().WithMessage("Quan hệ với người bệnh không hợp lệ.");
        RuleFor(x => x.CitizenId)
            .Must(CitizenIdNumber.HasValidFormat).WithMessage("Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.CitizenId));
        RuleFor(x => x)
            .Custom((request, context) =>
            {
                if (!string.IsNullOrWhiteSpace(request.CitizenId) && CitizenIdNumber.HasValidFormat(request.CitizenId.Trim())
                    && request.DateOfBirth is { } dob && request.Gender is { } gender
                    && CitizenIdNumber.CheckConsistency(request.CitizenId.Trim(), dob, gender) is { } error)
                {
                    context.AddFailure(nameof(RepresentativeRequest.CitizenId), error);
                }
            });
        RuleFor(x => x.DateOfBirth)
            .Must(d => d >= PatientRules.MinimumDate && d <= today()).WithMessage("Ngày sinh không hợp lệ.")
            .When(x => x.DateOfBirth is not null);
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Giới tính không hợp lệ.").When(x => x.Gender is not null);
        RuleFor(x => x.PhoneNumber)
            .Matches(PatientRules.PhonePattern).WithMessage("Số điện thoại không hợp lệ (ví dụ 0912345678).")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Address)
            .MaximumLength(Patient.AddressMaxLength).WithMessage($"Địa chỉ tối đa {Patient.AddressMaxLength} ký tự.");
    }
}

public sealed class PatientLookupRequestValidator : AbstractValidator<PatientLookupRequest>
{
    public PatientLookupRequestValidator()
    {
        RuleFor(x => x.CitizenId)
            .Must(CitizenIdNumber.HasValidFormat).WithMessage("Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.");
    }
}
