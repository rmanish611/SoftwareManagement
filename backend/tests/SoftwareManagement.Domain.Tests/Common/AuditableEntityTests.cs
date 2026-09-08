using AwesomeAssertions;
using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Tests.Common;

public sealed class AuditableEntityTests
{
    private sealed class TestEntity : AuditableEntity;

    [Fact]
    public void IsTransient_ReturnsTrue_WhenIdIsEmpty()
    {
        var entity = new TestEntity();

        entity.IsTransient().Should().BeTrue("an entity with no identity has not been persisted yet");
    }

    [Fact]
    public void IsTransient_ReturnsFalse_WhenIdIsAssigned()
    {
        var entity = new TestEntity { Id = Guid.NewGuid() };

        entity.IsTransient().Should().BeFalse();
    }

    [Fact]
    public void NewEntity_HasNoModificationStamp()
    {
        var entity = new TestEntity();

        entity.ModifiedAtUtc.Should().BeNull();
        entity.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public void RowVersion_IsNeverNull_SoConcurrencyChecksCannotThrow()
    {
        var entity = new TestEntity();

        entity.RowVersion.Should().NotBeNull().And.BeEmpty();
    }
}
