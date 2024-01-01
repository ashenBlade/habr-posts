using System.ComponentModel.Design.Serialization;
using System.Linq.Expressions;

namespace CinemaBooking.Infrastructure.Specifications;

public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> Expression { get; }

    public Specification<T> And(Specification<T> other)
    {
        return new AndSpecification<T>(this, other);
    }

    public Specification<T> Or(Specification<T> other)
    {
        return new OrSpecification<T>(this, other);
    }

    public Specification<T> Not()
    {
        return new NotSpecification<T>(this);
    }

    public static implicit operator Expression<Func<T, bool>>(Specification<T> spec) => spec.Expression;
}