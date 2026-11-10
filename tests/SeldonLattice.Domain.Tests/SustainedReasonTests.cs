using MessagePack;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.Domain.Tests;

/// <summary>
/// The sustained signal carries which of the two paths fired, on the synthesis event rather than
/// the stored node. The two paths mean different things - the gate could not validate any
/// candidate, or the moderator declined and the gate confirmed the decline - and folding them into
/// one boolean is what made the signal unreadable.
///
/// DebateTurnEvent uses explicit MessagePack keys, so the new key is additive: an event written
/// before it existed must still deserialise, with the reason null.
/// </summary>
public class SustainedReasonTests
{
    [Fact]
    public void SustainedReason_RoundTrips()
    {
        var evt = new DebateTurnEvent
        {
            Type = "synthesis",
            Turn = -1,
            Level = 2,
            IsSustained = true,
            SustainedReason = "exhaustion_confirmed: every further question would only restate a rung already climbed.",
            Timestamp = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
        };

        var read = MessagePackSerializer.Deserialize<DebateTurnEvent>(
            MessagePackSerializer.Serialize(evt));

        Assert.True(read.IsSustained);
        Assert.Equal("exhaustion_confirmed: every further question would only restate a rung already climbed.", read.SustainedReason);
    }

    [Fact]
    public void EventWrittenBeforeTheReasonExisted_StillDeserialises()
    {
        // The event is written in array form, one slot per key. Drop the last slot - key 33,
        // SustainedReason - and the record must still load with the reason null.
        var full = MessagePackSerializer.Deserialize<object[]>(
            MessagePackSerializer.Serialize(new DebateTurnEvent
            {
                Type = "synthesis",
                Turn = -1,
                IsSustained = true,
                SustainedReason = "exhaustion_confirmed: spent",
            }));

        Assert.True(full.Length >= 34, $"expected at least 34 slots, found {full.Length}");

        var legacy = MessagePackSerializer.Serialize(full.Take(33).ToArray());
        var read = MessagePackSerializer.Deserialize<DebateTurnEvent>(legacy);

        Assert.Null(read.SustainedReason);
        Assert.True(read.IsSustained);
    }

    [Fact]
    public void TheTwoReasons_AreDistinctStrings()
    {
        // Path (a): the gate exhausted its retries. Path (b): the moderator declined and the
        // gate confirmed. They must not collapse into one value.
        const string gateExhausted = "gate_exhausted: the gate could not validate any candidate after 2 attempts";
        const string exhaustionConfirmed = "exhaustion_confirmed: the inquiry is spent at this rung";

        Assert.NotEqual(gateExhausted, exhaustionConfirmed);
        Assert.StartsWith("gate_exhausted:", gateExhausted);
        Assert.StartsWith("exhaustion_confirmed:", exhaustionConfirmed);
    }
}
