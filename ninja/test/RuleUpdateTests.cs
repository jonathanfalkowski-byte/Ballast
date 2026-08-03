using System;
using System.IO;
using System.Text;
using Ballast;

public static class RuleUpdateTests
{
    public static void Run()
    {
        ACompleteSignedRuleBookPasses();
        MissingIntegrityFailsClosed();
        APartialSignedPayloadIsRejected();
        AFutureVerificationDateIsRejected();
    }

    static byte[] ShippedRules()
    {
        return File.ReadAllBytes("Ballast/ballast-rules.txt");
    }

    static void WithTestVerifier(Action work)
    {
        Func<byte[], string, bool> old = RuleBookUpdater.SignatureVerifier;
        try
        {
            RuleBookUpdater.SignatureVerifier = delegate(byte[] bytes, string signature)
            {
                return bytes != null && signature == "valid";
            };
            work();
        }
        finally { RuleBookUpdater.SignatureVerifier = old; }
    }

    static void ACompleteSignedRuleBookPasses()
    {
        T.S("a complete signed rule book passes validation");
        WithTestVerifier(delegate
        {
            RuleBook book;
            string error;
            bool ok = RuleBookUpdater.ValidateDownloadedPayload(
                ShippedRules(), "valid", new DateTime(2026, 8, 3), out book, out error);
            T.Ok(ok, "the shipped rule book satisfies integrity and completeness checks");
            T.Ok(book != null && book.Count >= RuleBookUpdater.MinimumRuleRows,
                 "the validated result retains all account rows");
        });
    }

    static void MissingIntegrityFailsClosed()
    {
        T.S("an unsigned rule book fails closed");
        Func<byte[], string, bool> old = RuleBookUpdater.SignatureVerifier;
        try
        {
            RuleBookUpdater.SignatureVerifier = null;
            RuleBook book;
            string error;
            T.Ok(!RuleBookUpdater.ValidateDownloadedPayload(
                ShippedRules(), null, new DateTime(2026, 8, 3), out book, out error),
                "no verifier and no signature never becomes an implicit pass");
            T.Ok(error.IndexOf("signature") >= 0, "the reason names the missing integrity check");
        }
        finally { RuleBookUpdater.SignatureVerifier = old; }
    }

    static void APartialSignedPayloadIsRejected()
    {
        T.S("a signed but partial rule book is rejected");
        WithTestVerifier(delegate
        {
            byte[] partial = Encoding.UTF8.GetBytes(
                "VERSION|999\nVERIFIED|2026-08-03\nApex Trader Funding|One row|50000|2500|INTRADAY|0|3000\n");
            RuleBook book;
            string error;
            T.Ok(!RuleBookUpdater.ValidateDownloadedPayload(
                partial, "valid", new DateTime(2026, 8, 3), out book, out error),
                "a high version cannot hide an incomplete payload");
            T.Ok(error.IndexOf("incomplete") >= 0, "the rejection explains completeness");
        });
    }

    static void AFutureVerificationDateIsRejected()
    {
        T.S("a rule book cannot claim a future verification date");
        WithTestVerifier(delegate
        {
            string text = Encoding.UTF8.GetString(ShippedRules())
                .Replace("VERIFIED|2026-08-01", "VERIFIED|2099-01-01");
            RuleBook book;
            string error;
            T.Ok(!RuleBookUpdater.ValidateDownloadedPayload(
                Encoding.UTF8.GetBytes(text), "valid", new DateTime(2026, 8, 3), out book, out error),
                "future-dated evidence is not accepted");
            T.Ok(error.IndexOf("verification date") >= 0, "the rejection names the bad date");
        });
    }
}
