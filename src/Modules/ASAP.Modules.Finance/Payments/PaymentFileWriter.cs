using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ASAP.Modules.Finance.Payments;

/// <summary>Who is paying, from where, and when.</summary>
/// <param name="MessageId">Unique to the file. Banks refuse a second file with the same one.</param>
/// <param name="CreatedAtUtc">When the file was made.</param>
/// <param name="DebtorName">The company paying.</param>
/// <param name="DebtorIban">The account the money leaves.</param>
/// <param name="DebtorBic">The bank's BIC, where it is known.</param>
/// <param name="ExecutionDate">The day the bank should pay.</param>
/// <param name="CurrencyCode">What every transfer in the file is in.</param>
public readonly record struct PaymentFileHeader(
    string MessageId,
    DateTime CreatedAtUtc,
    string DebtorName,
    string DebtorIban,
    string? DebtorBic,
    DateOnly ExecutionDate,
    string CurrencyCode);

/// <summary>One transfer to one beneficiary.</summary>
/// <param name="EndToEndId">Travels with the money to the beneficiary's statement.</param>
/// <param name="Amount">How much.</param>
/// <param name="CreditorName">Who is being paid.</param>
/// <param name="CreditorIban">Where it goes.</param>
/// <param name="CreditorBic">Their bank's BIC, where it is known.</param>
/// <param name="RemittanceInformation">What it pays, so they can match it without phoning.</param>
public readonly record struct PaymentTransfer(
    string EndToEndId,
    decimal Amount,
    string CreditorName,
    string CreditorIban,
    string? CreditorBic,
    string RemittanceInformation);

/// <summary>A file ready to hand to a bank, and the fingerprint that proves it has not changed.</summary>
/// <param name="FileName">What to call it.</param>
/// <param name="Content">The file itself.</param>
/// <param name="Sha256">Its hash, recorded so a file edited after export can be told apart.</param>
/// <param name="TransferCount">How many transfers it carries.</param>
/// <param name="ControlSum">What they add up to, which the bank checks against the transfers.</param>
public readonly record struct PaymentFile(
    string FileName,
    string Content,
    string Sha256,
    int TransferCount,
    decimal ControlSum);

/// <summary>
/// Writes an ISO 20022 credit transfer initiation — the pain.001 file banks import.
/// </summary>
/// <remarks>
/// <para>
/// The international standard rather than any one bank's own layout, because every bank in the
/// region that takes bulk payments takes this, and a company that changes bank should not have to
/// change the file. Version 03, which is the one banks accept without asking.
/// </para>
/// <para>
/// The count and control sum in the header are worked out from the transfers written, not passed
/// in. A bank checks them against each other and refuses a file that disagrees with itself, and
/// the only way to be sure they agree is for there to be one source.
/// </para>
/// <para>
/// Amounts are written with a full stop and exactly two decimals whatever culture the server runs
/// in. A file written on a machine set to Arabic or German would otherwise say 1.250,00, and a
/// bank reading that as twelve hundred and fifty thousandths is not a mistake anybody finds
/// quickly.
/// </para>
/// </remarks>
public static class PaymentFileWriter
{
    private const string Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    /// <summary>Writes the file.</summary>
    /// <param name="header">Who is paying, from where, and when.</param>
    /// <param name="transfers">What is being paid, and to whom.</param>
    /// <returns>The file and its fingerprint.</returns>
    public static PaymentFile Write(PaymentFileHeader header, IReadOnlyList<PaymentTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        if (transfers.Count == 0)
        {
            throw new ArgumentException("A payment file has to pay somebody.", nameof(transfers));
        }

        var controlSum = transfers.Sum(static t => Math.Round(t.Amount, 2, MidpointRounding.AwayFromZero));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
        };

        using var stream = new MemoryStream();

        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Document", Namespace);
            xml.WriteStartElement("CstmrCdtTrfInitn", Namespace);

            xml.WriteStartElement("GrpHdr", Namespace);
            Element(xml, "MsgId", Limit(header.MessageId, 35));
            Element(xml, "CreDtTm", header.CreatedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            Element(xml, "NbOfTxs", transfers.Count.ToString(CultureInfo.InvariantCulture));
            Element(xml, "CtrlSum", Money(controlSum));
            xml.WriteStartElement("InitgPty", Namespace);
            Element(xml, "Nm", Limit(header.DebtorName, 70));
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("PmtInf", Namespace);
            Element(xml, "PmtInfId", Limit(header.MessageId, 35));
            Element(xml, "PmtMtd", "TRF");
            Element(xml, "NbOfTxs", transfers.Count.ToString(CultureInfo.InvariantCulture));
            Element(xml, "CtrlSum", Money(controlSum));
            Element(xml, "ReqdExctnDt", header.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            xml.WriteStartElement("Dbtr", Namespace);
            Element(xml, "Nm", Limit(header.DebtorName, 70));
            xml.WriteEndElement();

            Account(xml, "DbtrAcct", header.DebtorIban);
            Agent(xml, "DbtrAgt", header.DebtorBic);

            foreach (var transfer in transfers)
            {
                xml.WriteStartElement("CdtTrfTxInf", Namespace);

                xml.WriteStartElement("PmtId", Namespace);
                Element(xml, "EndToEndId", Limit(transfer.EndToEndId, 35));
                xml.WriteEndElement();

                xml.WriteStartElement("Amt", Namespace);
                xml.WriteStartElement("InstdAmt", Namespace);
                xml.WriteAttributeString("Ccy", header.CurrencyCode);
                xml.WriteString(Money(transfer.Amount));
                xml.WriteEndElement();
                xml.WriteEndElement();

                Agent(xml, "CdtrAgt", transfer.CreditorBic);

                xml.WriteStartElement("Cdtr", Namespace);
                Element(xml, "Nm", Limit(transfer.CreditorName, 70));
                xml.WriteEndElement();

                Account(xml, "CdtrAcct", transfer.CreditorIban);

                xml.WriteStartElement("RmtInf", Namespace);
                Element(xml, "Ustrd", Limit(transfer.RemittanceInformation, 140));
                xml.WriteEndElement();

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        var bytes = stream.ToArray();
        var content = Encoding.UTF8.GetString(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        return new PaymentFile(
            $"{header.MessageId}.xml",
            content,
            hash,
            transfers.Count,
            controlSum);
    }

    private static string Money(decimal amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Cuts a value to what the schema allows, so a long name does not make the bank refuse the whole file.</summary>
    private static string Limit(string? value, int length)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    private static void Element(XmlWriter xml, string name, string value)
        => xml.WriteElementString(name, Namespace, value);

    private static void Account(XmlWriter xml, string name, string iban)
    {
        xml.WriteStartElement(name, Namespace);
        xml.WriteStartElement("Id", Namespace);
        Element(xml, "IBAN", Iban.Normalise(iban));
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>
    /// The bank's identifier, or the schema's own way of saying it is not given.
    /// </summary>
    /// <remarks>
    /// Most banks here route on the IBAN alone and do not need the BIC. The element is still
    /// required, so where it is unknown the file says so explicitly rather than leaving it out and
    /// failing validation.
    /// </remarks>
    private static void Agent(XmlWriter xml, string name, string? bic)
    {
        xml.WriteStartElement(name, Namespace);
        xml.WriteStartElement("FinInstnId", Namespace);

        if (string.IsNullOrWhiteSpace(bic))
        {
            xml.WriteStartElement("Othr", Namespace);
            Element(xml, "Id", "NOTPROVIDED");
            xml.WriteEndElement();
        }
        else
        {
            Element(xml, "BIC", bic.Trim().ToUpperInvariant());
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }
}
