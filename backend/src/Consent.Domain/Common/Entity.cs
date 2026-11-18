namespace Consent.Domain.Common;

/// <summary>Thực thể có khóa chính UUID. Hai thực thể bằng nhau khi cùng kiểu và cùng Id.</summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Id của thực thể không được rỗng.");
        }

        Id = id;
    }

    public Guid Id { get; }

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
