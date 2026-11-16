using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Lượt xác nhận phía bệnh viện của phiếu chờ ký mà người bệnh đã ký (quyết định 13/11/2026, nhiều bước xác nhận theo vai
/// trò): các bước trung gian của mẫu (<c>signatures.steps</c>) được xác nhận lần lượt theo thứ tự trong mẫu, xong hết mới
/// đến xác nhận cuối (vai trò cấu hình trên mẫu); một tài khoản chỉ xác nhận một phần trên một phiếu. Dùng chung cho nút
/// xác nhận trên phiếu (<see cref="ConsentFormService.BuildActions"/>) và danh sách "Chờ tôi xác nhận" ở trang tổng quan,
/// để hai nơi luôn khớp nhau.
/// </summary>
internal static class ConfirmationTurn
{
    /// <summary>Bước kế tiếp: bước đầu tiên theo thứ tự của mẫu chưa được xác nhận; <c>null</c> khi mọi bước đã xác nhận.</summary>
    public static TemplateConfirmationStep? NextStep(
        IReadOnlyList<TemplateConfirmationStep> steps, IEnumerable<string?> confirmedStepKeys)
    {
        var confirmed = confirmedStepKeys.ToHashSet(StringComparer.Ordinal);
        return steps.FirstOrDefault(step => !confirmed.Contains(step.Key));
    }

    /// <summary>
    /// Người dùng làm được ngay gì với phiếu: xác nhận bước kế tiếp khi bước đó thuộc vai trò của mình (<c>CanConfirmStep</c>),
    /// hoặc xác nhận cuối khi mọi bước đã xong và mình có vai trò xác nhận cuối (<c>CanConfirm</c>). Cả hai cần phiếu đang chờ
    /// phía bệnh viện xác nhận (<paramref name="awaitingConfirmation"/>: chờ ký và người bệnh đã ký), quyền ký phiếu, và người
    /// dùng chưa xác nhận bước nào của phiếu (<paramref name="confirmedAStep"/>).
    /// </summary>
    /// <param name="nextStepRoleCode">Vai trò của bước kế tiếp (<see cref="NextStep"/>); <c>null</c> khi mọi bước đã xác nhận.</param>
    public static (bool CanConfirmStep, bool CanConfirm) For(
        ICurrentUser user, bool awaitingConfirmation, string? nextStepRoleCode, string finalRoleCode, bool confirmedAStep)
    {
        if (!awaitingConfirmation || confirmedAStep || !user.HasPermission(Permissions.ConsentsSign))
        {
            return (false, false);
        }

        return nextStepRoleCode is { } stepRoleCode
            ? (user.IsInRole(stepRoleCode), false)
            : (false, user.IsInRole(finalRoleCode));
    }
}
