# Payment runs

A payment run pays the vendors whose invoices are due, from one bank account, on one day. It works out what is owed, writes the file your bank imports, and — once the bank has paid — records the payments and settles the invoices.

## Three stages, with a hard line between each

1. **Draft.** A proposal. Take vendors off it, argue about it, cancel it. Nothing has left the bank.
2. **Exported.** The file has been made and may already be at the bank. Nothing on the run can change now: a line taken off after export is a payment the ledger forgets and the bank still makes.
3. **Posted.** The payments are in the ledger and set against the invoices they paid.

Export and posting are separate on purpose. The file goes to the bank; the bank may reject a line; somebody posts what was actually paid, usually a day or two later. A system that posted on export would be recording payments on the strength of a file.

Proposing and releasing are separate permissions, so that deciding who is owed and sending them money are not the same act.

## What gets proposed

Every open vendor invoice due on or before the date you choose, in the bank account's currency, grouped into one transfer per vendor.

Left out, and the run says so:

- **Invoices already on another run** that is still a draft or exported. Two runs made the same morning cannot both carry one invoice. Cancel a run and its invoices become available again.
- **Invoices in another currency.** Pay them from an account held in their currency.
- **Blocked vendors.**

**Credit notes are not netted.** Apply them against invoices on the vendor's account first; what is left open is what gets paid. Deciding which invoice a credit note settles belongs to whoever agreed the credit, not to a payment run.

## Bank details, and the fraud they invite

Each vendor carries an IBAN. It is checked when it is entered — its length and its check digits — so a mistyped IBAN is refused at the keyboard rather than discovered when a payment bounces, or lands in somebody else's account because two swapped digits happened to make a number that exists.

**Each line copies the vendor's IBAN when the run is proposed**, and export refuses a line whose vendor card now says something different.

This is the most important rule on this page. An email announcing new bank details, arriving just before a payment run, is the commonest shape payment fraud takes. If the details have changed, confirm the change by telephone on a number you already hold — never one given in the email — then take the line off and propose it again.

Every change to a vendor's bank details is logged with who made it and what it replaced.

## The file

An ISO 20022 **pain.001** credit transfer file — the international standard every bank in the region that takes bulk payments accepts, so changing bank does not mean changing the file.

- The file's message id is the run number. Banks refuse a second file with an id they have seen, which is why the number series allows no gaps.
- The count and total in the header are worked out from the transfers written. The bank checks them against each other.
- Amounts are always written as 1250.50, whatever language the server runs in.
- Names longer than the standard allows are shortened rather than making the bank refuse the whole file.

The file is stored exactly as it was made, with its **SHA-256 fingerprint**. Downloading it again gives the same bytes. Many bank upload screens show a fingerprint; compare it. A file opened and "tidied" on a desktop between export and upload is a file paying people the system did not pay.

## Posting

Posting checks every invoice again. If one has been settled some other way since the file was made — somebody paid it by hand — posting is refused and names it. Check with the bank whether that transfer actually went. If it did, the vendor has been paid twice, and that needs a conversation with them, not a posting.

The payments post on the run's payment date: the bank account is credited and each vendor is debited. Each payment is then set against the invoices it paid. If an application is refused, the run still posts — the money went — and the payment is left open on the vendor's account to apply by hand.

## Cancelling

A draft or exported run can be cancelled. Cancel an exported run only once you are certain the bank has not received the file, or has rejected it.
