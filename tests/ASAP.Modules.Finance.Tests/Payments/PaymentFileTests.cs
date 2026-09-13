using System.Globalization;
using System.Xml.Linq;
using ASAP.Modules.Finance.Payments;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Payments;

/// <summary>
/// Whether an IBAN is one a bank would accept.
/// </summary>
/// <remarks>
/// A mistyped IBAN of the right length looks perfectly plausible on a screen. The check digits are
/// what stop two swapped digits paying somebody else.
/// </remarks>
public sealed class IbanTests
{
    /// <summary>The published examples hold.</summary>
    [Theory]
    [InlineData("SA0380000000608010167519")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("DE89370400440532013000")]
    public void Real_ibans_pass(string iban) => Iban.IsValid(iban).ShouldBeTrue();

    /// <summary>The way IBANs are printed on invoices is accepted.</summary>
    [Fact]
    public void Spaces_and_lower_case_are_accepted()
    {
        Iban.IsValid("sa03 8000 0000 6080 1016 7519").ShouldBeTrue();
        Iban.Normalise("sa03 8000 0000 6080 1016 7519").ShouldBe("SA0380000000608010167519");
    }

    /// <summary>One wrong digit is caught.</summary>
    [Fact]
    public void One_wrong_digit_is_caught() => Iban.IsValid("SA0380000000608010167518").ShouldBeFalse();

    /// <summary>Two digits swapped is caught.</summary>
    [Fact]
    public void Two_swapped_digits_are_caught() => Iban.IsValid("SA0380000000608010167591").ShouldBeFalse();

    /// <summary>A Saudi IBAN with a digit missing is caught by its length.</summary>
    [Fact]
    public void A_saudi_iban_the_wrong_length_is_caught() => Iban.IsValid("SA038000000060801016751").ShouldBeFalse();

    /// <summary>Nothing at all is not an IBAN.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT AN IBAN")]
    public void Nothing_is_not_an_iban(string? iban) => Iban.IsValid(iban).ShouldBeFalse();
}

/// <summary>
/// The pain.001 file a bank imports.
/// </summary>
/// <remarks>
/// A bank checks the header's count and control sum against the transfers and refuses a file that
/// disagrees with itself, so those are worked out from the transfers and checked here.
/// </remarks>
public sealed class PaymentFileWriterTests
{
    private static readonly XNamespace Pain = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    /// <summary>The header's count and control sum agree with the transfers.</summary>
    [Fact]
    public void The_header_agrees_with_the_transfers()
    {
        var file = Write();

        file.TransferCount.ShouldBe(2);
        file.ControlSum.ShouldBe(3750.50m);

        var xml = XDocument.Parse(file.Content);
        var header = xml.Descendants(Pain + "GrpHdr").Single();

        header.Element(Pain + "NbOfTxs")!.Value.ShouldBe("2");
        header.Element(Pain + "CtrlSum")!.Value.ShouldBe("3750.50");

        xml.Descendants(Pain + "CdtTrfTxInf").Count().ShouldBe(2);
    }

    /// <summary>
    /// Amounts use a full stop whatever culture the server is in.
    /// </summary>
    /// <remarks>
    /// A machine set to German would otherwise write 1.250,50, and a bank reading that is not a
    /// mistake anybody finds quickly.
    /// </remarks>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    public void Amounts_are_written_the_same_in_every_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            var xml = XDocument.Parse(Write().Content);

            xml.Descendants(Pain + "InstdAmt").Select(static a => a.Value).ShouldBe(["1250.50", "2500.00"]);
            xml.Descendants(Pain + "ReqdExctnDt").Single().Value.ShouldBe("2026-09-15");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The IBANs go out without spaces, and the currency is on every amount.</summary>
    [Fact]
    public void Ibans_are_normalised_and_currency_is_stated()
    {
        var xml = XDocument.Parse(Write().Content);

        xml.Descendants(Pain + "IBAN").Select(static i => i.Value)
            .ShouldBe(["SA0380000000608010167519", "GB82WEST12345698765432", "DE89370400440532013000"]);

        xml.Descendants(Pain + "InstdAmt").ShouldAllBe(a => a.Attribute("Ccy")!.Value == "SAR");
    }

    /// <summary>A name longer than the schema allows is cut rather than making the bank refuse the file.</summary>
    [Fact]
    public void A_long_name_is_cut_to_what_the_schema_allows()
    {
        var file = PaymentFileWriter.Write(Header(), [Transfer(new string('X', 120), 10m)]);

        XDocument.Parse(file.Content).Descendants(Pain + "Cdtr").Single().Element(Pain + "Nm")!.Value.Length.ShouldBe(70);
    }

    /// <summary>The same inputs make the same file, and the hash says so.</summary>
    [Fact]
    public void The_same_run_makes_the_same_file()
    {
        Write().Sha256.ShouldBe(Write().Sha256);
        Write().Sha256.Length.ShouldBe(64);
    }

    /// <summary>A file with nobody to pay is not a file.</summary>
    [Fact]
    public void A_file_must_pay_somebody()
        => Should.Throw<ArgumentException>(() => PaymentFileWriter.Write(Header(), []));

    private static PaymentFile Write()
        => PaymentFileWriter.Write(
            Header(),
            [
                Transfer("Gulf Office Supplies", 1250.50m, "GB82 WEST 1234 5698 7654 32"),
                Transfer("Najd Hardware", 2500m, "de89370400440532013000"),
            ]);

    private static PaymentFileHeader Header()
        => new(
            "PR-2026-0001",
            new DateTime(2026, 9, 13, 9, 30, 0, DateTimeKind.Utc),
            "ASAP Trading Company",
            "SA03 8000 0000 6080 1016 7519",
            null,
            new DateOnly(2026, 9, 15),
            "SAR");

    private static PaymentTransfer Transfer(string name, decimal amount, string iban = "GB82WEST12345698765432")
        => new($"PR-2026-0001-{name.Length}", amount, name, iban, null, "INV-1001, INV-1002");
}
