using System.Reflection;

namespace TimingApp.Architecture.Tests;

/// <summary>Dependency Rule, project boundaries and the OpenCvSharp and Vortice boundaries.</summary>
public sealed class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(Domain.SharedKernel.Result).Assembly;
    private static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Camera = typeof(Infrastructure.Camera.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IReadOnlyList<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    [Fact]
    public void Domain_ReferencesOnlyTheBaseClassLibrary()
    {
        var forbidden = References(Domain).Where(r => !r.StartsWith("System", StringComparison.Ordinal) && r != "netstandard").ToList();

        Assert.Empty(forbidden);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("OpenCvSharp")]
    [InlineData("TimingApp.Infrastructure")]
    [InlineData("TimingApp.Api")]
    [InlineData("System.Net.Sockets")]
    public void Application_DoesNotDependOnInfrastructureConcerns(string prefix) =>
        Assert.DoesNotContain(References(Application), r => r.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void Application_ReferencesDomainOnlyAmongOwnProjects() =>
        Assert.Equal(["TimingApp.Domain"], References(Application).Where(r => r.StartsWith("TimingApp", StringComparison.Ordinal)));

    [Fact]
    public void InfrastructureProjects_DoNotReferenceEachOtherOrTheApi()
    {
        Assert.DoesNotContain(References(Infrastructure), r => r is "TimingApp.Infrastructure.Camera" or "TimingApp.Api");
        Assert.DoesNotContain(References(Camera), r => r is "TimingApp.Infrastructure" or "TimingApp.Api");
    }

    [Fact]
    public void OpenCvSharp_IsOnlyReferencedByTheCameraInfrastructure()
    {
        var offenders = new[] { Domain, Application, Infrastructure, Api }
            .Where(a => References(a).Any(r => r.StartsWith("OpenCvSharp", StringComparison.Ordinal)))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.Empty(offenders);
        Assert.Contains(References(Camera), r => r.StartsWith("OpenCvSharp", StringComparison.Ordinal));
    }

    [Fact]
    public void Vortice_IsOnlyReferencedByTheCameraInfrastructure()
    {
        var offenders = new[] { Domain, Application, Infrastructure, Api }
            .Where(a => References(a).Any(r => r.StartsWith("Vortice", StringComparison.Ordinal) || r.StartsWith("SharpGen", StringComparison.Ordinal)))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.Empty(offenders);
        Assert.Contains(References(Camera), r => r.StartsWith("Vortice.MediaFoundation", StringComparison.Ordinal));
    }

    [Fact]
    public void CameraInfrastructure_DoesNotExposeOpenCvTypesInPublicSignatures()
    {
        var offenders = Camera.GetExportedTypes()
            .Where(t => t.Namespace != "TimingApp.Infrastructure.Camera.Sources")
            .SelectMany(t => t.GetMethods().Cast<MethodBase>().Concat(t.GetConstructors())
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m is MethodInfo i ? i.ReturnType : typeof(void)))
                .Where(p => p.Namespace?.StartsWith("OpenCvSharp", StringComparison.Ordinal) == true)
                .Select(p => $"{t.Name} → {p.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("TimingApp.Domain")]
    [InlineData("TimingApp.Application")]
    public void Types_BelongToABoundedContextOrTheSharedKernel(string root)
    {
        var assembly = root == "TimingApp.Domain" ? Domain : Application;
        var allowed = new[] { $"{root}.FinishRecording", $"{root}.SharedKernel", root };

        var offenders = assembly.GetTypes()
            .Where(t => t.Namespace is not null && !t.Namespace.StartsWith("System", StringComparison.Ordinal) && !t.Namespace.StartsWith("Microsoft", StringComparison.Ordinal))
            .Where(t => !allowed.Contains(t.Namespace))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }
}
