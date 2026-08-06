namespace WhatsBiz.Domain.Warehouses;

public sealed class Warehouse
{
    public Guid WarehouseId { get; set; } = Guid.NewGuid();
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public Guid WarehouseTypeId { get; set; }
    public Guid? BranchId { get; set; }
    public string? ManagerName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public decimal Capacity { get; set; }
    public Guid? AddressId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public string? Remarks { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public WarehouseType? WarehouseType { get; set; }
    public WarehouseAddress? Address { get; set; }
    public ICollection<WarehouseContact> Contacts { get; set; } = [];
    public ICollection<WarehouseZone> Zones { get; set; } = [];
}

public sealed class WarehouseType
{
    public Guid WarehouseTypeId { get; set; } = Guid.NewGuid();
    public string TypeCode { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseAddress
{
    public Guid AddressId { get; set; } = Guid.NewGuid();
    public Guid WarehouseId { get; set; }
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = "";
    public string? District { get; set; }
    public string State { get; set; } = "";
    public string Country { get; set; } = "India";
    public string PostalCode { get; set; } = "";
}

public sealed class WarehouseContact
{
    public Guid ContactId { get; set; } = Guid.NewGuid();
    public Guid WarehouseId { get; set; }
    public string ContactPerson { get; set; } = "";
    public string? Designation { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class WarehouseZone
{
    public Guid ZoneId { get; set; } = Guid.NewGuid();
    public Guid WarehouseId { get; set; }
    public string ZoneCode { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<WarehouseBin> Bins { get; set; } = [];
}

public sealed class WarehouseBin
{
    public Guid BinId { get; set; } = Guid.NewGuid();
    public Guid WarehouseId { get; set; }
    public Guid ZoneId { get; set; }
    public string BinCode { get; set; } = "";
    public string BinName { get; set; } = "";
    public decimal MaximumCapacity { get; set; }
    public bool IsActive { get; set; } = true;
}
