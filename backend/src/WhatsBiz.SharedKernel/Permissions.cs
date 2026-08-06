namespace WhatsBiz.SharedKernel;

public static class Permissions
{
    public static readonly IReadOnlyCollection<string> All = [Product.View, Product.Create, Product.Edit, Product.Delete, Supplier.View, Supplier.Create, Supplier.Edit, Supplier.Delete, Customer.View, Customer.Create, Customer.Edit, Customer.Delete, Warehouse.View, Warehouse.Create, Warehouse.Edit, Warehouse.Delete, Purchase.View, Purchase.Create, Purchase.Edit, Purchase.Delete, Purchase.Return, Purchase.Payment, Purchase.Approve, Inventory.View, Inventory.Adjust, Inventory.Transfer, Inventory.Reserve, POS.View, POS.Create, POS.Edit, POS.Return, POS.Void, POS.Discount, Sales.View, Sales.Create, Sales.Approve, Reports.View, Settings.Manage, Users.Manage, Roles.Manage, PermissionsManagement.Manage];
    public static class Product { public const string View = "product.view"; public const string Create = "product.create"; public const string Edit = "product.edit"; public const string Delete = "product.delete"; }
    public static class Supplier { public const string View = "supplier.view"; public const string Create = "supplier.create"; public const string Edit = "supplier.edit"; public const string Delete = "supplier.delete"; }
    public static class Customer { public const string View = "customer.view"; public const string Create = "customer.create"; public const string Edit = "customer.edit"; public const string Delete = "customer.delete"; }
    public static class Warehouse { public const string View = "warehouse.view"; public const string Create = "warehouse.create"; public const string Edit = "warehouse.edit"; public const string Delete = "warehouse.delete"; }
    public static class Purchase { public const string View = "purchase.view"; public const string Create = "purchase.create"; public const string Edit = "purchase.edit"; public const string Delete = "purchase.delete"; public const string Return = "purchase.return"; public const string Payment = "purchase.payment"; public const string Approve = "purchase.approve"; }
    public static class Inventory { public const string View = "inventory.view"; public const string Adjust = "inventory.adjust"; public const string Transfer = "inventory.transfer"; public const string Reserve = "inventory.reserve"; }
    public static class POS { public const string View = "pos.view"; public const string Create = "pos.create"; public const string Edit = "pos.edit"; public const string Return = "pos.return"; public const string Void = "pos.void"; public const string Discount = "pos.discount"; }
    public static class Sales { public const string View = "sales.view"; public const string Create = "sales.create"; public const string Approve = "sales.approve"; }
    public static class Reports { public const string View = "reports.view"; }
    public static class Settings { public const string Manage = "settings.manage"; }
    public static class Users { public const string Manage = "user.manage"; }
    public static class Roles { public const string Manage = "role.manage"; }
    public static class PermissionsManagement { public const string Manage = "permission.manage"; }
}
