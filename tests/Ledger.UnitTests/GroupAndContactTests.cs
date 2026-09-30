using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Contacts.Domain;
using Ledger.Modules.Groups.Domain;

namespace Ledger.UnitTests;

public class GroupAndContactTests
{
    [Fact]
    public void A_linked_contact_uses_the_user_id_as_person_id()
    {
        var owner = Guid.NewGuid();
        var user = Guid.NewGuid();

        var contact = Contact.Create(owner, "Bob", "bob@example.com", user);

        Assert.Equal(user, contact.PersonId);
        Assert.Throws<DomainException>(() => Contact.Create(owner, "Me", null, owner));
    }

    [Fact]
    public void Contacts_in_use_cannot_be_linked_anymore()
    {
        var contact = Contact.Create(Guid.NewGuid(), "Carl", null, null);
        contact.MarkInUse();

        Assert.Equal("contact.in_use",
            Assert.Throws<DomainException>(() => contact.LinkTo(Guid.NewGuid(), "carl@example.com")).Code);
    }

    [Fact]
    public void Members_with_expenses_cannot_be_removed()
    {
        var creator = Guid.NewGuid();
        var carl = Guid.NewGuid();
        var group = Group.Create("Trip", creator, "Alice");
        group.AddMember(carl, "Carl", null);

        Assert.True(group.IsVisibleTo(creator));
        Assert.Throws<DomainException>(() => group.AddMember(carl, "Carl", null));
        Assert.Throws<DomainException>(() => group.RemoveMember(creator));

        group.MarkActivity([carl]);
        Assert.Throws<DomainException>(() => group.RemoveMember(carl));
    }
}
