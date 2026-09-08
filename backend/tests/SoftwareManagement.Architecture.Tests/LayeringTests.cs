using System.Reflection;
using System.Xml.Linq;
using AwesomeAssertions;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Architecture.Tests;

/// <summary>
/// ADR-07 and ADR-10: a modular monolith is only modular while something enforces it. These
/// tests fail the build the first time a dependency points outward, which is the only moment
/// the mistake is cheap to fix.
///
/// Two different sources of truth are used deliberately. Forbidden dependencies are asserted
/// against the compiled assembly, because a compiled reference proves the code actually used
/// the type. Required dependencies are asserted against the project file, because the C#
/// compiler omits a reference to an assembly whose types are never touched, so an assembly
/// scan would report a false violation for a reference that genuinely exists.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(AuditableEntity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Slug).Assembly;

    private static readonly string BackendRoot = FindBackendRoot();

    private static List<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "SoftwareManagement.sln");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the backend root from " + AppContext.BaseDirectory);
    }

    private static List<string> ProjectReferencesOf(string projectName)
    {
        var path = Path.Combine(BackendRoot, "src", projectName, projectName + ".csproj");
        File.Exists(path).Should().BeTrue("the project file must exist at " + path);

        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    [Fact]
    public void Domain_DoesNotReferenceEntityFrameworkCore()
    {
        ReferencedNames(DomainAssembly)
            .Should().NotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal),
                "the domain must not know how it is stored");
    }

    [Fact]
    public void Domain_DoesNotReferenceAnyOtherProjectInTheSolution()
    {
        ReferencedNames(DomainAssembly)
            .Should().NotContain(name => name.StartsWith("SoftwareManagement.", StringComparison.Ordinal),
                "dependencies point inward and the domain is the innermost layer");
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructure()
    {
        ProjectReferencesOf("SoftwareManagement.Application")
            .Should().NotContain("SoftwareManagement.Infrastructure",
                "the application layer declares interfaces and infrastructure implements them");
    }

    [Fact]
    public void Application_DoesNotReferenceAspNetCoreMvc()
    {
        ReferencedNames(ApplicationAssembly)
            .Should().NotContain(name => name.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal),
                "a use case must be callable without an HTTP request");
    }

    [Fact]
    public void Application_ReferencesDomain_SoDependenciesPointInward()
    {
        ProjectReferencesOf("SoftwareManagement.Application")
            .Should().Contain("SoftwareManagement.Domain");
    }

    [Fact]
    public void Infrastructure_ReferencesApplication_AndNotTheApiLayer()
    {
        var references = ProjectReferencesOf("SoftwareManagement.Infrastructure");

        references.Should().Contain("SoftwareManagement.Application");
        references.Should().NotContain("SoftwareManagement.Api", "infrastructure must not depend on the web layer");
    }

    [Fact]
    public void Api_IsTheOnlyProjectAllowedToDependOnInfrastructure()
    {
        ProjectReferencesOf("SoftwareManagement.Api")
            .Should().Contain("SoftwareManagement.Infrastructure");

        ProjectReferencesOf("SoftwareManagement.Domain")
            .Should().BeEmpty("the domain project has no project references at all");
    }
}
