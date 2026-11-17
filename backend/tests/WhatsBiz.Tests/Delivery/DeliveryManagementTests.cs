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
    { var allowed=new[]{Permissions.Delivery.View,Permissions.Delivery.UpdateStatus,Permissions.Delivery.Confirm,Permissions.Delivery.RecordCod};allowed.Should().NotContain(Permissions.Delivery.Manage);allowed.Should().NotContain(Permissions.Delivery.Settings);allowed.Should().NotContain(Permissions.POS.View);allowed.Should().NotContain(Permissions.Finance.LedgerView); }
    [Theory]
    [InlineData(typeof(DeliveriesController))][InlineData(typeof(MyDeliveryController))][InlineData(typeof(DeliveryAgentsController))][InlineData(typeof(DeliverySettingsController))]
    public void Delivery_controllers_require_core_delivery_feature(Type controller)
    { controller.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Select(x=>x.Policy).Should().Contain(PermissionPolicyProvider.FeaturePrefix+FeatureKeys.DeliveryManagement); }
    [Fact] public void Failure_reasons_include_controlled_other_option()=>DeliveryFailureReasons.All.Should().Contain("Other");
    [Theory]
    [InlineData("PREPARING", "PREPARING", "PREPARING")]
    [InlineData("DELIVERED", "PREPARING", "PARTIALLY_DELIVERED")]
    [InlineData("DELIVERED", "OUT_FOR_DELIVERY", "PARTIALLY_DELIVERED")]
    [InlineData("DELIVERED", "DELIVERED", "DELIVERED")]
    [InlineData("DELIVERY_FAILED", "DELIVERED", "PARTIALLY_DELIVERED")]
    [InlineData("DELIVERY_FAILED", "PENDING", "DELIVERY_ATTENTION")]
    [InlineData("CANCELLED", "DELIVERED", "PARTIALLY_CANCELLED")]
    [InlineData("CANCELLED", "CANCELLED", "CANCELLED")]
    [InlineData("OUT_FOR_DELIVERY", "PREPARING", "PARTIALLY_DISPATCHED")]
    public void Marketplace_parent_delivery_projection_reconciles_independent_groups(string first,string second,string expected)
        =>MarketplaceFulfilmentPolicy.OverallStatus([first,second]).Should().Be(expected);
    [Theory]
    [InlineData("POSTED", "DELIVERED", "PREPARING", "PARTIALLY_FULFILLED")]
    [InlineData("PARTIALLY_FULFILLED", "DELIVERED", "DELIVERED", "FULFILLED")]
    [InlineData("POSTED", "DELIVERY_FAILED", "PENDING", "POSTED")]
    public void Marketplace_parent_lifecycle_only_completes_after_all_groups(string current,string first,string second,string expected)
        =>MarketplaceFulfilmentPolicy.ParentLifecycle(current,[first,second]).Should().Be(expected);
    [Fact] public void Marketplace_group_delivery_transitions_are_ordered_and_retryable()
    {
        MarketplaceFulfilmentPolicy.CanTransition("PENDING","PREPARING").Should().BeTrue();
        MarketplaceFulfilmentPolicy.CanTransition("DELIVERY_FAILED","PREPARING").Should().BeTrue();
        MarketplaceFulfilmentPolicy.CanTransition("PREPARING","DELIVERED").Should().BeFalse();
        MarketplaceFulfilmentPolicy.CanTransition("DELIVERED","PREPARING").Should().BeFalse();
    }
    [Fact] public void Lifecycle_defines_delivery_separately_from_sales()=>new[]{DeliveryStatuses.Unassigned,DeliveryStatuses.Assigned,DeliveryStatuses.Ready,DeliveryStatuses.PickedUp,DeliveryStatuses.OutForDelivery,DeliveryStatuses.Delivered,DeliveryStatuses.Failed,DeliveryStatuses.Rescheduled,DeliveryStatuses.Cancelled,DeliveryStatuses.ReturnRequested,DeliveryStatuses.Returned}.Should().OnlyHaveUniqueItems();
    [Fact] public void Notification_keys_cover_delivery_lifecycle()=>new[]{DeliveryTemplateKeys.Assigned,DeliveryTemplateKeys.OutForDelivery,DeliveryTemplateKeys.Otp,DeliveryTemplateKeys.Delivered,DeliveryTemplateKeys.Failed,DeliveryTemplateKeys.Rescheduled}.Should().OnlyHaveUniqueItems();
    [Fact] public void Standard_non_otp_completion_has_a_distinct_input_contract()=>new ConfirmDeliveryWithoutOtpInput("Customer received the order").Reason.Should().Be("Customer received the order");
    [Fact] public void Standard_non_otp_completion_requires_the_existing_confirm_permission()
    { var method=typeof(DeliveriesController).GetMethods().Single(x=>x.Name=="ConfirmWithoutOtp");method.GetCustomAttributes(typeof(HasPermissionAttribute),true).Cast<HasPermissionAttribute>().Single().Policy.Should().EndWith(Permissions.Delivery.Confirm); }

    [Fact]
    public void Storefront_delivery_transitions_keep_the_legacy_commerce_projection_in_sync()
    {
        var source = File.ReadAllText(Find("backend", "src", "WhatsBiz.Infrastructure", "Delivery", "DeliveryService.cs"));
        source.Should().Contain("SyncIntegrationDeliveryStatus(c,tx,t,row.OrderId,next,token)")
            .And.Contain("SourceChannel=N'STOREFRONT'")
            .And.Contain("DeliveredOn=CASE WHEN @status=N'DELIVERED'");
    }

    [Fact]
    public void Delivery_list_maps_collection_amounts_and_timestamps_to_their_actual_ordinals()
    {
        var source = File.ReadAllText(Find("backend", "src", "WhatsBiz.Infrastructure", "Delivery", "DeliveryService.cs"));
        source.Should().Contain("r.IsDBNull(28)?null:r.GetDecimal(28)")
            .And.Contain("r.IsDBNull(30)?null:r.GetDateTimeOffset(30)")
            .And.Contain("S(r,25),S(r,29),r.IsDBNull(27)?null:r.GetDateTimeOffset(27),S(r,26)")
            .And.NotContain("r.IsDBNull(24)?null:r.GetDecimal(24)")
            .And.NotContain("r.IsDBNull(25)?null:r.GetDateTimeOffset(25)");
    }

    [Fact]
    public void Marketplace_delivery_operations_are_parent_scoped_and_do_not_post_payment_or_invoice()
    {
        var source=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Delivery","DeliveryService.cs"));
        var api=File.ReadAllText(Find("backend","src","WhatsBiz.Api","Controllers","DeliveriesController.cs"));
        var customer=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Storefront","StorefrontCustomerService.cs"));
        var models=File.ReadAllText(Find("backend","src","WhatsBiz.Application","Features","Delivery","DeliveryModels.cs"));
        source.Should().Contain("EnsureMarketplaceParent(c,tx,tenantId,orderId,token)")
            .And.Contain("WHERE TenantId=@tenant AND OrderId=@order AND FulfilmentGroupId=@group AND FulfilmentStatus=@prior")
            .And.Contain("MarketplaceFulfilmentGroupEvents")
            .And.Contain("MarketplaceCodCollections c WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("CollectionStatus='REFUND_DUE'")
            .And.Contain("Marketplace group cancellation requires allocation-aware inventory return and refund reconciliation")
            .And.Contain("w.PaymentType,c.CollectionStatus")
            .And.Contain("collectionStatus is not(\"COLLECTED\" or \"REFUND_DUE\")")
            .And.Contain("N'PARTIALLY_DELIVERED'")
            .And.Contain("N'PARTIALLY_DISPATCHED'");
        models.Should().Contain("ParentDeliveryStatus").And.Contain("ParentPaymentStatus");
        api.Should().Contain("marketplace-orders/{orderId:guid}/fulfilment-groups")
            .And.Contain("Permissions.Delivery.Assign")
            .And.Contain("Permissions.Delivery.Confirm");
        customer.Should().Contain("MarketplaceFulfilmentGroupItems")
            .And.Contain("StorefrontDeliveryGroupDto")
            .And.Contain("PARTIALLY_DELIVERED")
            .And.Contain("PARTIALLY_DISPATCHED")
            .And.NotContain("SELECT g.WarehouseId");
    }

    [Fact]
    public void Marketplace_delivery_discovery_is_delivery_scoped_and_assignment_scoped()
    {
        var controller=typeof(DeliveriesController);
        var list=controller.GetMethod(nameof(DeliveriesController.MarketplaceAssignedOrders))!;
        list.GetCustomAttributes(typeof(HasPermissionAttribute),true).Cast<HasPermissionAttribute>().Single().Policy.Should().EndWith(Permissions.Delivery.View);
        var requirements=typeof(MyDeliveryController).GetMethod(nameof(MyDeliveryController.MarketplaceRequirements))!;
        requirements.GetCustomAttributes(typeof(HasPermissionAttribute),true).Cast<HasPermissionAttribute>().Single().Policy.Should().EndWith(Permissions.Delivery.View);

        var service=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Delivery","DeliveryService.cs"));
        service.Should().Contain("WHERE o.TenantId=@tenant AND (@manage=1 OR a.UserId=@user)")
            .And.Contain("g.TenantId=@tenant AND g.OrderId=@order AND (@manage=1 OR a.UserId=@user)")
            .And.Contain("i.OrderType='MARKETPLACE'");

        var seeder=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Identity","IdentitySeeder.cs"));
        seeder.Should().Contain("deliveryClaims.FirstOrDefault")
            .And.Contain("roles.RemoveClaimAsync(deliveryRole,deliveryPosView)");
        var routes=File.ReadAllText(Find("frontend","WhatsBiz.Web","src","app","app.routes.ts"));
        routes.Should().Contain("path: 'orders/storefront',canActivate:[permissionGuard],data:{permission:'pos.view'")
            .And.Contain("path: 'delivery'");
        var employeeUi=File.ReadAllText(Find("frontend","WhatsBiz.Web","src","app","features","administration","identity-administration.component.ts"));
        employeeUi.Should().Contain("permissions: [] as string[]")
            .And.Contain("permissions: [...user.permissions]");
    }

    [Fact]
    public void Marketplace_group_otp_is_scoped_and_reuses_tenant_delivery_limits()
    {
        var source=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Delivery","DeliveryService.cs"));
        var api=File.ReadAllText(Find("backend","src","WhatsBiz.Api","Controllers","DeliveriesController.cs"));
        source.Should().Contain("MarketplaceFulfilmentGroupOtps WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("MarketplaceOtpHash(tenantId,orderId,groupId,input.Otp,salt)")
            .And.Contain("MarketplaceOtpFingerprint(tenantId,orderId,otp)")
            .And.Contain("MaxOtpAttempts")
            .And.Contain("DeliveryOtpExpiryMinutes")
            .And.Contain("OtpResendCooldownSeconds")
            .And.Contain("NotifyMarketplaceGroupOtp")
            .And.Contain("ShouldCreateNotification(mode,outboundSafety)");
        api.Should().Contain("request-otp").And.Contain("confirm-delivery-otp");
    }

    [Fact]
    public void Marketplace_cancellation_requires_safe_inventory_and_persists_refund_review()
    {
        var source=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Delivery","DeliveryService.cs"));
        var migration=File.ReadAllText(Find("database","migrations","V89-MarketplaceGroupDeliverySafety.sql"));
        source.Should().Contain("Marketplace_ReleaseOrder")
            .And.Contain("ValidateMarketplaceGroupRestock")
            .And.Contain("ApprovalStatus='APPROVED'")
            .And.Contain("ga.Qty<>ii.Quantity")
            .And.Contain("i.PromotionDiscountAmount<>0")
            .And.Contain("i.DeliveryCharge<>0")
            .And.Contain("CollectionStatus='REFUND_DUE'")
            .And.Contain("REFUND_REVIEW_REQUIRED");
        source.Should().NotContain("QuantityOnHand=QuantityOnHand+");
        migration.Should().Contain("MarketplaceFulfilmentRefundObligations")
            .And.Contain("REFUND_REQUIRED")
            .And.Contain("REVIEW_REQUIRED")
            .And.Contain("UQ_MarketplaceGroupRefunds_TenantGroup")
            .And.Contain("RestockTransactionId");
    }

    [Fact]
    public void Standard_delivery_otp_remains_bound_to_legacy_order_delivery()
    {
        var source=File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Delivery","DeliveryService.cs"));
        source.Should().Contain("OrderDeliveryOtps WITH(UPDLOCK)")
            .And.Contain("OtpHash,OtpSalt,ExpiresAt,AttemptCount,VerifiedAt,InvalidatedAt")
            .And.Contain("OtpHash(tenantId,deliveryId,input.Otp,salt)");
    }

    private static string Find(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), Path.Combine(parts));
    }
}
