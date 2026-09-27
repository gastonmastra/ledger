namespace Ledger.Modules.Accounts.Domain;

public sealed class Account
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public AccountType Type { get; private set; }
    public bool IsArchived { get; private set; }

    private Account(
        Guid id,
        string name,
        AccountType type)
    {
        ValidateName(name);

        Id = id;
        Name = name;
        Type = type;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Account name is required.",
                nameof(name)
            );
    }

    public void Rename(string name)
    {
        ValidateName(name);

        Name = name;
    }

    public void Archive()
    {
        IsArchived = true;
    }

    public static Account Create(
        string name,
        AccountType accountType)
        => new(Guid.NewGuid(), name, accountType);

}