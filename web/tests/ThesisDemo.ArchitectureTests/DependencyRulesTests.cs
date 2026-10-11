using NetArchTest.Rules;
using ThesisDemo.Domain.Library;

namespace ThesisDemo.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_has_no_application_infrastructure_or_api_dependency() =>
        Assert.True(Types.InAssembly(typeof(Collection).Assembly).ShouldNot()
            .HaveDependencyOnAny("ThesisDemo.Application", "ThesisDemo.Infrastructure", "ThesisDemo.Api").GetResult().IsSuccessful);

    [Fact]
    public void Application_has_no_infrastructure_or_api_dependency() =>
        Assert.True(Types.InAssembly(typeof(Application.Artifacts.Artifact).Assembly).ShouldNot()
            .HaveDependencyOnAny("ThesisDemo.Infrastructure", "ThesisDemo.Api").GetResult().IsSuccessful);

    [Fact]
    public void Forbidden_frameworks_are_absent()
    {
        var assemblies = new[] { typeof(Collection).Assembly, typeof(Application.Artifacts.Artifact).Assembly,
            typeof(Infrastructure.Persistence.ThesisDemoDbContext).Assembly, typeof(Api.Controllers.SystemController).Assembly };
        foreach (var assembly in assemblies)
            Assert.True(Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("MediatR", "AutoMapper", "MassTransit", "RabbitMQ.Client").GetResult().IsSuccessful);
    }
}
