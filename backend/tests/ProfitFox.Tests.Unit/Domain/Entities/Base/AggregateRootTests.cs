using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.Events;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Base;

public class AggregateRootTests
{
    [Test]
    public void Added_event_is_visible_in_domain_events()
    {
        var root = new TestAggregate();
        var domainEvent = new TestEvent();

        root.Raise(domainEvent);

        root.DomainEvents.ShouldBe(new[] { domainEvent });
    }

    [Test]
    public void Domain_events_cannot_be_changed_from_outside()
    {
        var root = new TestAggregate();

        ((ICollection<IDomainEvent>)root.DomainEvents).IsReadOnly.ShouldBeTrue();
    }

    [Test]
    public void Clear_removes_all_events()
    {
        var root = new TestAggregate();
        root.Raise(new TestEvent());
        root.Raise(new TestEvent());

        root.ClearDomainEvents();

        root.DomainEvents.ShouldBeEmpty();
    }

    private sealed class TestAggregate : AggregateRoot
    {
        public void Raise(IDomainEvent domainEvent)
        {
            AddDomainEvent(domainEvent);
        }
    }

    private sealed class TestEvent : IDomainEvent;
}