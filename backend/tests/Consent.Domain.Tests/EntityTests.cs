using Consent.Domain.Common;

namespace Consent.Domain.Tests;

public sealed class EntityTests
{
    private sealed class SampleEntity(Guid id) : Entity(id);

    private sealed class OtherEntity(Guid id) : Entity(id);

    [Fact]
    public void Constructor_WithEmptyId_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => new SampleEntity(Guid.Empty));
    }

    [Fact]
    public void Entities_WithSameTypeAndId_AreEqual()
    {
        var id = Guid.NewGuid();

        var first = new SampleEntity(id);
        var second = new SampleEntity(id);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_WithDifferentIds_AreNotEqual()
    {
        Assert.NotEqual(new SampleEntity(Guid.NewGuid()), new SampleEntity(Guid.NewGuid()));
    }

    [Fact]
    public void Entities_OfDifferentTypes_WithSameId_AreNotEqual()
    {
        var id = Guid.NewGuid();

        Entity sample = new SampleEntity(id);
        Entity other = new OtherEntity(id);

        Assert.False(sample.Equals(other));
        Assert.True(sample != other);
    }
}
