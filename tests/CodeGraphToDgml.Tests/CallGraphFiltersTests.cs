using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Tests;

[TestClass]
public sealed class CallGraphFiltersTests
{
    // ControllerBase lives in a separately-compiled, metadata-only "external" assembly, mirroring
    // how ASP.NET Core's real ControllerBase (from a NuGet package) looks to the analyzed solution.
    private const string ExternalControllerBaseSource = @"
namespace Microsoft.AspNetCore.Mvc
{
    public class ActionResult { }

    public class ControllerBase
    {
        public ActionResult Ok(object value) => null;
        public ActionResult BadRequest(object value) => null;
        public ActionResult NotWhitelisted() => null;
    }
}
";

    private const string ControllerSource = @"
namespace App
{
    public class MyController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public void Handle()
        {
            Ok(1);
            BadRequest(2);
            NotWhitelisted();
        }
    }
}
";

    private static async Task<(Solution Solution, INamedTypeSymbol ControllerBaseType)> CreateFixtureAsync()
    {
        var solution = await RoslynTestFixture.CreateSolutionWithExternalLibraryAsync(ExternalControllerBaseSource, ControllerSource);
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        var controllerBaseType = compilation!.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase")!;
        return (solution, controllerBaseType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WellKnownFrameworkMethods
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task IsWellKnownOutcomeMethod_ControllerBaseOk_ReturnsTrue()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var okMethod = controllerBaseType.GetMembers("Ok").OfType<IMethodSymbol>().Single();

        Assert.IsTrue(WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(okMethod));
    }

    [TestMethod]
    public async Task IsWellKnownOutcomeMethod_ControllerBaseBadRequest_ReturnsTrue()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var method = controllerBaseType.GetMembers("BadRequest").OfType<IMethodSymbol>().Single();

        Assert.IsTrue(WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(method));
    }

    [TestMethod]
    public async Task IsWellKnownOutcomeMethod_NonWhitelistedMethod_ReturnsFalse()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var method = controllerBaseType.GetMembers("NotWhitelisted").OfType<IMethodSymbol>().Single();

        Assert.IsFalse(WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(method));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IsAllowed — whitelist carve-out over external-symbol filtering
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task IsAllowed_WellKnownOutcomeMethod_AllowedEvenWithExternalSymbolsExcluded()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var okMethod = controllerBaseType.GetMembers("Ok").OfType<IMethodSymbol>().Single();

        Assert.IsFalse(okMethod.Locations.Any(l => l.IsInSource),
            "Sanity check: Ok() must genuinely be an external (metadata-only) symbol for this test to be meaningful.");

        var options = new TraversalOptions { IncludeExternalSymbols = false };

        Assert.IsTrue(CallGraphFilters.IsAllowed(okMethod, options),
            "Ok() should be allowed through even though ControllerBase is external to this compilation, " +
            "because it's whitelisted as a well-known outcome method.");
    }

    [TestMethod]
    public async Task IsAllowed_NonWhitelistedExternalMethod_ExcludedWhenExternalSymbolsDisabled()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var method = controllerBaseType.GetMembers("NotWhitelisted").OfType<IMethodSymbol>().Single();

        var options = new TraversalOptions { IncludeExternalSymbols = false };

        Assert.IsFalse(CallGraphFilters.IsAllowed(method, options),
            "A non-whitelisted external method should still be excluded by default, same as before item 4.");
    }

    [TestMethod]
    public async Task IsAllowed_NonWhitelistedExternalMethod_IncludedWhenExternalSymbolsEnabled()
    {
        var (_, controllerBaseType) = await CreateFixtureAsync();
        var method = controllerBaseType.GetMembers("NotWhitelisted").OfType<IMethodSymbol>().Single();

        var options = new TraversalOptions { IncludeExternalSymbols = true };

        Assert.IsTrue(CallGraphFilters.IsAllowed(method, options),
            "The global IncludeExternalSymbols toggle should still work independently of the whitelist.");
    }
}
