using MediatR;

namespace Ledger.BuildingBlocks.Application;

/// <summary>A use case that changes state.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A use case that changes state and returns nothing.</summary>
public interface ICommand : IRequest;

/// <summary>A use case that only reads state.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

public interface ICommandHandler<in TCommand>
    : IRequestHandler<TCommand>
    where TCommand : ICommand;

public interface IQueryHandler<in TQuery, TResponse>
    : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
