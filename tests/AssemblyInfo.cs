using Xunit;

// Serialize the whole test assembly. Log (src/FourExHex.Model/Log.cs)
// keeps its sink and per-category level array as process-wide statics.
// LogTests opens those gates and installs a sink that captures into a
// local List<string>; meanwhile any other test exercising HeuristicAi,
// GameController, or GameOperations emits Log.* calls that — with the
// gate open and sink installed — land in LogTests' list and corrupt its
// assertions (extra strings, or "Collection was modified" mid-enumeration).
// The suite is small (~4s serialized), so global serialization is
// cheaper than per-class collection plumbing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
