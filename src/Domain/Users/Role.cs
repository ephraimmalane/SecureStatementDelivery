namespace Domain.Users;

public sealed class Role
{
    public static readonly Role Admin = new(1, "Admin");
    public static readonly Role Customer = new(2, "Customer");

    private Role(int id, string name)
    {
        Id = id;
        Name = name;
    }

    private Role() { }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
}
