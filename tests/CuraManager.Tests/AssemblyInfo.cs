// MaSch.Core's NotifyPropertyChangedAttribute caches reflection data in a
// non-concurrent static Dictionary without locking, so two threads constructing
// ObservableObject-derived types at once corrupt it. xUnit parallelizes across
// test classes by default, which triggers that roughly one run in six. The whole
// suite runs in well under a second, so serialising costs nothing.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
