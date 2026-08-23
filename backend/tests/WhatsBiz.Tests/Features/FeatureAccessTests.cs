using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Tests.Features;

public sealed class FeatureAccessTests
{
    [Theory]
    [InlineData(false,true,false)]
    [InlineData(true,true,true)]
    [InlineData(true,false,false)]
    public void V1ParentControlsPosEffectiveState(bool v1, bool pos, bool expected)
        => State(Evaluate(v1,pos,false,true), FeatureKeys.Pos).EffectiveEnabled.Should().Be(expected);

    [Theory]
    [InlineData(false,true,false)]
    [InlineData(true,true,true)]
    public void V2ParentControlsWhatsAppEffectiveState(bool v2, bool commerce, bool expected)
        => State(Evaluate(true,true,v2,commerce), FeatureKeys.WhatsAppCommerce).EffectiveEnabled.Should().Be(expected);

    [Fact]
    public void ParentTogglePreservesChildConfiguration()
    {
        State(Evaluate(true,true,true,true),FeatureKeys.WhatsAppCommerce).EffectiveEnabled.Should().BeTrue();
        var disabled = State(Evaluate(true,true,false,true),FeatureKeys.WhatsAppCommerce);
        disabled.ConfiguredEnabled.Should().BeTrue(); disabled.EffectiveEnabled.Should().BeFalse();
        var restored = State(Evaluate(true,true,true,true),FeatureKeys.WhatsAppCommerce);
        restored.ConfiguredEnabled.Should().BeTrue(); restored.EffectiveEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GateUsesAuthenticatedTenantAndReturnsSafe403()
    {
        var tenantA=Guid.NewGuid(); var tenantB=Guid.NewGuid();
        var service = new TenantFeatureStub(new() { [tenantA]=false, [tenantB]=true });
        var called=false; var middleware=new FeatureGateMiddleware(_ => { called=true; return Task.CompletedTask; });
        var a=Context("/api/pos"); await middleware.InvokeAsync(a,new UserStub(tenantA),service);
        a.Response.StatusCode.Should().Be(403); called.Should().BeFalse();
        a.Response.Body.Position=0; using var json=await JsonDocument.ParseAsync(a.Response.Body);
        json.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
        var b=Context("/api/pos"); await middleware.InvokeAsync(b,new UserStub(tenantB),service);
        called.Should().BeTrue(); service.Seen.Should().ContainInOrder(tenantA,tenantB);
    }

    private static DefaultHttpContext Context(string path) { var c=new DefaultHttpContext(); c.Request.Path=path; c.Response.Body=new MemoryStream(); return c; }
    private static FeatureAccessState State(IReadOnlyCollection<FeatureAccessState> states,string key) => states.Single(x=>x.FeatureKey==key);
    private static IReadOnlyCollection<FeatureAccessState> Evaluate(bool v1,bool pos,bool v2,bool commerce)
    {
        var id=Guid.NewGuid();
        FeatureEvaluationInput I(string key,string type,string? parent,string version,int order,bool configured,string[]? dependencies=null)
            => new(id,key,key,type,parent,version,order,configured,true,true,true,dependencies ?? []);
        return FeatureAccessEvaluator.Evaluate([
            I(FeatureKeys.V1,"VERSION",null,"V1",0,v1), I(FeatureKeys.Products,"MODULE",FeatureKeys.V1,"V1",10,true),
            I(FeatureKeys.Inventory,"MODULE",FeatureKeys.V1,"V1",20,true), I(FeatureKeys.Customers,"MODULE",FeatureKeys.V1,"V1",30,true),
            I(FeatureKeys.Pos,"MODULE",FeatureKeys.V1,"V1",40,pos,[FeatureKeys.Products,FeatureKeys.Inventory]),
            I(FeatureKeys.V2,"VERSION",null,"V2",0,v2,[FeatureKeys.V1]),
            I(FeatureKeys.WhatsAppCommerce,"MODULE",FeatureKeys.V2,"V2",10,commerce,[FeatureKeys.Products,FeatureKeys.Inventory,FeatureKeys.Customers])]);
    }

    private sealed class UserStub(Guid tenant):ICurrentUserService { public Guid? UserId=>Guid.NewGuid(); public Guid? TenantId=>tenant; public string? Username=>"test"; public string? Email=>null; public IReadOnlyCollection<string> Roles=>[]; public IReadOnlyCollection<string> Permissions=>[]; }
    private sealed class TenantFeatureStub(Dictionary<Guid,bool> enabled):IFeatureService
    {
        public List<Guid> Seen { get; }=[];
        public Task<bool> IsEnabledAsync(Guid tenantId,string featureKey,CancellationToken cancellationToken=default){Seen.Add(tenantId);return Task.FromResult(enabled[tenantId]);}
        public Task<IReadOnlyDictionary<string,bool>> GetEffectiveFeaturesAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId,IReadOnlyCollection<TenantFeatureUpdate> updates,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public void InvalidateTenant(Guid tenantId){} public void InvalidateAll(){}
    }
}
