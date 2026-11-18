using Consent.Domain.Auditing;
using Consent.Domain.Common;

namespace Consent.Domain.Tests;

public sealed class AuditLogTests
{
    [Fact]
    public void Create_CopiesActor_AndTruncatesLongValues()
    {
        var actor = new AuditActor(Guid.NewGuid(), "admin", "10.0.0.1", new string('x', 1000), "trace-1");

        var log = AuditLog.Create(DateTimeOffset.UnixEpoch, AuditActions.Update, actor, "User", "42", "{}");

        Assert.Equal(actor.UserId, log.UserId);
        Assert.Equal("admin", log.UserName);
        Assert.Equal(AuditLog.UserAgentMaxLength, log.UserAgent!.Length);
        Assert.Equal("trace-1", log.CorrelationId);
    }

    [Fact]
    public void Create_WithoutAction_Throws()
    {
        Assert.Throws<DomainException>(() => AuditLog.Create(DateTimeOffset.UnixEpoch, " ", AuditActor.System));
    }
}
