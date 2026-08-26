using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Platform.Tests.Events;

/// <summary>Raised before a sales order is released, so anything may object.</summary>
internal sealed class OrderReleasing : VetoableEvent
{
    public required string OrderNo { get; init; }

    public decimal Total { get; init; }
}

/// <summary>Raised once an order has been released and committed.</summary>
internal sealed class OrderReleased : IIntegrationEvent
{
    public required string OrderNo { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public string EventName => "Sales.OrderReleased";
}

/// <summary>An ordinary in-transaction notification.</summary>
internal sealed class StockMoved : IDomainEvent
{
    public required string ItemNo { get; init; }
}

internal sealed class RecordingHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IAsapEvent
{
    private readonly Action<TEvent>? _onHandle;

    public RecordingHandler(int order = 0, Action<TEvent>? onHandle = null)
    {
        Order = order;
        _onHandle = onHandle;
    }

    public int Order { get; }

    public int CallCount { get; private set; }

    public Task HandleAsync(TEvent asapEvent, CancellationToken cancellationToken = default)
    {
        CallCount++;
        _onHandle?.Invoke(asapEvent);
        return Task.CompletedTask;
    }
}

internal sealed class CollectingOutbox : IOutboxWriter
{
    public List<OutboxMessage> Messages { get; } = [];

    public void Add(OutboxMessage message) => Messages.Add(message);
}

public sealed class EventPublisherTests
{
    private static readonly MessageDefinition CreditLimit = new()
    {
        Code = "SALES.ORDER.OVER_CREDIT_LIMIT",
        Severity = MessageSeverity.Blocked,
        Title = "Customer is over their credit limit",
        Detail = "Order {OrderNo} would take the customer {Excess:N2} over their limit.",
        Resolution = "Take payment against the outstanding balance, or raise the credit limit.",
        OverridePermission = "Sales.Order.Override",
    };

    private static readonly MessageDefinition LowStock = new()
    {
        Code = "INV.ITEM.BELOW_REORDER",
        Severity = MessageSeverity.Warning,
        Title = "Item is below its reorder point",
    };

    private sealed class Fixture
    {
        private readonly ServiceCollection _services = [];

        public CollectingOutbox Outbox { get; } = new();

        public MutableTenantContext Tenancy { get; } = new()
        {
            TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001"),
            CompanyId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"),
        };

        public FrozenClock Clock { get; } = new(new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc));

        public MessageCatalog Catalog { get; } = new([CreditLimit, LowStock]);

        public Fixture Subscribe<TEvent>(IEventHandler<TEvent> handler)
            where TEvent : IAsapEvent
        {
            _services.AddSingleton(handler);
            return this;
        }

        public EventPublisher Build()
            => new(
                _services.BuildServiceProvider(),
                Outbox,
                Tenancy,
                Clock,
                NullLogger<EventPublisher>.Instance);
    }

    [Fact]
    public async Task Delivers_a_domain_event_to_every_handler()
    {
        var first = new RecordingHandler<StockMoved>();
        var second = new RecordingHandler<StockMoved>();
        var publisher = new Fixture().Subscribe(first).Subscribe(second).Build();

        await publisher.PublishAsync(new StockMoved { ItemNo = "ITEM-100" });

        first.CallCount.ShouldBe(1);
        second.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_event_with_no_handlers_is_harmless()
    {
        var publisher = new Fixture().Build();

        await Should.NotThrowAsync(() => publisher.PublishAsync(new StockMoved { ItemNo = "ITEM-100" }));
    }

    [Fact]
    public async Task Runs_handlers_in_the_order_they_asked_for()
    {
        // Core sits at 0, so an extension needing to run first uses a negative order.
        var sequence = new List<string>();
        var publisher = new Fixture()
            .Subscribe(new RecordingHandler<StockMoved>(order: 10, _ => sequence.Add("late")))
            .Subscribe(new RecordingHandler<StockMoved>(order: -5, _ => sequence.Add("early")))
            .Subscribe(new RecordingHandler<StockMoved>(order: 0, _ => sequence.Add("core")))
            .Build();

        await publisher.PublishAsync(new StockMoved { ItemNo = "ITEM-100" });

        sequence.ShouldBe(["early", "core", "late"]);
    }

    [Fact]
    public async Task A_failing_domain_handler_takes_the_whole_operation_down()
    {
        // Deliberate. A domain event runs inside the caller transaction; swallowing the failure
        // would leave a shipment posted with its costing record never updated.
        var publisher = new Fixture()
            .Subscribe(new RecordingHandler<StockMoved>(
                onHandle: _ => throw new InvalidOperationException("costing unavailable")))
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new StockMoved { ItemNo = "ITEM-100" }));
    }

    [Fact]
    public async Task A_vetoable_event_with_no_objections_succeeds()
    {
        var publisher = new Fixture().Subscribe(new RecordingHandler<OrderReleasing>()).Build();

        var result = await publisher.PublishVetoableAsync(
            new OrderReleasing { OrderNo = "SO-00001", Total = 500m });

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_extension_can_refuse_an_operation_the_core_knows_nothing_about()
    {
        // The whole point of the extension model: a credit-limit rule the core never shipped,
        // enforced without forking core code, surfacing as an ordinary ASAP message.
        var fixture = new Fixture();
        var publisher = fixture
            .Subscribe(new RecordingHandler<OrderReleasing>(onHandle: e => e.Object(
                fixture.Catalog.Render(
                    "SALES.ORDER.OVER_CREDIT_LIMIT",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["OrderNo"] = e.OrderNo,
                        ["Excess"] = 250m,
                    }))))
            .Build();

        var result = await publisher.PublishVetoableAsync(
            new OrderReleasing { OrderNo = "SO-00001", Total = 500m });

        result.Failed.ShouldBeTrue();
        var objection = result.Failures.ShouldHaveSingleItem();
        objection.Code.Value.ShouldBe("SALES.ORDER.OVER_CREDIT_LIMIT");
        objection.Detail.ShouldNotBeNull().ShouldContain("250.00");
        objection.Resolution.ShouldNotBeNull().ShouldContain("raise the credit limit");
        result.IsFullyOverridable.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_subscriber_runs_even_after_one_objects()
    {
        // So the user is told every reason at once, rather than discovering them one failed
        // attempt at a time.
        var fixture = new Fixture();
        var second = new RecordingHandler<OrderReleasing>(order: 1);

        var publisher = fixture
            .Subscribe(new RecordingHandler<OrderReleasing>(
                order: 0,
                onHandle: e => e.Object(fixture.Catalog.Render("SALES.ORDER.OVER_CREDIT_LIMIT"))))
            .Subscribe(second)
            .Build();

        var result = await publisher.PublishVetoableAsync(new OrderReleasing { OrderNo = "SO-1" });

        second.CallCount.ShouldBe(1);
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_warning_travels_back_without_stopping_the_operation()
    {
        var fixture = new Fixture();
        var publisher = fixture
            .Subscribe(new RecordingHandler<OrderReleasing>(
                onHandle: e => e.Warn(fixture.Catalog.Render("INV.ITEM.BELOW_REORDER"))))
            .Build();

        var result = await publisher.PublishVetoableAsync(new OrderReleasing { OrderNo = "SO-1" });

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldHaveSingleItem().Severity.ShouldBe(MessageSeverity.Warning);
    }

    [Fact]
    public void An_integration_event_is_queued_rather_than_delivered()
    {
        var fixture = new Fixture();
        var publisher = fixture.Build();

        publisher.Enqueue(new OrderReleased { OrderNo = "SO-00001" });

        var queued = fixture.Outbox.Messages.ShouldHaveSingleItem();
        queued.EventName.ShouldBe("Sales.OrderReleased");
        queued.TenantId.ShouldBe(fixture.Tenancy.TenantId!.Value);
        queued.CompanyId.ShouldBe(fixture.Tenancy.CompanyId);
        queued.Payload.ShouldContain("SO-00001");
        queued.ProcessedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void A_queued_event_records_a_type_name_that_survives_a_patch_release()
    {
        // A message sitting in the outbox across an upgrade must still deserialise, so the
        // recorded type carries the assembly name without its version.
        var fixture = new Fixture();

        fixture.Build().Enqueue(new OrderReleased { OrderNo = "SO-1" });

        var queued = fixture.Outbox.Messages.ShouldHaveSingleItem();
        queued.EventType.ShouldContain(nameof(OrderReleased));
        queued.EventType.ShouldNotContain("Version=");
        queued.EventType.ShouldNotContain("PublicKeyToken=");
    }

    [Fact]
    public void A_queued_event_without_its_own_timestamp_is_stamped_by_the_clock()
    {
        var fixture = new Fixture();

        fixture.Build().Enqueue(new OrderReleased { OrderNo = "SO-1" });

        fixture.Outbox.Messages.ShouldHaveSingleItem()
               .OccurredAtUtc.ShouldBe(fixture.Clock.UtcNow);
    }

    [Fact]
    public void A_queued_event_keeps_a_timestamp_it_already_had()
    {
        var happened = new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc);
        var fixture = new Fixture();

        fixture.Build().Enqueue(new OrderReleased { OrderNo = "SO-1", OccurredAtUtc = happened });

        fixture.Outbox.Messages.ShouldHaveSingleItem().OccurredAtUtc.ShouldBe(happened);
    }

    [Fact]
    public void An_objection_must_actually_be_a_failure()
    {
        var catalog = new MessageCatalog([LowStock]);
        var releasing = new OrderReleasing { OrderNo = "SO-1" };

        // Guards against an extension author reaching for Object with an advisory message and
        // silently blocking every order in the company.
        Should.Throw<ArgumentException>(() => releasing.Object(catalog.Render("INV.ITEM.BELOW_REORDER")))
              .Message.ShouldContain("Use Warn");
    }

    [Fact]
    public void A_warning_must_not_be_a_failure()
    {
        var catalog = new MessageCatalog([CreditLimit]);
        var releasing = new OrderReleasing { OrderNo = "SO-1" };

        Should.Throw<ArgumentException>(
            () => releasing.Warn(catalog.Render("SALES.ORDER.OVER_CREDIT_LIMIT")))
              .Message.ShouldContain("Use Object");
    }
}
