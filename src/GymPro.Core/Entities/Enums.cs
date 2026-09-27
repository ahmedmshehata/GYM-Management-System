namespace GymPro.Core.Entities;

public enum Gender
{
    Unknown = 0,
    Male = 1,
    Female = 2,
}

public enum PlanKind
{
    /// <summary>Unlimited visits between start and end date (legacy role_type 'M').</summary>
    TimeBased = 0,

    /// <summary>A fixed number of sessions that must be used before the end date (legacy role_type 'C').</summary>
    SessionPack = 1,
}

public enum SubscriptionStatus
{
    Active = 0,
    Cancelled = 1,
}

public enum UserRole
{
    Reception = 0,
    Manager = 1,
    Admin = 2,
}

public enum AuditAction
{
    Insert = 0,
    Update = 1,
    Delete = 2,
    Login = 10,
    LoginFailed = 11,
    Logout = 12,
    PasswordChanged = 13,
    Import = 20,
}
