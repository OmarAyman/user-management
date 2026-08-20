namespace UserManagement.Domain.Constants;

/// <summary>
/// Field limits shared by the EF Core configuration, the validators and the tests. One definition means the
/// database column, the validation message and the assertion can never disagree about what "too long" is.
/// </summary>
public static class UserConstraints
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 50;

    public const int EmailMaxLength = 256;

    public const int NameMaxLength = 100;

    public const int PasswordHashMaxLength = 500;

    /// <summary>Length beats symbol classes, so the floor is high and the composition rule is light.</summary>
    public const int PasswordMinLength = 12;

    public const int PasswordMaxLength = 128;

    /// <summary>Enough for an IPv6 address, including an IPv4-mapped form.</summary>
    public const int IpAddressMaxLength = 45;

    public const int AuditEntityNameMaxLength = 100;
    public const int AuditEntityIdMaxLength = 64;
    public const int CorrelationIdMaxLength = 64;

    /// <summary>SHA-256 rendered as lowercase hex.</summary>
    public const int TokenHashLength = 64;
}
