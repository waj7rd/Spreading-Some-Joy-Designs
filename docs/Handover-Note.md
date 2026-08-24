# Handover note

A plain-English note to send with the site. Adapt the wording — it should sound
like you.

Two things it's quietly doing beyond being friendly: it puts the "what we can't
print" guidance **in writing and dated**, and it makes clear who owns the site
and who decides what goes to the press. Both matter if anyone ever asks. Keep a
copy of what you actually send.

---

Hi Karrie,

The website's ready. It's yours — no charge, no strings, and you can do whatever
you like with it.

**What it does**

Two ways for people to order:

- **Shop our designs** — the ones you make yourself. Customers pick one, choose a
  size, and order. Nothing to upload, nothing to wait for.
- **Design your own** — a customer uploads their own artwork, or pastes a link to
  it, and places it on the shirt front and back.

Either way the order lands on your **Requests** screen. You set the date and
accept it, and it moves to the **Production** board so you can see what's due
when.

It also keeps your customer list, your garment prices, and a record of every
order — so "what did I charge Sarah last March" becomes something you can look
up rather than remember.

**Making your own designs**

Sign in, open **Design Yours**, add your artwork, and save. Because you're signed
in it goes straight to the shop rather than to checkout, and there's nothing to
review — you made it, so it's approved automatically.

Those are worth leaning on. They're quicker for you, better margin, and they
carry none of the hassle below.

**The Artwork screen — please use it**

Anything a customer uploads waits there until you approve it. Nothing reaches
the press before you've looked at it. That's on purpose, and it's the part that
protects you.

The rule of thumb I'd suggest: **if you're not sure, reject it and ask.** There's
a box for the reason and the customer sees it. A slightly awkward message beats
the alternative.

**What we can't print**

This is the bit worth being firm about, because it's the printer — you — who
gets the letter, not the customer who asked. If an image has any of these in it,
say no unless they can show you a licence:

- Sports teams and leagues, and college or school logos
- Cartoon and film characters — Disney, Marvel, anime, that sort of thing
- Band and music logos
- Any company's logo, unless it's their own business asking
- Photos of famous people

The awkward truth is these are exactly what people ask for. "I found it on
Google" isn't permission — someone owns nearly every image online, and printing
it on something you sell is the part that causes trouble.

Your own artwork, something a customer genuinely made, a design bought with a
licence that covers printing for resale, or something clearly out of copyright:
all fine.

**One thing I'd really encourage**

If you're taking money from people who aren't friends and family, set up an LLC
before you go much further. It's usually a couple of hundred dollars and an
afternoon. It doesn't stop problems, but it keeps a business problem from
reaching your house and savings. Genuinely the highest-value hour available
here — more than anything on the website.

**Where things stand**

The site is yours to run. I'm not hosting it and I'm not going to be the one
approving artwork — what goes to the press is your call, and it should be, since
you're the one who knows the customers.

Happy to help with anything technical whenever you need it.

— Will

---

## Details to include separately

Don't put these in the same message you might forward around.

- **Sign-in:** the three staff accounts, and get her to change the passwords
  immediately — Account → Change your password
- **Roles:** Admin runs everything including staff accounts; Manager does
  everything except staff accounts, including approving artwork; Associate works
  the production board but can't approve artwork
- **The database:** it needs the SQL scripts in `Spreading Some Joy Designs.DAL/Scripts/`,
  which aren't in the GitHub repo. Copy in OneDrive under `Dev Backups`
    - Run order: `CreateDatabase.sql`, `SeedData.sql`, then `AddShipping.sql`
    - `AddShipping.sql` adds the shipping columns and is safe to run more than
      once. Like the others it is **gitignored** — if it isn't in `Dev Backups`,
      it exists on one machine only
