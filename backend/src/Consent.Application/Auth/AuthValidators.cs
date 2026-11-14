using Consent.Domain.Identity;
using FluentValidation;

namespace Consent.Application.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Vui lòng nhập tên đăng nhập.")
            .MaximumLength(User.UserNameMaxLength).WithMessage("Tên đăng nhập quá dài.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Vui lòng nhập mật khẩu.")
            .MaximumLength(PasswordPolicy.MaxLength).WithMessage("Mật khẩu quá dài.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Vui lòng nhập mật khẩu hiện tại.");
        RuleFor(x => x.NewPassword)
            .Custom((password, context) =>
            {
                foreach (var error in PasswordPolicy.Validate(password))
                {
                    context.AddFailure(error);
                }
            })
            .NotEqual(x => x.CurrentPassword).WithMessage("Mật khẩu mới phải khác mật khẩu hiện tại.");
    }
}
