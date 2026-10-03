using System.Globalization;
using System.Text.Json.Nodes;
using Plugview.Core.Audio;

namespace Plugview.Core.Tests;

public sealed class AudioTests
{
    [Fact]
    public void CardQuirksMatchTheSpec()
    {
        var spec = Fixture.Load("card-quirks.json");
        foreach (var testCase in spec["detect"].Items().OfType<JsonNode>())
        {
            static ushort Hex(JsonNode? node) => ushort.Parse(node.Text(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var quirk = CardAudioQuirks.Detect(
                Hex(testCase["vendorId"]), Hex(testCase["productId"]), (int)testCase["declaredSampleRate"].Integer(), (int)testCase["declaredChannels"].Integer());
            CardAudioQuirk? expected = testCase["expected"].OptionalText() switch
            {
                null => null,
                "ms2109InterleavedStereo" => CardAudioQuirk.Ms2109InterleavedStereo,
                var other => throw new InvalidDataException($"unknown quirk {other}"),
            };
            Assert.True(expected == quirk, testCase.Description());
        }
        foreach (var testCase in spec["ms2109"].Items().OfType<JsonNode>())
        {
            var input = testCase["declaredMono"].Items().Select(sample => (short)sample.Integer()).ToArray();
            var expected = testCase["expectedStereo"].Items().Select(sample => (short)sample.Integer()).ToArray();
            var output = new short[input.Length];
            var frames = CardAudioQuirks.ReadMs2109(input, output);
            Assert.Equal(expected, output[..(frames * 2)]);
        }
    }

    private const int SampleRate = 48_000;
    private const int FramesPerPeriod = 480;
    private const double PeriodSeconds = 0.01;
    private const double TargetMilliseconds = 20;
    private const double CapMilliseconds = 60;

    /// <summary>Two hours of a card clock running fast or slow against the speakers, with jittery 10 ms packets.</summary>
    [Theory]
    [InlineData(-300)]
    [InlineData(0)]
    [InlineData(300)]
    public void HoldsTheDelayAgainstClockDrift(double cardClockPpm)
    {
        var controller = new AudioDriftController(SampleRate, TargetMilliseconds, CapMilliseconds);
        var random = new Random(4815);
        double buffered = SampleRate * TargetMilliseconds / 1000;
        double produced = 0;
        var heldPackets = 0;
        var resets = 0;
        var lastHourSum = 0.0;
        var lastHourCount = 0;
        var worstLastHourError = 0.0;
        var steps = (int)(2 * 60 * 60 / PeriodSeconds);

        for (var step = 0; step < steps; step++)
        {
            // The card's packets sometimes arrive one period late, then two at once (never two periods late).
            produced += FramesPerPeriod * (1 + (cardClockPpm / 1_000_000));
            var isLate = heldPackets == 0 && random.NextDouble() < 0.05;
            if (isLate)
            {
                heldPackets++;
            }
            else
            {
                var whole = Math.Floor(produced);
                buffered += whole;
                produced -= whole;
                heldPackets = 0;
            }

            // The delay is held where the controller reads it: before this period's frames are played.
            if (step >= steps / 2)
            {
                var errorMilliseconds = ((buffered * 1000) / SampleRate) - TargetMilliseconds;
                lastHourSum += errorMilliseconds;
                lastHourCount++;
                worstLastHourError = Math.Max(worstLastHourError, Math.Abs(errorMilliseconds));
            }

            switch (controller.Update((int)buffered, PeriodSeconds))
            {
                case DriftAction.Play play:
                    buffered = Math.Max(0, buffered - (FramesPerPeriod * play.InputFramesPerOutputFrame));
                    break;
                case DriftAction.DropTo drop:
                    resets++;
                    buffered = drop.TargetFrames;
                    break;
                case DriftAction.Refill refill:
                    resets++;
                    buffered = refill.TargetFrames;
                    break;
            }

        }

        Assert.Equal(0, resets);
        Assert.InRange(lastHourSum / lastHourCount, -2, 2);
        Assert.InRange(worstLastHourError, 0, 20);
    }

    [Fact]
    public void DropsToTheTargetAfterAStallAndRefillsWhenDry()
    {
        var controller = new AudioDriftController(SampleRate, TargetMilliseconds, CapMilliseconds);
        Assert.Equal(new DriftAction.DropTo(960), controller.Update(SampleRate, PeriodSeconds));
        Assert.Equal(new DriftAction.Refill(960), controller.Update(0, PeriodSeconds));
        Assert.IsType<DriftAction.Play>(controller.Update(960, PeriodSeconds));
    }

    [Fact]
    public void NeverCorrectsMoreThanTheClamp()
    {
        var controller = new AudioDriftController(SampleRate, TargetMilliseconds, CapMilliseconds);
        for (var step = 0; step < 10_000; step++)
        {
            var action = Assert.IsType<DriftAction.Play>(controller.Update(2800, PeriodSeconds));
            Assert.InRange(action.InputFramesPerOutputFrame, 1 - 0.001, 1 + 0.001);
        }
    }
}
