# Architecture notes

Short notes on things that are true about this codebase but not obvious from
reading it. Kept deliberately brief — this is a warning sign, not a manual.

The layering is copied deliberately from `Gregs-Auto`: `.Domain` (entities,
repository interfaces, business rules) / `.DAL` (EF Core context, generic
repository, infrastructure) / Web. If something here looks familiar, that's why.

---

## ⚠️ The thing this site is for is also its main legal risk

The pitch is "grab images off the internet and print them on a shirt". The
images belong to somebody. Print-on-demand vendors receive DMCA notices over
exactly this, and the operator — not the customer — is the one who receives them.

Two mechanisms exist to survive that, and **both are load-bearing, not decoration**:

1. **A rights attestation captured per order.** `Orders.RightsAttested` and
   `RightsAttestedAt`. `OrderLogic.PlaceAsync` refuses outright without it —
   it's a gate, not a checkbox that gets recorded. Stored per order rather than
   per account, because an attestation made in March says nothing about a
   picture uploaded in June.
2. **A human approval gate before production.** `Artworks.Status` starts at
   `Pending` and `DesignLogic.ValidateForOrderAsync` refuses anything that isn't
   `Approved` — *including* `Pending`. "Not yet reviewed" is not "approved".

Removing either one doesn't break a test that says "copyright". It breaks the
only two things standing between the studio and somebody else's lawyer.

**What is deliberately not claimed:** none of this detects infringement. It
records that the customer asserted a right, and puts a person in the loop before
the press runs. That's what a real shop does; it is not a safe harbour.

---

## ⚠️ The URL fetcher makes requests to addresses strangers choose

`HttpImageFetcher` is the single most security-sensitive class here. A customer
types an address and our server, inside our network, goes and fetches it. Left
open, the artwork box becomes a proxy for reaching whatever the server can reach
— a cloud metadata endpoint, an admin panel on localhost, a database on a
private subnet.

What's in place, and why each piece is needed:

| Guard | Without it |
|---|---|
| Scheme allow-list (http/https only) | `file:///C:/…` reads the disk |
| Reject userinfo in the URL | `https://trusted.com@evil.test/` reads as one host, resolves to another |
| DNS resolved and **every** returned address checked | A hostname answering one public and one private address slips through a first-address check |
| Redirects walked by hand, re-checked per hop | A public URL that 302s to `169.254.169.254` is the entire attack |
| Body read with a hard cap | `Content-Length` is a claim made by the server we're defending against |
| Short timeouts | A URL that hangs ties up request threads |
| Vague error text | "connection refused" vs "timed out" is how you map a network |

`ImageUrlPolicy` is pure functions, separate from the fetching, specifically so
it can be tested — see `ImageUrlPolicyTests`, which is the highest-value file in
the test suite. A regression there is invisible from outside: the site keeps
working perfectly while becoming an open proxy.

**Known gap: DNS rebinding.** Between the lookup and the socket opening, a
hostile DNS server can answer differently. Closing it means connecting to the
validated IP and carrying the hostname in a `Host` header, which breaks TLS
validation and virtual hosting. The mitigation today is that the response body
never reaches the customer — they see "that didn't work", not what was there.
Know about this before exposing the site to real traffic.

**The `User-Agent` is not optional.** `HttpClient` sends none by default and a
good number of image hosts (Wikimedia among them) answer 403 to anything
anonymous. Found by trying it; the fix is in `Program.cs`.

---

## Bytes are ours, never hotlinked

`Artworks` stores our own copy. `SourceUrl` is provenance — evidence for a
takedown dispute — and is never fetched again.

A URL that resolved to a cat drawing when the customer pasted it can resolve to
anything at all by the time the job reaches the press, and the press is where
that stops being recoverable.

Three consequences worth knowing:

- **Everything is decoded before it's believed.** `ImageSharpInspector.Inspect`
  decides what bytes actually are. The `Content-Type` header and the file
  extension are both just claims.
- **Everything is re-encoded.** `Normalise` strips EXIF (which can carry the GPS
  coordinates of where a photo was taken), XMP, ICC, and any embedded thumbnail
  that doesn't match the image — a thumbnail mismatch would mean the moderator
  approved one picture and the press printed another.
- **The hash is of the normalised bytes, not the input.** That's what makes
  dedupe work across the same picture with different metadata, and it's why a
  rejected image comes back rejected under a different URL.

Files live in `App_Data/artwork/`, **outside `wwwroot` on purpose**. Static files
there are served with no code in the way, which would make every stored image
publicly readable by anyone who guessed a URL — including the ones a moderator
rejected. `ArtworkController.File` costs a little throughput and buys the ability
to say no. Filenames are `{sha256}.{ext}`, generated by us; nothing the customer
supplied reaches a path.

---

## Print quality is a property of the image *and* the size

Not of the file alone. The same 1000px image is a crisp 300 DPI at 85mm across
and an unusable 42 DPI at 600mm.

This is why resolution can't be checked at upload time, and why
`DesignLogic.CheckPlacement` runs `ImageLimits.CheckPrintQuality` against the
placement rather than the artwork. Below 150 DPI is refused rather than warned
about — the customer gets a blurry shirt and blames the studio.

The refusal always states the width that *would* work, or the customer is left
guessing at sizes until one is accepted.

---

## Rules are re-checked at order time, not trusted from the design

`ValidateForOrderAsync` re-runs every rule. The world moves between saving a
design and ordering it: artwork gets rejected, a garment is archived, the print
area is shrunk on the catalogue screen.

Shrinking a print area deliberately does **not** rewrite existing designs. They
are refused at the point they'd have gone to press instead, so nothing is
silently moved without the customer's say-so.

## Prices are snapshotted

`OrderLines.UnitPrice` is copied at order time. Re-pricing the catalogue must
never restate what somebody already agreed to pay. The design's *name* is
deliberately not copied — it stays resolvable through the FK, and a rename is
usually a correction history should reflect. Same reasoning as Greg's Auto.

## Anonymous input never reaches customer records

`OrderRequests` is a holding table. Nothing a stranger types becomes a `Customer`
or an `Order` until staff accept it — at which point both are created inside one
`IUnitOfWork.ExecuteAsync`.

That transaction is not decoration. Accepting creates a customer, re-parents the
design onto them, *then* places the order. If the order is refused — the day
filled up while the request sat in the queue, the artwork got rejected — the
customer must not survive. Verified: `OrderRequestLogicTests.A_refused_acceptance_leaves_no_customer_behind`.

Note it rolls back on a **returned failure**, not just an exception. This
codebase reports business refusals as result objects, so a Unit of Work that
only caught exceptions would commit the very orphans it exists to prevent.

## Things that are enforced, and where

| Rule | Enforced in |
|---|---|
| Which URLs the server will fetch | `Artworks/ImageUrlPolicy` + `DAL/Imaging/HttpImageFetcher` |
| What counts as a usable image | `Artworks/ArtworkLogic` + `ImageLimits` |
| Nothing prints without human approval | `Ordering/DesignLogic.ValidateForOrderAsync` |
| Artwork fits the print area, and prints sharply | `Ordering/DesignLogic.CheckPlacement` |
| Rights attested; date, capacity, size valid | `Ordering/OrderLogic.PlaceAsync` |
| Anonymous input never reaches customer records | `Ordering/OrderRequestLogic` |
| Lockout, enumeration resistance, last-Admin guards | `Identity/UserLogic` |
| A studio cannot change its own tier | `Shared/StudioLogic` + the view model having no tier property |

---

## Known gaps

**Single-tenant, and honestly so.** Unlike Greg's Auto, there is no `StudioId`
column on anything. `StudioContextProvider` serves the lowest `StudioId` and
every query returns every row. That's correct for one studio and would need the
full treatment — global query filters *before* tenant resolution — before a
second one shares a deployment. Nothing here pretends otherwise, which is the
improvement over Greg's Auto's half-built version.

**No per-record ownership checks.** Every staff action takes a bare `id`.
Correct while there is one studio.

**The designer is anonymous and session-backed.** An in-progress design lives in
`HttpContext.Session`, not the database, so a visitor who wanders off doesn't
leave a half-made design behind. The session is in-memory — a second web server,
or a restart, loses in-progress designs. Saved designs are unaffected.

**Orphaned artwork accumulates.** An image fetched and then abandoned before the
design is saved stays on disk and in the table forever. Nothing sweeps it. Worth
a job before this runs for real.

**`Scripts/` is gitignored.** The SQL that builds this schema is local only, same
arrangement as Greg's Auto. A fresh clone cannot create the database. **Back
those files up somewhere off this disk.**

**Prices and capacity in the seed data are invented.** 60 garments a day and a
3-day turnaround are placeholders. They're questions for whoever runs the press,
not defaults to rely on.

## Tests

**Spreading Some Joy Designs.Tests** (182) — business rules against in-memory
fakes. Fast, no database, run constantly.

There is no smoke-test suite yet. Greg's Auto has one, and it exists there
because unit tests are structurally blind to a class of failure that only shows
up against a real database (a migration once added a NOT NULL column with no
default, breaking every INSERT while every unit test stayed green). The same
exposure applies here. Worth adding.
