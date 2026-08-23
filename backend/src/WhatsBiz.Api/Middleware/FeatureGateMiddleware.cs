using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace WhatsBiz.Api.Middleware;

public sealed class FeatureGateMiddleware(RequestDelegate next)
{
    private static readonly (string Prefix, string Feature)[] Routes =
    [
        ("/api/whatsapp-commerce/analytics", FeatureKeys.CommerceAnalytics),
        ("/api/whatsapp-commerce/demo/orders", FeatureKeys.CommerceOrders),
        ("/api/whatsapp-commerce/demo", FeatureKeys.WhatsAppCommerceDemo),
        ("/api/whatsapp-commerce/delivery-orders", FeatureKeys.CommerceOrders),
        ("/api/whatsapp", FeatureKeys.WhatsAppConfiguration),
        ("/api/commerce/collections", FeatureKeys.CommerceCollections),
        ("/api/dashboard", FeatureKeys.Dashboard), ("/api/pos", FeatureKeys.Pos),
        ("/api/products", FeatureKeys.Products), ("/api/product-categories", FeatureKeys.Products),
        ("/api/brands", FeatureKeys.Products), ("/api/units", FeatureKeys.Products),
        ("/api/customers", FeatureKeys.Customers), ("/api/customer-groups", FeatureKeys.Customers),
        ("/api/suppliers", FeatureKeys.Suppliers), ("/api/purchases", FeatureKeys.Purchase),
        ("/api/inventory", FeatureKeys.Inventory), ("/api/finance", FeatureKeys.Finance),
        ("/api/receivables", FeatureKeys.Finance), ("/api/gst", FeatureKeys.Gst),
        ("/api/print", FeatureKeys.Printing), ("/api/warehouses", FeatureKeys.Warehouses),
        ("/api/warehouse-types", FeatureKeys.Warehouses), ("/api/identity", FeatureKeys.UsersRoles)
        ,("/api/admin/users", FeatureKeys.UsersRoles), ("/api/admin/roles", FeatureKeys.UsersRoles),
        ("/api/admin", FeatureKeys.Administration), ("/api/ledger", FeatureKeys.Finance),
        ("/api/receipts", FeatureKeys.Finance), ("/api/payments", FeatureKeys.Finance),
        ("/api/customer-outstanding", FeatureKeys.Finance), ("/api/supplier-outstanding", FeatureKeys.Finance),
        ("/api/customer-ageing", FeatureKeys.Finance), ("/api/supplier-ageing", FeatureKeys.Finance),
        ("/api/cashbook", FeatureKeys.Finance), ("/api/bankbook", FeatureKeys.Finance), ("/api/daybook", FeatureKeys.Finance)
    ];

    public async Task InvokeAsync(HttpContext context, ICurrentUserService currentUser, IFeatureService features)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var required = Routes.FirstOrDefault(x => path.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase)).Feature;
        if (required is not null && currentUser.TenantId is Guid tenantId && !await features.IsEnabledAsync(tenantId, required, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden, Title = "Feature unavailable",
                Detail = "This operation is not available for the current tenant.",
                Extensions = { ["code"] = "FEATURE_DISABLED" }
            }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
