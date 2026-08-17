namespace Numera.Discord.Abstractions;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class EconomyOptionAttribute : Attribute
{
    public EconomyOptionAttribute(string name, string description, bool required)
    {
        Name = name;
        Description = description;
        Required = required;
    }

    public string Name { get; }

    public string Description { get; }

    public bool Required { get; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true, Inherited = false)]
public sealed class EconomyChoiceAttribute : Attribute
{
    public EconomyChoiceAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyAutocompleteProviderAttribute : Attribute
{
    public EconomyAutocompleteProviderAttribute(string key) => Key = key;

    public string Key { get; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class EconomyAutocompleteAttribute : Attribute
{
    public EconomyAutocompleteAttribute(string providerKey) => ProviderKey = providerKey;

    public string ProviderKey { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyComponentAttribute : Attribute
{
    public EconomyComponentAttribute(EconomyComponentKind kind, string action)
    {
        Kind = kind;
        Action = action;
    }

    public EconomyComponentKind Kind { get; }

    public string Action { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EconomyModalAttribute : Attribute
{
    public EconomyModalAttribute(string action, Type formType)
    {
        Action = action;
        FormType = formType;
    }

    public string Action { get; }

    public Type FormType { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EconomyModalFormAttribute : Attribute
{
    public EconomyModalFormAttribute(string title) => Title = title;

    public string Title { get; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class EconomyModalFieldAttribute : Attribute
{
    public EconomyModalFieldAttribute(
        string customId,
        string label,
        EconomyModalFieldStyle style,
        bool required,
        int minLength,
        int maxLength,
        string placeholder)
    {
        CustomId = customId;
        Label = label;
        Style = style;
        Required = required;
        MinLength = minLength;
        MaxLength = maxLength;
        Placeholder = placeholder;
    }

    public string CustomId { get; }

    public string Label { get; }

    public EconomyModalFieldStyle Style { get; }

    public bool Required { get; }

    public int MinLength { get; }

    public int MaxLength { get; }

    public string Placeholder { get; }
}
