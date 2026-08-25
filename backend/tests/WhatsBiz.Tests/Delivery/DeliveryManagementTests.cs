using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.SharedKernel;
#pragma warning disable CA1707

namespace WhatsBiz.Tests.Delivery;

public sealed class DeliveryManagementTests
{
    private static readonly Guid Manual=Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Default=Guid.Parse("22222222-2222-2222-2222-222222222222");
    [Fact] public void Explicit_agent_has_highest_priority()=>DeliveryAssignmentPolicy.Resolve(Manual,true,Default,true,true).Should().Be(new DeliveryAssignmentDecision(Manual,"MANUAL"));
    [Fact] public void Valid_default_is_used_without_manual_agent()=>DeliveryAssignmentPolicy.Resolve(null,true,Default,true,true).Should().Be(new DeliveryAssignmentDecision(Default,"DEFAULT"));
    [Fact] public void Missing_default_is_unassigned()=>DeliveryAssignmentPolicy.Resolve(null,true,null,false,false).Should().Be(new DeliveryAssignmentDecision(null,null));
    [Fact] public void Disabled_automatic_assignment_is_unassigned()=>DeliveryAssignmentPolicy.Resolve(null,false,Default,true,true).AgentId.Should().BeNull();
    [Fact] public void Inactive_default_is_unassigned()=>DeliveryAssignmentPolicy.Resolve(null,true,Default,false,true).AgentId.Should().BeNull();
    [Fact] public void Unavailable_default_is_unassigned()=>DeliveryAssignmentPolicy.Resolve(null,true,Default,true,false).AgentId.Should().BeNull();
    [Fact] public void Delivery_agent_permissions_are_restricted()
    { var allowed=new[]{Permissions.Delivery.View,Permissions.Delivery.UpdateStatus,Permissions.Delivery.Confirm,Permissions.Delivery.RecordCod};allowed.Should().NotContain(Permissions.Delivery.Manage);allowed.Should().NotContain(Permissions.Delivery.Settings);allowed.Should().NotContain(Permissions.Finance.LedgerView); }
    [Theory]
    [InlineData(typeof(DeliveriesController))][InlineData(typeof(MyDeliveryController))][InlineData(typeof(DeliveryAgentsController))][InlineData(typeof(DeliverySettingsController))]
    public void Delivery_controllers_require_core_delivery_feature(Type controller)
    { controller.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Select(x=>x.Policy).Should().Contain(PermissionPolicyProvider.FeaturePrefix+FeatureKeys.DeliveryManagement); }
    [Fact] public void Failure_reasons_include_controlled_other_option()=>DeliveryFailureReasons.All.Should().Contain("Other");
    [Fact] public void Lifecycle_defines_delivery_separately_from_sales()=>new[]{DeliveryStatuses.Unassigned,DeliveryStatuses.Assigned,DeliveryStatuses.Ready,DeliveryStatuses.PickedUp,DeliveryStatuses.OutForDelivery,DeliveryStatuses.Delivered,DeliveryStatuses.Failed,DeliveryStatuses.Rescheduled,DeliveryStatuses.Cancelled,DeliveryStatuses.ReturnRequested,DeliveryStatuses.Returned}.Should().OnlyHaveUniqueItems();
    [Fact] public void Notification_keys_cover_delivery_lifecycle()=>new[]{DeliveryTemplateKeys.Assigned,DeliveryTemplateKeys.OutForDelivery,DeliveryTemplateKeys.Otp,DeliveryTemplateKeys.Delivered,DeliveryTemplateKeys.Failed,DeliveryTemplateKeys.Rescheduled}.Should().OnlyHaveUniqueItems();
}
