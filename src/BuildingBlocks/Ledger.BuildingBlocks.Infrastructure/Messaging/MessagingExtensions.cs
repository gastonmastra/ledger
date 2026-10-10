using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>What a module brings to the message bus: its queue, its DbContext and its consumers.</summary>
public sealed record ModuleMessagingRegistration(
    string Module,
    IReadOnlyList<Type> Consumers,
    Action<IBusRegistrationConfigurator> Register,
    Action<IReceiveEndpointConfigurator, IBusRegistrationContext> ConfigureEndpoint);

public static class MessagingExtensions
{
    public const string ConnectionStringName = "RabbitMq";

    /// <summary>
    /// Registers the messaging of one module:
    /// <list type="bullet">
    /// <item>a transactional outbox (events are published only if the business change commits);</item>
    /// <item>one RabbitMQ queue named after the module with all its consumers — the unit that can be
    /// moved to its own service later;</item>
    /// <item>an inbox in the module DbContext (see <see cref="ModuleConsumer{TMessage,TDbContext}"/>), so a
    /// redelivered message is processed once.</item>
    /// </list>
    /// Call <see cref="AddLedgerMessageBus"/> once, after every module.
    /// </summary>
    public static IServiceCollection AddModuleMessaging<TDbContext>(
        this IServiceCollection services,
        string module,
        params Type[] consumers)
        where TDbContext : DbContext
    {
        services.AddHostedService<OutboxProcessor<TDbContext>>();

        services.AddSingleton(new ModuleMessagingRegistration(
            module,
            consumers,
            bus =>
            {
                foreach (var consumer in consumers)
                    bus.AddConsumer(consumer);
            },
            (endpoint, context) =>
            {
                // A message can depend on another module's event that has not been processed yet
                // (eventual consistency, e.g. a wallet account before the user's system accounts):
                // retry with exponential backoff (about three minutes in total) before dead-lettering it.
                endpoint.UseMessageRetry(retry => retry.Exponential(
                    retryLimit: 12,
                    minInterval: TimeSpan.FromMilliseconds(100),
                    maxInterval: TimeSpan.FromSeconds(30),
                    intervalDelta: TimeSpan.FromMilliseconds(200)));

                foreach (var consumer in consumers)
                    endpoint.ConfigureConsumer(context, consumer);
            }));

        return services;
    }

    /// <summary>Registers the RabbitMQ bus with one receive endpoint per module that has consumers.</summary>
    public static IServiceCollection AddLedgerMessageBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var modules = services
            .Where(d => d.ServiceType == typeof(ModuleMessagingRegistration))
            .Select(d => (ModuleMessagingRegistration)d.ImplementationInstance!)
            .ToList();

        services.AddMassTransit(bus =>
        {
            foreach (var module in modules)
                module.Register(bus);

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(configuration.GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException(
                        $"Connection string '{ConnectionStringName}' is not configured.")));

                foreach (var module in modules.Where(m => m.Consumers.Count != 0))
                {
                    rabbit.ReceiveEndpoint($"ledger-{module.Module}", endpoint =>
                        module.ConfigureEndpoint(endpoint, context));
                }
            });
        });

        return services;
    }
}
