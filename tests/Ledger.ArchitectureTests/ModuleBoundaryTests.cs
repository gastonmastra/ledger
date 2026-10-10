using System.Reflection;
using NetArchTest.Rules;

namespace Ledger.ArchitectureTests;

/// <summary>
/// Keeps the modules independently deployable: a module may only use the Contracts (integration
/// events) of another module, and the clean architecture layers only point inwards.
/// </summary>
public class ModuleBoundaryTests
{
    public static readonly string[] Modules = ["Identity", "Accounts", "Wallets", "Transactions", "Contacts", "Groups", "Debts", "Investments"];

    private static readonly string[] InternalLayers = ["Domain", "Application", "Infrastructure", "Presentation"];

    public static TheoryData<string> AllModules => new(Modules);

    private static Assembly Layer(string module, string layer)
        => Assembly.Load($"Ledger.Modules.{module}.{layer}");

    private static IEnumerable<Assembly> AllLayers(string module)
        => InternalLayers.Append("Contracts").Select(layer => Layer(module, layer));

    [Theory]
    [MemberData(nameof(AllModules))]
    public void A_module_only_uses_the_contracts_of_other_modules(string module)
    {
        var forbidden = Modules
            .Where(other => other != module)
            .SelectMany(other => InternalLayers.Select(layer => $"Ledger.Modules.{other}.{layer}"))
            .ToArray();

        var result = Types.InAssemblies(AllLayers(module))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        AssertSuccess(result);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Contracts_depend_on_nothing(string module)
    {
        var otherModules = Modules
            .Where(other => other != module)
            .Select(other => $"Ledger.Modules.{other}");

        var result = Types.InAssembly(Layer(module, "Contracts"))
            .ShouldNot()
            .HaveDependencyOnAny(otherModules
                .Concat(["Ledger.BuildingBlocks", "Microsoft.EntityFrameworkCore", "MassTransit", "MediatR"])
                .ToArray())
            .GetResult();

        AssertSuccess(result);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Domain_is_persistence_and_framework_free(string module)
    {
        var result = Types.InAssembly(Layer(module, "Domain"))
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "MassTransit", "MediatR", "Microsoft.AspNetCore",
                $"Ledger.Modules.{module}.Application",
                $"Ledger.Modules.{module}.Infrastructure",
                $"Ledger.Modules.{module}.Presentation")
            .GetResult();

        AssertSuccess(result);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Application_does_not_depend_on_infrastructure_or_web(string module)
    {
        var result = Types.InAssembly(Layer(module, "Application"))
            .ShouldNot()
            .HaveDependencyOnAny(
                "MassTransit", "Npgsql", "Microsoft.AspNetCore",
                $"Ledger.Modules.{module}.Infrastructure",
                $"Ledger.Modules.{module}.Presentation")
            .GetResult();

        AssertSuccess(result);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Presentation_does_not_touch_persistence(string module)
    {
        var result = Types.InAssembly(Layer(module, "Presentation"))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "MassTransit", $"Ledger.Modules.{module}.Infrastructure")
            .GetResult();

        AssertSuccess(result);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Integration_events_are_immutable_records(string module)
    {
        var mutable = Types.InAssembly(Layer(module, "Contracts"))
            .That().AreClasses().And().AreNotAbstract()
            .GetTypes()
            .Select(t => t.ReflectionType)
            .Where(t => t.GetProperties().Any(p => p.SetMethod is { } setter && !IsInitOnly(setter)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(mutable);
    }

    [Fact]
    public void Dependency_detection_works()
    {
        // Positive control: Debts asks the ledger to post entries through the Transactions contracts.
        var dependents = Types.InAssembly(Layer("Debts", "Application"))
            .That()
            .HaveDependencyOnAny("Ledger.Modules.Transactions.Contracts")
            .GetTypes();

        Assert.NotEmpty(dependents);
    }

    [Fact]
    public void Investments_asks_the_ledger_through_the_transactions_contracts()
    {
        var dependents = Types.InAssembly(Layer("Investments", "Application"))
            .That()
            .HaveDependencyOnAny("Ledger.Modules.Transactions.Contracts")
            .GetTypes();

        Assert.NotEmpty(dependents);
    }

    private static bool IsInitOnly(MethodInfo setter)
        => setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static void AssertSuccess(TestResult result)
        => Assert.True(
            result.IsSuccessful,
            "Architecture rule broken by: " + string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName)));
}
