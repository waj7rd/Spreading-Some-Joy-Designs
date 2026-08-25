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

## Designs are addressed by token, never by key

`/Orders/Place` is anonymous by necessity — a customer has no account. It used
to take `designId`, a sequential integer, which meant anyone could count upwards
and read every design ever made, artwork included. On a site built around people
uploading their own pictures that's a real disclosure.

`Designs.PublicToken` is a GUID, and it's what appears in URLs. **It is not a
permission check** — anyone holding the link can see the design, which is what
makes a shareable link work. What it prevents is enumeration.

The POST takes the token too. Leaving the key on the form would have been the
way back in.

The migration adds the column nullable, backfills with `NEWID()` per row, *then*
tightens to NOT NULL with a default. Adding it NOT NULL with a default in one
step would have given every existing row the same token.

## Security headers, and why `nosniff` in particular

Set in `Program.cs` on every response. The one that earns its place is
`X-Content-Type-Options: nosniff`, because this application serves bytes
supplied by strangers from its own origin at `/Artwork/File`. Images are decoded
and re-encoded before storage so a polyglot doesn't survive, but without
`nosniff` a browser may still ignore our `Content-Type` and sniff a crafted file
as HTML — stored XSS on the same origin as the staff session cookie.

`style-src` allows `'unsafe-inline'` because artwork placement is expressed as
per-request style attributes. **`script-src` does not**, which is the half that
matters — and it's why the two inline event handlers that used to exist (the
logo fallback and the garment picker) now live in `site.js`. A blocked inline
handler fails silently; if you add one, it will simply stop working.

## `ForwardedHeaders` is off by default, deliberately

Behind a reverse proxy the app must trust `X-Forwarded-For`, or every visitor
lands in one rate-limit bucket and every `LoginAudit` row records the proxy's
address.

But the header is client-supplied. Trusting it while the app is directly
reachable lets anyone forge an IP and walk past the login and artwork-fetch
limits. So it's opt-in via configuration, and the known proxy addresses have to
be listed — turning it on is a deployment decision, not a default.

## Studio designs do not bypass the approval gate

`Designs.IsStudioDesign` marks artwork the studio made itself, offered from the
shop. It carries none of the provenance risk of a customer-supplied image.

The obvious implementation is a branch in `ValidateForOrderAsync` that skips the
approval check for studio designs. **That is deliberately not what happens.**

Instead, artwork added by a signed-in staff member is created `Approved` and
attributed to them (`ArtworkLogic.StoreAsync`, `approvedByUserId`). A studio
design then passes the same gate as everything else, honestly.

The reason is that a bypass branch is a second path to the press, and second
paths get reached by accident — a flag set wrongly, a query that forgets a
filter, a future feature that copies a design. There is no branch here to get
wrong: if the artwork isn't `Approved`, it doesn't print, whoever made it.

`StudioDesignTests.A_studio_design_with_unapproved_artwork_is_still_refused` is
the guard. If someone adds a bypass later, that test fails.

Two related details:

- **Re-uploading a rejected image does not un-reject it**, even for staff. The
  dedupe returns the existing row untouched. Reversing a rejection is what the
  Approve button is for, where it's an explicit act by a named person.
- **The shop hides designs whose garment has been archived.** Otherwise they
  stay orderable right until `OrderLogic` refuses at the last step, which reads
  as the site being broken rather than the product being discontinued.

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

This is why resolution can't be checked at upload time: the designer computes it
live from the placement, against `ImageLimits.MinimumDpi`.

Below 150 DPI is **warned about, not refused**. `DesignLogic.CheckPlacement`
enforces the geometry — minimum size, inside the print area — and stops there.
The studio reviews every piece of artwork before it goes to press, so Karrie
makes the resolution call on the job in front of her; a soft warning she can
overrule beats a hard block she has to work around.

The warning states the width that *would* work — `ImageLimits.MaxPrintableWidthMm`
— or the customer is left guessing at sizes.

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

`Orders.ShippingFee` follows the same rule for the same reason. It is copied
from the studio record when the order is placed, never read live. A studio
putting its postage up next month must not restate what a customer already
agreed to.

## Postage is a charge on the order, not a line on it

`Order.Total` used to be defined as the sum of the lines. It is now
`Subtotal + ShippingFee`, where `Subtotal` is that old sum.

Postage could have been an `OrderLine` instead, and that would have been worse.
A line has a design, a size and a quantity, and it lands in every count of how
many garments the press has to run — so a shipped order would have quietly eaten
a slot of the day's capacity for a cardboard box. Verified:
`OrderLogicTests.Postage_does_not_compete_for_press_capacity`.

Existing orders carry `ShippingFee = 0`, so `Total` still equals `Subtotal` for
every order placed before shipping existed. Nothing was restated by adding this.

## Shipping is a switch on the studio, not a tier feature

`Studios.OffersShipping` is off by default, and turning it on is something the
studio does from the settings screen — the same place capacity, turnaround and
closed days live. It describes how this shop operates, not what it has paid for,
which is why it isn't a `Feature` on the licensing tier.

With it off, the storefront never renders the ship-or-collect choice **and**
`Fulfilment.Check` refuses a shipping request anyway. Both, not either: the form
is a suggestion, and the switch can change while somebody has the page open.

The rules are applied again at acceptance rather than only at submission, so a
request that was submitted while shipping was on is refused if it's still in the
queue when the studio turns shipping off. Same shape as a request stranded by a
day filling up. Verified:
`OrderRequestLogicTests.Turning_shipping_off_after_a_request_was_submitted_blocks_the_acceptance`.

A collection order is never refused for any of this — switching postage off must
not stop the studio taking orders.

## A collection order stores no address at all

Not a partial one. `Fulfilment.ToStore` drops whatever was posted when the
method is collection, so a half-filled address can't sit on a collection order
waiting for somebody — or something — to read it as a shipping label.

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

## The gang sheet is the last gate before the film

A gang sheet packs many transfers onto one piece of DTF film, which is bought by
the width and charged by the length. It is also the last thing that happens
before ink meets film — which makes it the last place the artwork approval gate
can be applied, and the most important one.

So it is applied **twice**. `GangSheetLogic.AddItemsAsync` refuses artwork that
isn't `Approved`, and `MarkReadyAsync` checks every item on the sheet again.
Same shape as `Fulfilment.Check` running at both submission and acceptance, and
for the same reason: a draft can sit open for days, and the state of the artwork
when a transfer was added says nothing about the state of it now. The first
check is a courtesy so nobody builds a sheet they can't print; the second is the
one that counts. Verified:
`GangSheetLogicTests.Artwork_rejected_after_it_was_added_stops_the_sheet_going_to_the_press`.

**One row per physical copy, not a row with a quantity.** Twelve shirts needing
the same front is twelve `GangSheetItems`, because twelve of them have to be
somewhere on the film and each has its own position and its own cut. A quantity
column would have meant the layout couldn't describe itself.

**Sizes are snapshotted onto the item**, the same rule `OrderLines.UnitPrice`
follows. Somebody resizing artwork in the designer next week must not silently
restate a sheet that has already been packed — or printed.

**A transfer that didn't fit stays on the sheet, flagged.** `IsPlaced` is false
and the screen lists it. Dropping it silently is how a customer's order goes
missing, and a sheet with anything unplaced can't be marked ready.

**Gang sheets are not tier-gated**, and there is no studio switch for them
either. This is how the shop gets shirts printed, not a capability it buys — the
same reasoning that keeps shipping a switch on the studio rather than a
`Feature`, taken one step further. The controller is gated on `ManageOrders`,
because packing film is the same job as working the production board and the
same people do it.

## There are two ways onto a sheet, and one gate

Two entry points share one set of tables and one packer:

- **`/GangSheets`** — staff, `ManageOrders`. Packs transfers off open orders.
  These are `GangSheetOrigin.Studio`, have no customer and no price.
- **`/SheetBuilder`** — anonymous. A visitor arranges their own images on a
  sheet of film and buys it. These become `GangSheetOrigin.Customer`, with a
  customer and a price.

The public one is a second road to the same press, which is the entire risk in
it. Two things keep that safe.

**Nothing anonymous becomes a Customer or a GangSheet.** Submitting writes a
`GangSheetRequest` — a holding table, exactly like `OrderRequests` and for the
same reason. `AcceptAsync` creates the customer and the sheet inside one
`IUnitOfWork.ExecuteAsync`, so a refused acceptance leaves no orphan. Verified:
`GangSheetRequestLogicTests.A_refused_acceptance_leaves_no_customer_behind`.

**Acceptance goes through `GangSheetLogic.AddItemsAsync`, not around it.** That
is the same method the staff tool uses and the same approval gate. A customer's
transfers land on film by the identical code path as the studio's, so there is
no second gate to get wrong — the point `StudioDesignTests` makes about the
approval bypass that deliberately doesn't exist, applied again here.

Two consequences worth knowing:

- **Unapproved artwork doesn't stop somebody asking, only staff accepting.** The
  order form works the same way. Refusing at the point a customer is trying to
  pay would put the moderation queue in front of the till.
- **The acceptance check loads each `Artwork` by id rather than reading the
  `Artwork` navigation property.** The repository does `Include` it, but an
  unloaded navigation reads as null, null reads as "not approved", and the
  failure mode is a queue nobody can ever accept from. A check this important
  must not depend on a caller having remembered something.

### The builder posts back on every change

Adding an image, resizing it, changing the sheet size — all full page loads, and
the server repacks each time. A bit of JavaScript would feel smoother, and it
is deliberately not there: the preview has to be laid out by the same packer
that lays out the real sheet, and a second implementation in the browser would
be a second answer to "where does this go", one of which is wrong at exactly the
wrong moment.

The in-progress sheet lives in `HttpContext.Session` as JSON, like the designer.
A visitor who wanders off leaves nothing behind but the artwork they fetched.

### Sheets are sold as fixed sizes

`GangSheetSizes` is a small catalogue with a price per sheet, separate from
`Products` because a sheet of film is not a garment — no blank cost, no size
run, no per-side print charge, and putting it in `Products` would have meant a
third of the rows carrying meaningless values in all of those columns.

Fixed sizes rather than a price per inch, because that is how film is bought
from a supplier and because a price that moves while somebody is still
arranging their images is a price they can't decide against. `PriceQuoted` on
the request and `Price` on the sheet are both snapshots, so putting prices up
never restates what a customer already agreed to. Same rule as
`OrderLines.UnitPrice`, third time it appears in this schema.

`GangSheetSide.Any` exists for these. A transfer on a customer's own sheet isn't
destined for the front or the back of anything — a cut list that claimed
otherwise would be telling the person at the bench something untrue.

### Packing

`Production/GangSheetPacker` is pure functions over rectangles, separate from
everything that touches a database, for the same reason `ImageUrlPolicy` and
`Pricing` are: where a transfer ends up on a piece of film is the sort of thing
somebody argues with, and an argument is only settleable if it can be run.

The algorithm is first-fit decreasing height — shelf packing. **It is not
optimal**; optimal rectangle packing is NP-hard and a sheet of film costs a few
dollars. It is *predictable*, which matters more here: the same transfers pack
the same way every time, so repacking after one removal doesn't rearrange the
sheet wholesale under somebody who had already started cutting. Ties are broken
on the item id for exactly that reason.

Rotation is a **fallback, not an optimisation** — a transfer is turned only when
it wouldn't otherwise fit across the film. A cut list full of sideways transfers
nobody asked for is its own kind of mistake.

`UsedLengthMm` is stored rather than recomputed on read. It is the number the
film is charged by, and the layout it came from is the one that got printed.

## Things that are enforced, and where

| Rule | Enforced in |
|---|---|
| Which URLs the server will fetch | `Artworks/ImageUrlPolicy` + `DAL/Imaging/HttpImageFetcher` |
| What counts as a usable image | `Artworks/ArtworkLogic` + `ImageLimits` |
| Nothing prints without human approval — studio designs included | `Ordering/DesignLogic.ValidateForOrderAsync` |
| Artwork fits the print area (resolution is warned about, not enforced) | `Ordering/DesignLogic.CheckPlacement` |
| Rights attested; date, capacity, size valid | `Ordering/OrderLogic.PlaceAsync` |
| Shipping needs a full address, and a studio that ships | `EntityModels/Fulfilment.Check`, from both `OrderLogic.PlaceAsync` and `OrderRequestLogic` |
| Anonymous input never reaches customer records | `Ordering/OrderRequestLogic` |
| Nothing unapproved reaches the film — checked again at the press | `Production/GangSheetLogic.AddItemsAsync` **and** `MarkReadyAsync` |
| A stranger's sheet never becomes a customer or a sheet unaccepted | `Production/GangSheetRequestLogic` |
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

**The staff sheet tool only offers what's on an open order.**
`GangSheetItems.OrderLineId` is nullable and the logic layer handles a transfer
with no order behind it — that is how customer sheets work — but the staff
screen has no way to add a loose one. A reprint or a spare has to go on as a
copy of an order line. Adding "put any approved artwork on the sheet at a size I
type" is a screen, not a schema change.

**A customer gang sheet is not an `Order`.** Accepting one creates a `GangSheet`
owned by a `Customer` with the price snapshotted onto it, and nothing else. It
does not appear on the production board, it isn't in any order total, and the
capacity check knows nothing about it.

That was a deliberate stop, not an oversight. Making it an `Order` means an
`OrderLine` with no `DesignId`, and `Order.GarmentCount` sums line quantities —
so a 40-transfer sheet would instantly book 40 garments of press capacity for a
job with no garments in it. That is the same trap the postage decision above
describes, with a bigger blast radius, and it wants its own think rather than
being bolted on. **Until then, a customer sheet is invoiced by hand.**

**Nothing tells an order it has been printed.** A sheet marked printed doesn't
move the orders whose transfers were on it — somebody still moves them across
the production board by hand. Making that automatic means deciding what happens
when an order is spread over two sheets, which is a real question and not one to
answer by guessing.

**The packer is deliberately not optimal.** See above. If film cost becomes worth
optimising, the thing to change is `GangSheetPacker.Pack` alone — it's a pure
function with its own test file, and nothing else knows how it decides.

## Tests

**Spreading Some Joy Designs.Tests** (300) — business rules against in-memory
fakes. Fast, no database, run constantly.

There is no smoke-test suite yet. Greg's Auto has one, and it exists there
because unit tests are structurally blind to a class of failure that only shows
up against a real database (a migration once added a NOT NULL column with no
default, breaking every INSERT while every unit test stayed green). The same
exposure applies here. Worth adding.

The shipping columns were added with that in mind: every one of them is either
nullable or `NOT NULL` with a default, so no existing `INSERT` in the codebase
breaks. That was checked against the real database rather than assumed — but by
hand, once, which is exactly the thing a smoke-test suite would do on every run.

**And then it happened anyway.** `AddShipping.sql` was written, gitignored along
with the rest of `Scripts/`, and never made it onto the database — every page
died with `Invalid column name 'OffersShipping'` while all 241 unit tests stayed
green, because EF selects every mapped property and one missing column breaks the
whole query rather than just the feature. The script has been rewritten and run,
and the columns are in `CreateDatabase.sql` now too. This is the exact failure the
paragraph above describes, and it is the argument for the smoke-test suite: it
took a missing table to notice, not a failing test.

`AddGangSheets.sql` and `AddCustomerGangSheets.sql` carry the same warning. Between
them they create five tables and add four columns, they are gitignored, and a
fresh clone will not have them.
