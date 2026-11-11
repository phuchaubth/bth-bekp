using Consent.Domain.Identity;
using FluentValidation;

namespace Consent.Application.Users;

internal static class UserProfileRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IUserProfileRequest
    {
        validator.RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Vui lòng nhập họ tên.")
            .MaximumLength(User.FullNameMaxLength).WithMessage($"Họ tên tối đa {User.FullNameMaxLength} ký tự.");
        validator.RuleFor(x => x.Email)
            .MaximumLength(User.EmailMaxLength).WithMessage($"Email tối đa {User.EmailMaxLength} ký tự.")
            .EmailAddress().WithMessage("Email không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        validator.RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[0-9 .-]{8,20}$").WithMessage("Số điện thoại không hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        validator.RuleFor(x => x.Title)
            .MaximumLength(User.TitleMaxLength).WithMessage($"Chức danh tối đa {User.TitleMaxLength} ký tự.");
        validator.RuleFor(x => x.SmartCaUserId)
            .MaximumLength(User.SmartCaUserIdMaxLength).WithMessage($"Mã SmartCA tối đa {User.SmartCaUserIdMaxLength} ký tự.")
            .Matches("^[A-Za-z0-9]+$").WithMessage("Mã SmartCA chỉ gồm chữ không dấu và số.")
            .When(x => !string.IsNullOrWhiteSpace(x.SmartCaUserId));
        validator.RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("Vui lòng chọn ít nhất một vai trò.");
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Vui lòng nhập tên đăng nhập.")
            .Length(User.UserNameMinLength, User.UserNameMaxLength)
                .WithMessage($"Tên đăng nhập phải có từ {User.UserNameMinLength} đến {User.UserNameMaxLength} ký tự.")
            .Matches("^[A-Za-z0-9._-]+$").WithMessage("Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới.");
        UserProfileRules.Apply(this);
        RuleFor(x => x.Password).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password, context.InstanceToValidate.UserName))
            {
                context.AddFailure(error);
            }
        });
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator() => UserProfileRules.Apply(this);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password))
            {
                context.AddFailure(error);
            }
        });
    }
}
