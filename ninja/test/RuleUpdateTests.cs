using System;
using System.IO;
using System.Globalization;
using System.Text;
using Ballast;

public static class RuleUpdateTests
{
    public static void Run()
    {
        VerificationIsPerFirm();
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

    /// <summary>
    /// A clock the shipped rule book cannot outrun.
    ///
    /// This was a fixed 3 August, so the day the rule book's own verification
    /// date moved to the 6th the suite went red on a file that was perfectly
    /// valid - the validator was right and the test's calendar was wrong. Reading
    /// the date out of the file under test keeps the future-date guard honest
    /// without breaking every time somebody verifies a firm.
    /// </summary>
    static DateTime ShippedNow()
    {
        string text = Encoding.UTF8.GetString(ShippedRules());
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string[] f = lines[i].Trim().Split('|');
            if (f.Length == 2 && f[0].Trim().ToUpperInvariant() == "VERIFIED")
            {
                DateTime d;
                if (DateTime.TryParseExact(f[1].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
                    return d.Date;
            }
        }
        return new DateTime(2026, 8, 3);
    }

    static void ACompleteSignedRuleBookPasses()
    {
        T.S("a complete signed rule book passes validation");
        WithTestVerifier(delegate
        {
            RuleBook book;
            string error;
            bool ok = RuleBookUpdater.ValidateDownloadedPayload(
                ShippedRules(), "valid", ShippedNow(), out book, out error);
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
                ShippedRules(), null, ShippedNow(), out book, out error),
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
                partial, "valid", ShippedNow(), out book, out error),
                "a high version cannot hide an incomplete payload");
            T.Ok(error.IndexOf("incomplete") >= 0, "the rejection explains completeness");
        });
    }

    static void AFutureVerificationDateIsRejected()
    {
        T.S("a rule book cannot claim a future verification date");
        WithTestVerifier(delegate
        {
            // Rewrite whatever date the shipped book carries, rather than one
            // typed in here. Hardcoding "2026-08-01" meant this test silently
            // stopped patching anything the day the rule book was re-verified,
            // and then failed because the book's real date was newer than the
            // 3 August the check below pretends it is.
            string text = System.Text.RegularExpressions.Regex.Replace(
                Encoding.UTF8.GetString(ShippedRules()),
                @"VERIFIED\|\d{4}-\d{2}-\d{2}", "VERIFIED|2099-01-01");
            RuleBook book;
            string error;
            T.Ok(!RuleBookUpdater.ValidateDownloadedPayload(
                Encoding.UTF8.GetBytes(text), "valid", ShippedNow(), out book, out error),
                "future-dated evidence is not accepted");
            T.Ok(error.IndexOf("verification date") >= 0, "the rejection names the bad date");
        });
    }


    /// <summary>
    /// One verification date for the whole rule book was a quiet lie. It read
    /// "verified" across a file in which two firms had been confirmed against
    /// their own pages and seven had not, and nothing on screen told a trader
    /// which kind of row he was looking at.
    ///
    /// A figure nobody has confirmed is not a scandal. Presenting it as though
    /// somebody had is - especially on a public reference page whose entire
    /// claim is that it is right.
    /// </summary>
    static void VerificationIsPerFirm()
    {
        T.S("verification is per firm, and silence means unconfirmed");

        RuleBook rb = new RuleBook();
        T.Ok(rb.Load("Ballast/ballast-rules.txt"), "the shipped rule book loads");

        // Checked against the firm's own pages today.
        T.Eq(rb.VerifiedFor("Topstep"), "2026-08-06", "Topstep was confirmed");
        T.Ok(rb.SourceFor("Topstep").Length > 0, "and it records where");
        T.Ok(rb.ConfidenceFor("Topstep").IndexOf("Read off") >= 0,
             "so it says so: " + rb.ConfidenceFor("Topstep"));

        // Not yet checked. This must never read as though it had been.
        T.Eq(rb.VerifiedFor("Bulenox"), "", "Bulenox has not been confirmed");
        T.Ok(rb.ConfidenceFor("Bulenox").IndexOf("Not independently confirmed") >= 0,
             "and it says THAT, plainly: " + rb.ConfidenceFor("Bulenox"));

        // A firm nobody has heard of is unconfirmed rather than an error.
        T.Ok(rb.ConfidenceFor("A firm that does not exist")
               .IndexOf("Not independently confirmed") >= 0,
             "an unknown firm is unconfirmed, not a crash");
        T.Ok(rb.ConfidenceFor(null).Length > 0, "and neither is no firm at all");

        // Every confidence line, either way, tells him to check his own
        // dashboard. The rule book is a convenience and never the authority.
        T.Ok(rb.ConfidenceFor("Topstep").IndexOf("your own dashboard") >= 0,
             "a confirmed firm still says verify it yourself");
        T.Ok(rb.ConfidenceFor("Bulenox").IndexOf("your own dashboard") >= 0,
             "and so does an unconfirmed one");
    }
}
