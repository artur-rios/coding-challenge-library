using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Library.Testing.Mock;

/// <summary>
///     An <see cref="IQueryable{T}" /> over an in-memory list that EF Core's async operators can be
///     composed on.
/// </summary>
/// <remarks>
///     The handlers call <c>FirstOrDefaultAsync</c>, <c>CountAsync</c> and <c>ToListAsync</c> on the
///     queryables their repositories hand out. Those extensions demand an
///     <see cref="IAsyncQueryProvider" /> and throw against a plain LINQ-to-objects queryable, so a
///     fake repository that returned <c>list.AsQueryable()</c> would fail every test for reasons that
///     have nothing to do with the code under test. This is the smallest shim that makes them work.
/// </remarks>
public class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new TestAsyncEnumerable<TEntity>(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new TestAsyncEnumerable<TElement>(expression);

    public object? Execute(Expression expression) => inner.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        // TResult is Task<T> for every async operator; the synchronous result is computed and wrapped.
        var expectedResultType = typeof(TResult).GetGenericArguments()[0];

        var executionResult = typeof(IQueryProvider)
            .GetMethods()
            .First(method => method.Name == nameof(IQueryProvider.Execute) && method.IsGenericMethod)
            .MakeGenericMethod(expectedResultType)
            .Invoke(this, [expression]);

        return (TResult)typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(expectedResultType)
            .Invoke(null, [executionResult])!;
    }
}

/// <summary>An enumerable that is both queryable and async-enumerable.</summary>
public class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable)
    {
    }

    public TestAsyncEnumerable(Expression expression) : base(expression)
    {
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

/// <summary>An async enumerator over a synchronous one.</summary>
public class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;

    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

    public ValueTask DisposeAsync()
    {
        inner.Dispose();

        return ValueTask.CompletedTask;
    }
}
