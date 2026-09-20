using Xunit;

// These tests are not parallel-safe, and pretending otherwise would be a latent bug rather than a
// speed-up.
//
// Each fixture hosts the real application against its own database, and the application reads its
// configuration from process-wide environment variables before any of the factory's callbacks run.
// ConfiguredApiFactory closes that window by applying a fixture's variables under a lock held for
// exactly as long as its host is being built, so host construction is safe on its own. What is
// still not safe is the state underneath: fixtures share a media root, and the rules under test
// count rows - submissions inside a window, messages waiting in the outbox - so two collections
// writing at once would make those counts depend on timing.
//
// Tests inside a collection already run one at a time; this makes the collections do the same.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
