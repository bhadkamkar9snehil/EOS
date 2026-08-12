using EngineeringPerformance.Application;
using EngineeringPerformance.UI;

namespace EngineeringPerformance.Domain.Tests;

public sealed class PeerAnalyticsTests
{
    [Fact]
    public void SparsePerfectRatingsRemainProvisionalAndFollowEstablishedSignals()
    {
        var reviews = new List<PeerReviewItem>();
        AddRatings(reviews, "Supported", [4.8m, 4.7m, 4.6m, 4.8m, 4.7m, 4.6m]);
        AddRatings(reviews, "Typical A", [4.5m, 4.4m, 4.3m, 4.5m, 4.4m]);
        AddRatings(reviews, "Typical B", [4.3m, 4.2m, 4.1m, 4.3m, 4.2m]);
        AddRatings(reviews, "Sparse Perfect", [5m, 5m]);

        var summary = Analytics.Peers(reviews);
        var sparse = summary.Standings.Single(x => x.Name == "Sparse Perfect");
        var supported = summary.Standings.Single(x => x.Name == "Supported");

        Assert.Equal(5, summary.EvidenceCoverageTarget);
        Assert.True(supported.IsEstablished);
        Assert.False(sparse.IsEstablished);
        Assert.True(sparse.Average - sparse.ConfidenceLowerBound > supported.Average - supported.ConfidenceLowerBound);
        var sparseAspect = Analytics.ReliableAspectEstimate(sparse, 5m, summary.AverageRating, summary.ModelPriorStrength);
        var supportedAspect = Analytics.ReliableAspectEstimate(supported, 4.8m, summary.AverageRating, summary.ModelPriorStrength);
        Assert.True(sparseAspect < supportedAspect, $"Sparse aspect {sparseAspect} should remain below supported aspect {supportedAspect}.");
        Assert.All(summary.Standings.TakeWhile(x => x.IsEstablished), x => Assert.True(x.IsEstablished));
        var ordered = summary.Standings.ToArray();
        Assert.True(Array.IndexOf(ordered, supported) < Array.IndexOf(ordered, sparse));
    }

    [Fact]
    public void DuplicateRowsFromOneReviewerDoNotCreateExtraEvidence()
    {
        var reviews = new List<PeerReviewItem>();
        AddRatings(reviews, "Person", [5m, 4.8m]);
        reviews.Add(Review("Reviewer 1", "Person", 5m));

        var standing = Analytics.Peers(reviews).Standings.Single();

        Assert.Equal(2, standing.ReviewsReceived);
    }

    private static void AddRatings(ICollection<PeerReviewItem> destination, string subject, IReadOnlyList<decimal> ratings)
    {
        for (var index = 0; index < ratings.Count; index++)
            destination.Add(Review($"Reviewer {index + 1}", subject, ratings[index]));
    }

    private static PeerReviewItem Review(string reviewer, string subject, decimal rating) =>
        new(reviewer, reviewer, subject, subject, rating, rating, rating, rating, rating, null);
}
