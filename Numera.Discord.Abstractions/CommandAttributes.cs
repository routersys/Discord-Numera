namespace Numera.Discord.Abstractions;

public enum AuthorizationLevel
{
    SystemOwner = 1,
    GuildOperator = 2,
    BankOperator = 3,
    MerchantOperator = 4,
    Customer = 5,
    Unregistered = 6,
}

public enum EconomyInteractionContext
{
    Guild = 1,
    BotDirectMessage = 2,
    PrivateChannel = 3,
}

public enum EconomyIntegrationType
{
    GuildInstall = 1,
    UserInstall = 2,
}

public enum EconomyComponentKind
{
    Button = 1,
    Select = 2,
}

public enum EconomyModalFieldStyle
{
    Short = 1,
    Paragraph = 2,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomySlashCommandAttribute : Attribute
{
    public EconomySlashCommandAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyUserCommandAttribute : Attribute
{
    public EconomyUserCommandAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyMessageCommandAttribute : Attribute
{
    public EconomyMessageCommandAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EconomyCommandGroupAttribute : Attribute
{
    public EconomyCommandGroupAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyCommandContextAttribute : Attribute
{
    public EconomyCommandContextAttribute(params EconomyInteractionContext[] contexts) => Contexts = contexts;

    public EconomyInteractionContext[] Contexts { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyIntegrationTypeAttribute : Attribute
{
    public EconomyIntegrationTypeAttribute(params EconomyIntegrationType[] integrationTypes) =>
        IntegrationTypes = integrationTypes;

    public EconomyIntegrationType[] IntegrationTypes { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyDefaultMemberPermissionsAttribute : Attribute
{
    public EconomyDefaultMemberPermissionsAttribute(ulong permissions) => Permissions = permissions;

    public ulong Permissions { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyEphemeralAttribute : Attribute
{
    public EconomyEphemeralAttribute(bool enabled) => Enabled = enabled;

    public bool Enabled { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyAuthorizationAttribute : Attribute
{
    public EconomyAuthorizationAttribute(AuthorizationLevel minimumLevel) => MinimumLevel = minimumLevel;

    public AuthorizationLevel MinimumLevel { get; }
}
