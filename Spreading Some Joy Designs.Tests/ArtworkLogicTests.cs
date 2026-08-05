using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class ArtworkLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);

    private static (ArtworkLogic Logic, FakeArtworkRepository Repo, FakeImageStore Store, FakeImageInspector Inspector)
        Build(IImageFetcher? fetcher = null, IImageInspector? inspector = null)
    {
        var repo = new FakeArtworkRepository();
        var store = new FakeImageStore();
        var insp = (FakeImageInspector)(inspector ?? FakeImageInspector.Returning(2000, 2000));

        var logic = new ArtworkLogic(
            repo,
            fetcher ?? FakeImageFetcher.Returning([1, 2, 3]),
            insp,
            store,
            new FixedStudioClock(Now));

        return (logic, repo, store, insp);
    }

    [Fact]
    public async Task Stores_a_fetched_image_as_pending()
    {
        var (logic, repo, store, _) = Build();

        var result = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        Assert.True(result.Success);

        var artwork = Assert.Single(repo.All);
        Assert.Equal(ArtworkStatus.Pending, artwork.Status);
        Assert.Equal("https://example.com/cat.png", artwork.SourceUrl);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task Refuses_a_url_the_policy_rejects_without_fetching_it()
    {
        var fetcher = FakeImageFetcher.Returning([1, 2, 3]);
        var (logic, repo, _, _) = Build(fetcher);

        var result = await logic.AddFromUrlAsync("file:///C:/secrets.txt", customerId: null);

        Assert.False(result.Success);
        Assert.Empty(repo.All);

        // The point: no network call happened at all.
        Assert.Empty(fetcher.RequestedUrls);
    }

    [Fact]
    public async Task Reports_the_fetchers_refusal_unchanged()
    {
        var (logic, repo, _, _) = Build(FakeImageFetcher.Failing("We couldn't reach that address."));

        var result = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        Assert.False(result.Success);
        Assert.Equal("We couldn't reach that address.", result.ErrorMessage);
        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task Refuses_bytes_the_inspector_cannot_decode()
    {
        // The Content-Type header said image/png; the bytes are something else.
        // This is the check that stops a disguised file reaching the store.
        var (logic, repo, store, _) = Build(inspector: FakeImageInspector.Rejecting());

        var result = await logic.AddFromUploadAsync([0x4D, 0x5A], "totally-a-png.png", customerId: null);

        Assert.False(result.Success);
        Assert.Empty(repo.All);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task Refuses_an_image_too_small_to_print()
    {
        var (logic, repo, _, _) = Build(inspector: FakeImageInspector.Returning(32, 32));

        var result = await logic.AddFromUploadAsync([1, 2, 3], "favicon.png", customerId: null);

        Assert.False(result.Success);
        Assert.Contains("32", result.ErrorMessage);
        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task Refuses_a_decompression_bomb_before_decoding_it()
    {
        // Small on disk, 2.5 billion pixels in memory.
        var (logic, repo, _, inspector) = Build(inspector: FakeImageInspector.Returning(50_000, 50_000));

        var result = await logic.AddFromUploadAsync([1, 2, 3], "bomb.png", customerId: null);

        Assert.False(result.Success);
        Assert.Empty(repo.All);

        // Never normalised — that's the step that would have allocated the bitmap.
        Assert.Equal(0, inspector.NormaliseCount);
    }

    [Fact]
    public async Task Refuses_bytes_over_the_size_ceiling()
    {
        var (logic, repo, _, _) = Build();

        var huge = new byte[ImageLimits.MaxBytes + 1];
        var result = await logic.AddFromUploadAsync(huge, "huge.png", customerId: null);

        Assert.False(result.Success);
        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task Identical_bytes_reuse_the_existing_row()
    {
        var (logic, repo, store, _) = Build();

        var first = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);
        var second = await logic.AddFromUrlAsync("https://other.example/copy.png", customerId: null);

        Assert.True(second.Success);
        Assert.True(second.WasDeduplicated);
        Assert.Equal(first.ArtworkId, second.ArtworkId);

        Assert.Single(repo.All);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task A_rejected_image_comes_straight_back_rejected_under_a_new_url()
    {
        // The reason dedupe is keyed on the content hash rather than the URL:
        // otherwise re-hosting a rejected picture is enough to get it printed.
        var (logic, repo, _, _) = Build();

        var first = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);
        await logic.RejectAsync(first.ArtworkId, reviewedByUserId: 7, "Someone else's photograph.");

        var second = await logic.AddFromUrlAsync("https://mirror.example/same-picture.png", customerId: null);

        Assert.True(second.Success);
        Assert.Equal(first.ArtworkId, second.ArtworkId);

        var artwork = Assert.Single(repo.All);
        Assert.Equal(ArtworkStatus.Rejected, artwork.Status);
        Assert.Equal("Someone else's photograph.", artwork.RejectionReason);
    }

    [Fact]
    public async Task Hashes_the_normalised_bytes_not_the_original()
    {
        // The stored file is what a moderator saw and what the press prints, so
        // that's what the hash has to identify. Hashing the input would make the
        // same picture with different EXIF count as two images.
        var (logic, repo, store, inspector) = Build();

        await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        var artwork = Assert.Single(repo.All);
        Assert.Equal(1, inspector.NormaliseCount);

        var stored = store.Files[artwork.StoredFileName];
        Assert.Equal(stored.LongLength, artwork.ByteSize);
    }

    [Fact]
    public async Task Approving_records_who_and_when()
    {
        var (logic, repo, _, _) = Build();
        var added = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        var result = await logic.ApproveAsync(added.ArtworkId, reviewedByUserId: 3);

        Assert.True(result.Success);

        var artwork = Assert.Single(repo.All);
        Assert.Equal(ArtworkStatus.Approved, artwork.Status);
        Assert.Equal(3, artwork.ReviewedByUserId);
        Assert.Equal(Now, artwork.ReviewedAt);
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_refused()
    {
        var (logic, _, _, _) = Build();
        var added = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        var result = await logic.RejectAsync(added.ArtworkId, reviewedByUserId: 3, "   ");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approving_clears_a_previous_rejection_reason()
    {
        var (logic, repo, _, _) = Build();
        var added = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        await logic.RejectAsync(added.ArtworkId, 3, "Wrong on second look.");
        await logic.ApproveAsync(added.ArtworkId, 3);

        var artwork = Assert.Single(repo.All);
        Assert.Equal(ArtworkStatus.Approved, artwork.Status);
        Assert.Null(artwork.RejectionReason);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var (logic, repo, _, _) = Build();

        var result = await logic.AddFromUploadAsync([], "empty.png", customerId: null);

        Assert.False(result.Success);
        Assert.Empty(repo.All);
    }
}
