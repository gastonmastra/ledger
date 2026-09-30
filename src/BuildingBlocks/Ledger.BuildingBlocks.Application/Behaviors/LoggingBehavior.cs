using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Ledger.BuildingBlocks.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            logger.LogDebug("Handled {Request} in {Elapsed} ms", name, stopwatch.ElapsedMilliseconds);
        }
    }
}
