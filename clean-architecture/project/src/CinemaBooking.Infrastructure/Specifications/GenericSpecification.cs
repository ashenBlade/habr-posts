using System.Linq.Expressions;

namespace CinemaBooking.Infrastructure.Specifications;

public class GenericSpecification<T>: Specification<T>
{
    private readonly Expression<Func<T, bool>> _expression;

    public GenericSpecification(Expression<Func<T, bool>> expression)
    {
        _expression = expression;
    }

    public override Expression<Func<T, bool>> Expression => _expression;
}