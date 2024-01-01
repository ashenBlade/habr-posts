using System.Linq.Expressions;

namespace CinemaBooking.Infrastructure.Specifications;

public class AndSpecification<T>: Specification<T>
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;

    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        _left = left;
        _right = right;
        _lazy = new Lazy<Expression<Func<T, bool>>>(() => System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.AndAlso(_left.Expression.Body, _right.Expression.Body),
            _left.Expression.Parameters.Single()));
    }

    private readonly Lazy<Expression<Func<T, bool>>> _lazy;
    public override Expression<Func<T, bool>> Expression => _lazy.Value;
}