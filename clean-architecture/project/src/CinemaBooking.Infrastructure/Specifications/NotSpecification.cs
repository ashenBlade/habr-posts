using System.Linq.Expressions;

namespace CinemaBooking.Infrastructure.Specifications;

public class NotSpecification<T>: Specification<T>
{
    private readonly Specification<T> _spec;

    public NotSpecification(Specification<T> spec)
    {
        _spec = spec;
        _lazy = new Lazy<Expression<Func<T, bool>>>(() => System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.Not(_spec.Expression.Body), _spec.Expression.Parameters.Single()));
    }

    private readonly Lazy<Expression<Func<T, bool>>> _lazy;
    public override Expression<Func<T, bool>> Expression => _lazy.Value;
}