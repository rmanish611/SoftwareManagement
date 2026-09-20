using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Notifications;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// The last step, where a queued message meets a mail server.
///
/// No mail server is started. Everything asserted here is about what the sender does when sending
/// does not work, which is the behaviour that decides whether an enquiry is retried or lost. The
/// one thing not exercised is a successful delivery: that needs a real server, and a fake one that
/// always says 250 would be asserting on the fake.
/// </summary>
public sealed class EmailSenderTests
{
    [Fact]
    public async Task REQ_NOTIF_004_A_sender_with_no_host_configured_reports_a_failure_rather_than_a_delivery()
    {
        var sender = Build(new Dictionary<string, string?>(StringComparer.Ordinal), fromAddress: "hello@softwaremanagement.test");

        var outcome = await sender.SendAsync(Message(), CancellationToken.None);

        // Saying "sent" here would mark the row Sent and the enquiry would be silently dropped. A
        // misconfigured deployment must look broken, not quiet (NFR-AVAIL-03).
        outcome.Succeeded.Should().BeFalse();
        outcome.StatusCode.Should().Be("NOT_CONFIGURED");
        outcome.Response.Should().Contain("SMTP");
    }

    [Fact]
    public async Task REQ_NOTIF_002_A_sender_with_a_host_but_no_sending_address_refuses_to_send_at_all()
    {
        var sender = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Smtp:Host"] = "127.0.0.1" },
            fromAddress: null);

        var outcome = await sender.SendAsync(Message(), CancellationToken.None);

        // Falling back to the enquirer's own address is the tempting bug here, and it is the one
        // that fails SPF at the receiving end and gets the domain filed as a forger (BR-NOTIF-03).
        outcome.Succeeded.Should().BeFalse();
        outcome.StatusCode.Should().Be("NOT_CONFIGURED");
    }

    [Fact]
    public async Task REQ_NOTIF_004_A_mail_server_that_refuses_the_connection_is_a_failure_the_pump_can_retry()
    {
        var port = FreePort();

        var sender = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Smtp:Host"] = "127.0.0.1",
                ["Smtp:Port"] = port.ToString(CultureInfo.InvariantCulture),
                ["Smtp:UseSsl"] = "false",
                ["Smtp:TimeoutMs"] = "2000",
                ["Smtp:Username"] = "postmaster",
                ["Smtp:Password"] = "not-a-real-password",
            },
            fromAddress: "hello@softwaremanagement.test");

        var outcome = await sender.SendAsync(Message(), CancellationToken.None);

        // The exception has to be caught and turned into an outcome. If it escaped, one unreachable
        // mail server would stop the pump and every other message behind it would wait with it.
        outcome.Succeeded.Should().BeFalse();
        outcome.StatusCode.Should().NotBeNullOrEmpty();
        outcome.Response.Should().NotBeNullOrEmpty();
        outcome.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task NFR_PRIV_03_A_refused_delivery_never_puts_the_recipients_address_in_the_outcome()
    {
        var port = FreePort();

        var sender = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Smtp:Host"] = "127.0.0.1",
                ["Smtp:Port"] = port.ToString(CultureInfo.InvariantCulture),
                ["Smtp:UseSsl"] = "false",
                ["Smtp:TimeoutMs"] = "2000",
            },
            fromAddress: "hello@softwaremanagement.test");

        var message = Message();
        var outcome = await sender.SendAsync(message, CancellationToken.None);

        // The outcome is written to EmailDeliveryLogs and read by whoever is diagnosing a bounce.
        // What the mail server said belongs there; who it was for does not (NFR-PRIV-03).
        outcome.Response.Should().NotContain(message.ToAddress);
    }

    [Fact]
    public async Task REQ_NOTIF_004_A_sending_address_that_is_not_an_address_fails_without_throwing()
    {
        var sender = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Smtp:Host"] = "127.0.0.1",
                ["Smtp:Port"] = FreePort().ToString(CultureInfo.InvariantCulture),
                ["Smtp:UseSsl"] = "false",
                ["Smtp:TimeoutMs"] = "2000",
            },
            fromAddress: "not an address at all");

        var outcome = await sender.SendAsync(Message(), CancellationToken.None);

        // A setting the owner mistyped is a configuration fault, not a crash in a background
        // service that then stops draining the queue.
        outcome.Succeeded.Should().BeFalse();
        outcome.StatusCode.Should().Be("ERROR");
    }

    private static SmtpEmailSender Build(Dictionary<string, string?> settings, string? fromAddress) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new StubSettings(fromAddress),
            NullLogger<SmtpEmailSender>.Instance);

    private static OutboxEmail Message() => new()
    {
        Id = Guid.NewGuid(),
        TemplateKey = EmailTemplate.LeadAcknowledgement,
        ToAddress = "enquirer@example.test",
        Subject = "We have your enquiry",
        HtmlBody = "<p>Thank you.</p>",
        TextBody = "Thank you.",
        CorrelationId = Guid.NewGuid(),
    };

    /// <summary>
    /// A port nothing is listening on. The listener is opened only to be told a free number and is
    /// closed immediately, which is the only way to ask the operating system for one.
    /// </summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubSettings(string? fromAddress) : ISystemSettings
    {
        public string? Value(string key) => key switch
        {
            "notify.fromEmail" => fromAddress,
            "company.name" => "Software Management",
            _ => null,
        };

        public void Invalidate()
        {
        }
    }
}
