using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Sammha.Application.Common;

/// <summary>CQRS markers: commands change state, queries only read.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;
public interface IQuery<out TResult> : IRequest<TResult>;

/// <summary>Logs every command and query with its duration; slow ones as warnings.</summary>
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 1500) log.LogWarning("{Request} took {Ms} ms", name, sw.ElapsedMilliseconds);
            else log.LogDebug("{Request} took {Ms} ms", name, sw.ElapsedMilliseconds);
        }
    }
}
