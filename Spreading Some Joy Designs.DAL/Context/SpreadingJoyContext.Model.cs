using Microsoft.EntityFrameworkCore;
using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.DAL.Context;

// All model configuration, kept in its own file so the context itself stays
// readable. Constraint names are stated explicitly and match the names in
// Scripts/CreateDatabase.sql — when they drift, EF quietly generates a second
// constraint alongside the one already in the database.
public partial class SpreadingJoyContext
{
    private static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Studio>(entity =>
        {
            entity.ToTable("Studios");
            entity.HasKey(e => e.StudioId).HasName("PK_Studios");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.AddressLine).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.PostalCode).HasMaxLength(20);
            entity.Property(e => e.TimeZoneId).IsRequired().HasMaxLength(100);

            entity.Property(e => e.ClosedDaysRaw)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("ClosedDays");

            entity.Property(e => e.OffersShipping)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Studios_OffersShipping");

            entity.Property(e => e.ShippingFee)
                .HasColumnType("decimal(10, 2)")
                .HasDefaultValue(0m)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Studios_ShippingFee");

            entity.Property(e => e.TierName)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnName("Tier");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Studios_CreatedAt");

            // Computed in C# from ClosedDaysRaw / TierName — not columns.
            entity.Ignore(e => e.ClosedDays);
            entity.Ignore(e => e.Tier);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.ProductId).HasName("PK_Products");

            // Name and colour together identify a garment; neither is unique on
            // its own, because the same tee comes in a dozen colours.
            entity.HasIndex(e => new { e.Name, e.Colour }, "UQ_Products_Name_Colour").IsUnique();

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Colour).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ColourHex).IsRequired().HasMaxLength(7);
            entity.Property(e => e.BasePrice).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.PrintSidePrice).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.ExtendedSizeUpcharge).HasColumnType("decimal(10, 2)");

            entity.Property(e => e.SizesRaw)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("Sizes");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Products_IsActive");

            entity.Ignore(e => e.Sizes);
        });

        modelBuilder.Entity<Artwork>(entity =>
        {
            entity.ToTable("Artworks");
            entity.HasKey(e => e.ArtworkId).HasName("PK_Artworks");

            // The dedupe and the rejection memory both hang off this being
            // unique. Two rows with one hash means an image can be approved and
            // rejected at the same time.
            entity.HasIndex(e => e.Sha256, "UQ_Artworks_Sha256").IsUnique();

            // The moderation queue reads exactly this way: pending, oldest
            // first.
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_Artworks_Status_CreatedAt");

            entity.Property(e => e.SourceUrl).HasMaxLength(2048);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Sha256).IsRequired().HasMaxLength(64).IsFixedLength();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(ArtworkStatus.Pending)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Artworks_Status");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Artworks_CreatedAt");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Artworks_Customers");

            // SetNull, not Cascade: deactivating a user must not erase the
            // record of which images they approved.
            entity.HasOne(d => d.ReviewedByUser).WithMany()
                .HasForeignKey(d => d.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Artworks_Users");
        });

        modelBuilder.Entity<Design>(entity =>
        {
            entity.ToTable("Designs");
            entity.HasKey(e => e.DesignId).HasName("PK_Designs");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Designs_IsActive");

            entity.Property(e => e.IsStudioDesign)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Designs_IsStudioDesign");

            entity.Property(e => e.PublicToken)
                .HasDefaultValueSql("(newid())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Designs_PublicToken");

            // Unique because it's how a design is looked up from a URL, and
            // indexed because that lookup happens on every order page load.
            entity.HasIndex(e => e.PublicToken, "UQ_Designs_PublicToken").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Designs_CreatedAt");

            // The shop lists exactly this: studio designs that are still active,
            // newest first.
            entity.HasIndex(e => new { e.IsStudioDesign, e.IsActive }, "IX_Designs_IsStudioDesign_IsActive");

            entity.HasOne(d => d.Product).WithMany(p => p.Designs)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Designs_Products");

            entity.HasOne(d => d.Customer).WithMany(c => c.Designs)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Designs_Customers");

            // Restrict on both: artwork attached to a design is what the press
            // prints, and a cascade here would silently empty a live design.
            entity.HasOne(d => d.FrontArtwork).WithMany()
                .HasForeignKey(d => d.FrontArtworkId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Designs_Artworks_Front");

            entity.HasOne(d => d.BackArtwork).WithMany()
                .HasForeignKey(d => d.BackArtworkId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Designs_Artworks_Back");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(e => e.CustomerId).HasName("PK_Customers");

            entity.HasIndex(e => e.Email, "UQ_Customers_Email").IsUnique();

            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(30);

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Customers_IsActive");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Customers_CreatedAt");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(e => e.OrderId).HasName("PK_Orders");

            // The capacity check is "everything open due on this date", and it
            // runs on every order placement.
            entity.HasIndex(e => new { e.DueOn, e.Status }, "IX_Orders_DueOn_Status");

            entity.Property(e => e.DueOn).HasColumnType("date");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);

            // Postage is a charge on the order, not a line on it. decimal, never
            // float: money that cannot represent 0.10 exactly has no business in a
            // column somebody is going to be invoiced from.
            entity.Property(e => e.ShippingFee)
                .HasColumnType("decimal(10, 2)")
                .HasDefaultValue(0m)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Orders_ShippingFee");

            entity.Property(e => e.FulfilmentMethod)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(FulfilmentMethod.Pickup)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Orders_FulfilmentMethod");

            entity.Property(e => e.ShipToLine1).HasMaxLength(200);
            entity.Property(e => e.ShipToLine2).HasMaxLength(200);
            entity.Property(e => e.ShipToCity).HasMaxLength(100);
            entity.Property(e => e.ShipToState).HasMaxLength(50);
            entity.Property(e => e.ShipToPostalCode).HasMaxLength(20);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(OrderStatus.Received)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Orders_Status");

            entity.Property(e => e.RightsAttested)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Orders_RightsAttested");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Orders_CreatedAt");

            entity.HasOne(d => d.Customer).WithMany(c => c.Orders)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Orders_Customers");

            // Computed in C# from the lines and the stored fee.
            entity.Ignore(e => e.Subtotal);
            entity.Ignore(e => e.Total);
            entity.Ignore(e => e.GarmentCount);
            entity.Ignore(e => e.IsShipped);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.HasKey(e => e.OrderLineId).HasName("PK_OrderLines");

            entity.Property(e => e.SizeCode).IsRequired().HasMaxLength(10);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(10, 2)");

            // Cascade here and only here: a line has no meaning apart from its
            // order, unlike every other relationship in this schema.
            entity.HasOne(d => d.Order).WithMany(o => o.OrderLines)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_OrderLines_Orders");

            entity.HasOne(d => d.Design).WithMany(d => d.OrderLines)
                .HasForeignKey(d => d.DesignId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OrderLines_Designs");

            entity.Ignore(e => e.LineTotal);
        });

        modelBuilder.Entity<OrderRequest>(entity =>
        {
            entity.ToTable("OrderRequests");
            entity.HasKey(e => e.OrderRequestId).HasName("PK_OrderRequests");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_OrderRequests_Status_CreatedAt");

            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(30);
            entity.Property(e => e.SizeCode).IsRequired().HasMaxLength(10);
            entity.Property(e => e.RequestedFor).HasColumnType("date");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.DeclineReason).HasMaxLength(500);

            entity.Property(e => e.FulfilmentMethod)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(FulfilmentMethod.Pickup)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_OrderRequests_FulfilmentMethod");

            entity.Property(e => e.ShipToLine1).HasMaxLength(200);
            entity.Property(e => e.ShipToLine2).HasMaxLength(200);
            entity.Property(e => e.ShipToCity).HasMaxLength(100);
            entity.Property(e => e.ShipToState).HasMaxLength(50);
            entity.Property(e => e.ShipToPostalCode).HasMaxLength(20);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(OrderRequestStatus.Pending)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_OrderRequests_Status");

            entity.Property(e => e.RightsAttested)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_OrderRequests_RightsAttested");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_OrderRequests_CreatedAt");

            entity.HasOne(d => d.Design).WithMany()
                .HasForeignKey(d => d.DesignId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OrderRequests_Designs");

            entity.HasOne(d => d.HandledByUser).WithMany()
                .HasForeignKey(d => d.HandledByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_OrderRequests_Users");

            entity.HasOne(d => d.Order).WithMany()
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OrderRequests_Orders");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.UserId).HasName("PK_Users");

            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();

            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);

            entity.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(Domain.Identity.Roles.Associate)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_Role");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_IsActive");

            entity.Property(e => e.FailedLoginCount)
                .HasDefaultValue(0)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_FailedLoginCount");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_CreatedAt");
        });

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.ToTable("LoginAudit");
            entity.HasKey(e => e.LoginAuditId).HasName("PK_LoginAudit");

            entity.HasIndex(e => e.OccurredAt, "IX_LoginAudit_OccurredAt");

            entity.Property(e => e.EmailAttempted).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Event).IsRequired().HasMaxLength(20);

            // Long enough for an IPv6 address with a scope id.
            entity.Property(e => e.IpAddress).HasMaxLength(64);

            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_LoginAudit_OccurredAt");

            // SetNull, not Cascade: removing a user must not erase the record of
            // what happened.
            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_LoginAudit_Users");
        });

        modelBuilder.Entity<GangSheet>(entity =>
        {
            entity.ToTable("GangSheets");
            entity.HasKey(e => e.GangSheetId).HasName("PK_GangSheets");

            // The list screen opens on drafts and sheets waiting at the press.
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_GangSheets_Status_CreatedAt");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(GangSheetStatus.Draft)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_Status");

            entity.Property(e => e.GutterMm)
                .HasDefaultValue(Domain.Production.FilmSizes.DefaultGutterMm)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_GutterMm");

            entity.Property(e => e.MarginMm)
                .HasDefaultValue(Domain.Production.FilmSizes.DefaultMarginMm)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_MarginMm");

            entity.Property(e => e.AllowRotation)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_AllowRotation");

            entity.Property(e => e.UsedLengthMm)
                .HasDefaultValue(0)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_UsedLengthMm");

            // 'Studio' or 'Customer'. See Domain/EntityModels/GangSheet.cs.
            entity.Property(e => e.Origin)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(GangSheetOrigin.Studio)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_Origin");

            // Snapshotted from what the customer was quoted, never read live off
            // the catalogue. decimal, never float: money that cannot represent
            // 0.10 exactly has no business in a column somebody is invoiced from.
            entity.Property(e => e.Price)
                .HasColumnType("decimal(10, 2)")
                .HasDefaultValue(0m)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_Price");

            // Restrict on both: a customer with film on order isn't deleted out
            // from under it, and a withdrawn sheet size still has to resolve for
            // the sheets already sold at it.
            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheets_Customers");

            entity.HasOne(d => d.GangSheetSize).WithMany()
                .HasForeignKey(d => d.GangSheetSizeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheets_GangSheetSizes");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheets_CreatedAt");

            // SetNull for the same reason LoginAudit uses it: removing a staff
            // account must not delete the record of film that was printed.
            entity.HasOne(d => d.CreatedByUser).WithMany()
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_GangSheets_Users");

            // Counted in C# from the items — not columns.
            entity.Ignore(e => e.IsEditable);
            entity.Ignore(e => e.PlacedCount);
            entity.Ignore(e => e.UnplacedCount);
            entity.Ignore(e => e.CoveragePercent);
        });

        modelBuilder.Entity<GangSheetItem>(entity =>
        {
            entity.ToTable("GangSheetItems");
            entity.HasKey(e => e.GangSheetItemId).HasName("PK_GangSheetItems");

            // "How many of this order line are already on a sheet" runs on every
            // visit to the build screen.
            entity.HasIndex(e => e.OrderLineId, "IX_GangSheetItems_OrderLineId");

            entity.Property(e => e.Label).IsRequired().HasMaxLength(120);

            entity.Property(e => e.Side)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue(GangSheetSide.Front)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_Side");

            entity.Property(e => e.XMm)
                .HasDefaultValue(0)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_XMm");

            entity.Property(e => e.YMm)
                .HasDefaultValue(0)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_YMm");

            entity.Property(e => e.Rotated)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_Rotated");

            entity.Property(e => e.IsPlaced)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_IsPlaced");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetItems_CreatedAt");

            // Cascade, like OrderLines: a transfer has no meaning apart from the
            // sheet it sits on. Deleting a draft takes its contents with it.
            entity.HasOne(d => d.GangSheet).WithMany(s => s.Items)
                .HasForeignKey(d => d.GangSheetId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_GangSheetItems_GangSheets");

            // Restrict: artwork that has been printed cannot be deleted out from
            // under the record of having printed it.
            entity.HasOne(d => d.Artwork).WithMany()
                .HasForeignKey(d => d.ArtworkId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheetItems_Artworks");

            // SetNull on both: a sheet outlives the order that prompted it, and
            // a transfer added by hand never had one. Losing the link is not
            // losing the transfer — Label carries what the press needs to read.
            entity.HasOne(d => d.OrderLine).WithMany()
                .HasForeignKey(d => d.OrderLineId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_GangSheetItems_OrderLines");

            entity.HasOne(d => d.Design).WithMany()
                .HasForeignKey(d => d.DesignId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_GangSheetItems_Designs");

            // The footprint on the film, which is the stored size with the
            // rotation applied. Computed in C#.
            entity.Ignore(e => e.PlacedWidthMm);
            entity.Ignore(e => e.PlacedHeightMm);
        });

        modelBuilder.Entity<GangSheetSize>(entity =>
        {
            entity.ToTable("GangSheetSizes");
            entity.HasKey(e => e.GangSheetSizeId).HasName("PK_GangSheetSizes");

            entity.HasIndex(e => e.Name, "UQ_GangSheetSizes_Name").IsUnique();

            entity.Property(e => e.Name).IsRequired().HasMaxLength(60);
            entity.Property(e => e.Price).HasColumnType("decimal(10, 2)");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetSizes_IsActive");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetSizes_CreatedAt");

            entity.Ignore(e => e.Dimensions);
        });

        modelBuilder.Entity<GangSheetRequest>(entity =>
        {
            entity.ToTable("GangSheetRequests");
            entity.HasKey(e => e.GangSheetRequestId).HasName("PK_GangSheetRequests");

            // The queue reads exactly this way: pending, oldest first.
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_GangSheetRequests_Status_CreatedAt");

            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.DeclineReason).HasMaxLength(500);

            entity.Property(e => e.PriceQuoted).HasColumnType("decimal(10, 2)");

            entity.Property(e => e.RightsAttested)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetRequests_RightsAttested");

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(GangSheetRequestStatus.Pending)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetRequests_Status");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetRequests_CreatedAt");

            entity.HasOne(d => d.GangSheetSize).WithMany()
                .HasForeignKey(d => d.GangSheetSizeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheetRequests_GangSheetSizes");

            entity.HasOne(d => d.HandledByUser).WithMany()
                .HasForeignKey(d => d.HandledByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_GangSheetRequests_Users");

            // Set on acceptance, so the request keeps pointing at what it became.
            entity.HasOne(d => d.GangSheet).WithMany()
                .HasForeignKey(d => d.GangSheetId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheetRequests_GangSheets");

            entity.Ignore(e => e.TransferCount);
        });

        modelBuilder.Entity<GangSheetRequestItem>(entity =>
        {
            entity.ToTable("GangSheetRequestItems");
            entity.HasKey(e => e.GangSheetRequestItemId).HasName("PK_GangSheetRequestItems");

            entity.Property(e => e.Label).IsRequired().HasMaxLength(120);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_GangSheetRequestItems_CreatedAt");

            // Cascade, like OrderLines and GangSheetItems: an item on a request
            // has no meaning apart from the request it is part of.
            entity.HasOne(d => d.GangSheetRequest).WithMany(r => r.Items)
                .HasForeignKey(d => d.GangSheetRequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_GangSheetRequestItems_GangSheetRequests");

            entity.HasOne(d => d.Artwork).WithMany()
                .HasForeignKey(d => d.ArtworkId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_GangSheetRequestItems_Artworks");
        });
    }
}
