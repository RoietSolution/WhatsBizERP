using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Notifications;
using WhatsBiz.Infrastructure.Storefront;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace WhatsBiz.Tests.Features;

public sealed class StorefrontEntitlementTests
{
    [Theory]
    [InlineData(FeatureKeys.StorefrontScheduledSelling)]
    [InlineData(FeatureKeys.StorefrontPopupCampaigns)]
    [InlineData(FeatureKeys.StorefrontNotifications)]
    public void EffectiveAccessRequiresGlobalPlanTenantParentAndDependency(string key)
    {
        FeatureAccessState Evaluate(bool global=true,bool plan=true,bool tenant=true,bool parent=true,bool dependency=true) => FeatureAccessEvaluator.Evaluate([
            new(Guid.NewGuid(),FeatureKeys.V1,"V1","VERSION",null,"V1",1,parent,true,true,true,[]),
            new(Guid.NewGuid(),FeatureKeys.Administration,"Admin","MODULE",FeatureKeys.V1,"V1",2,dependency,true,true,true,[]),
            new(Guid.NewGuid(),key,key,"MODULE",FeatureKeys.V1,"V1",3,tenant,plan,global,true,[FeatureKeys.Administration])]).Single(x=>x.FeatureKey==key);
        Evaluate().EffectiveEnabled.Should().BeTrue();
        Evaluate(global:false).DisabledReason.Should().Be("GLOBAL_FEATURE_DISABLED");
        Evaluate(plan:false).DisabledReason.Should().Be("SUBSCRIPTION_NOT_ENTITLED");
        Evaluate(tenant:false).DisabledReason.Should().Be("TENANT_CONFIGURATION_DISABLED");
        Evaluate(parent:false).DisabledReason.Should().Be("PARENT_VERSION_DISABLED");
        Evaluate(dependency:false).DisabledReason.Should().StartWith("DEPENDENCY_DISABLED:");
        FeatureKeys.RequiresFreshEvaluation(key.ToLowerInvariant()).Should().BeTrue();
    }

    [Fact]
    public async Task BackendUsesCurrentTenantEntitlementNotAnOldJwtOrAnotherTenant()
    {
        var tenant=Guid.NewGuid();var flags=new Flags();var handler=new FeatureAuthorizationHandler(new User(tenant),flags);
        var requirement=new FeatureRequirement(FeatureKeys.StorefrontPopupCampaigns);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("STOREFRONT_POPUP_CAMPAIGNS","true")],"Fixture"));
        var before=new AuthorizationHandlerContext([requirement],principal,new{TenantId=Guid.NewGuid()});await handler.HandleAsync(before);
        before.HasSucceeded.Should().BeFalse();flags.LastTenant.Should().Be(tenant);
        flags.Enabled=true;var enabled=new AuthorizationHandlerContext([requirement],principal,null);await handler.HandleAsync(enabled);enabled.HasSucceeded.Should().BeTrue();
        flags.Enabled=false;var disabled=new AuthorizationHandlerContext([requirement],principal,null);await handler.HandleAsync(disabled);disabled.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task DisabledSchedulesAreDormantAndManagementIsRejectedWithoutReadingOrDeletingData()
    {
        var flags=new Flags();var tenant=Guid.NewGuid();var service=new StorefrontAvailabilityService(new ConfigurationBuilder().Build(),features:flags);
        (await service.GetProductAvailabilityAsync(tenant,[Guid.NewGuid()],default)).Should().BeEmpty();
        (await service.GetAnnouncementsAsync(tenant,default)).Should().BeEmpty();
        (await service.GetCampaignWindowsAsync(tenant,default)).Should().BeEmpty();
        await service.EnsureOrderEligibleAsync(tenant,[Guid.NewGuid()],default);
        await ((Func<Task>)(async()=>await service.GetSchedulesAsync(tenant,default))).Should().ThrowAsync<BusinessRuleException>();
        await ((Func<Task>)(async()=>await service.SaveScheduleAsync(tenant,null,null!,default))).Should().ThrowAsync<BusinessRuleException>();
        await ((Func<Task>)(()=>service.DeleteScheduleAsync(tenant,Guid.NewGuid(),default))).Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DisabledCampaignFeedAndImagesAreEmptyAndCannotReachPersistence()
    {
        var tenant=Guid.NewGuid();var availability=DispatchProxy.Create<IStorefrontAvailabilityService, Availability>();
        ((Availability)(object)availability).Tenant=tenant;
        var service=new StorefrontCampaignService(new ConfigurationBuilder().Build(),null!,availability,null!,null!,features:new Flags());
        (await service.FeedAsync("store",default))!.Campaigns.Should().BeEmpty();
        (await service.ImageAsync("store",Guid.NewGuid(),Guid.NewGuid(),default)).Should().BeNull();
        await ((Func<Task>)(async()=>await service.ListAsync(tenant,default))).Should().ThrowAsync<BusinessRuleException>();
        await ((Func<Task>)(async()=>await service.SaveAsync(tenant,null,null!,default))).Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task OptionalRetailerAlertsDoNotQueueWhenDisabled()
    {
        var service=new StorefrontAutomatedNotificationService(new ConfigurationBuilder().Build(),new Flags());
        await service.QueueOrderEmailAsync(Guid.NewGuid(),Guid.NewGuid(),default); // Empty configuration proves no SQL or send executes.
    }

    [Fact]
    public async Task OptionalWorkerPausesAlreadyQueuedMessagesWithoutTouchingSmtpOrMeta()
    {
        using var services=new ServiceCollection().AddSingleton<IFeatureService>(new Flags()).BuildServiceProvider();
        var worker=new StorefrontAutomatedNotificationWorker(new ConfigurationBuilder().Build(),Options.Create(new StorefrontFeedbackNotificationOptions()),null!,null!,
            NullLogger<StorefrontAutomatedNotificationWorker>.Instance,services.GetRequiredService<IServiceScopeFactory>());
        var itemType=typeof(StorefrontAutomatedNotificationWorker).GetNestedType("WorkItem",BindingFlags.NonPublic)!;
        var item=Activator.CreateInstance(itemType,Guid.NewGuid(),Guid.NewGuid(),"PRODUCT_RESTOCK","WHATSAPP",Guid.NewGuid(),null,null,1);
        var task=(Task)typeof(StorefrontAutomatedNotificationWorker).GetMethod("Deliver",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(worker,[item,CancellationToken.None])!;
        await task;var result=task.GetType().GetProperty("Result")!.GetValue(task)!;
        result.GetType().GetProperty("Status")!.GetValue(result).Should().Be("PENDING");
        result.GetType().GetProperty("Category")!.GetValue(result).Should().Be("FEATURE_DISABLED");
    }

    [Theory]
    [InlineData(nameof(StorefrontAdministrationController.GetAvailabilitySchedules),FeatureKeys.StorefrontScheduledSelling)]
    [InlineData(nameof(StorefrontAdministrationController.CreateAvailabilitySchedule),FeatureKeys.StorefrontScheduledSelling)]
    [InlineData(nameof(StorefrontAdministrationController.UpdateAvailabilitySchedule),FeatureKeys.StorefrontScheduledSelling)]
    [InlineData(nameof(StorefrontAdministrationController.DeleteAvailabilitySchedule),FeatureKeys.StorefrontScheduledSelling)]
    [InlineData(nameof(StorefrontAdministrationController.UpdateAutomatedNotificationSettings),FeatureKeys.StorefrontNotifications)]
    [InlineData(nameof(StorefrontAdministrationController.GetNotificationSettings),FeatureKeys.StorefrontNotifications)]
    [InlineData(nameof(StorefrontAdministrationController.TestNotificationEmail),FeatureKeys.StorefrontNotifications)]
    public void ManagementActionsUseExistingFeatureAuthorization(string action,string key) =>
        typeof(StorefrontAdministrationController).GetMethod(action)!.GetCustomAttribute<RequireFeatureAttribute>()!.Policy.Should().Be(PermissionPolicyProvider.FeaturePrefix+key);

    public class Availability : DispatchProxy
    {
        public Guid Tenant {get;set;}
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=> targetMethod!.Name==nameof(IStorefrontAvailabilityService.ResolveTenantIdAsync)
            ? Task.FromResult<Guid?>(Tenant) : throw new InvalidOperationException("Disabled campaigns must not read schedule data.");
    }
    internal sealed class Flags : IFeatureService
    {
        public bool Enabled {get;set;}public Guid? LastTenant {get;private set;}
        public Task<bool> IsEnabledAsync(Guid tenantId,string featureKey,CancellationToken cancellationToken=default){LastTenant=tenantId;return Task.FromResult(Enabled);}
        public Task<IReadOnlyDictionary<string,bool>> GetEffectiveFeaturesAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId,IReadOnlyCollection<TenantFeatureUpdate> updates,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantDisplayNameAsync(Guid tenantId,string? displayName,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public void InvalidateTenant(Guid tenantId){} public void InvalidateAll(){}
    }
    private sealed class User(Guid tenant):ICurrentUserService
    {public Guid? UserId=>Guid.NewGuid();public Guid? TenantId=>tenant;public string? Username=>null;public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[];}
}
