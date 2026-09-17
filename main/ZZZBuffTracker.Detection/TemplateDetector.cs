using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Detection;

public sealed record DetectorDecision(string TemplateId, double Score, string Decision);
public sealed record DetectionResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<DetectorDecision> Decisions);

/// <summary>Aligned grayscale template matching. Score is similarity, not a calibrated probability.</summary>
public sealed class TemplateDetector
{
    private sealed class State
    {
        public int Candidate = -1, Count;
        public int Confirmed = 0;
        public TimeSpan LastEmission = TimeSpan.MinValue;
    }
    private readonly TemplatePack pack;
    private readonly Dictionary<string, State> states = [];
    private TimeSpan lastTime = TimeSpan.MinValue;
    private long lastSequence = -1;
    private string? character;
    private (int Width, int Height)? dimensions;

    public TemplateDetector(TemplatePack pack) { pack.Validate(); this.pack = pack; }

    public DetectionResult Process(GrayFrame frame, TrackingContext context, TimeSpan processedAt)
    {
        frame.Validate();
        if (frame.Sequence <= lastSequence || frame.CapturedAt <= lastTime)
            return new([], [new("*", 0, "StaleFrame")]);
        if (dimensions is { } size && size != (frame.Width, frame.Height))
            throw new InvalidOperationException("Resolution changed; recalibrate and restart capture.");
        dimensions = (frame.Width, frame.Height);
        if (lastTime != TimeSpan.MinValue && frame.CapturedAt - lastTime > TimeSpan.FromMilliseconds(500))
            foreach (var state in states.Values) { state.Count = 0; state.Candidate = -1; }
        lastTime = frame.CapturedAt; lastSequence = frame.Sequence;
        var client = frame.Crop(pack.ClientArea);
        // Aspect ratio changes invalidate HUD placement. Same-aspect 1080p/1440p is supported.
        if (Math.Abs((double)client.Width / client.Height / ((double)pack.ReferenceWidth / pack.ReferenceHeight) - 1) > .025)
            throw new InvalidOperationException("Client aspect ratio differs from template pack; recalibrate ROI.");
        var scores = pack.Templates.Where(t => t.Kind == TemplateKind.Character || t.CharacterId is null
            || t.CharacterId == context.Preset.CharacterId).Select(t => (Template: t, Score: Match(client, t))).ToArray();
        var characters = scores.Where(s => s.Template.Kind == TemplateKind.Character).OrderByDescending(s => s.Score).ToArray();
        var winner = characters.Length > 0 && characters[0].Score >= Math.Max(.9, characters[0].Template.OnThreshold)
            && (characters.Length == 1 || characters[0].Score - characters[1].Score >= .05) ? characters[0].Template.Id : null;
        List<GameEvent> events = []; List<DetectorDecision> decisions = [];
        foreach (var (t, score) in scores)
        {
            if (!states.TryGetValue(t.Id, out var state)) states[t.Id] = state = new();
            // 0 absent; 1 present; 2 ambiguous. Character templates only emit the unambiguous winner.
            var observation = t.Kind == TemplateKind.Character ? (winner == t.Id ? 1 : 2)
                : score >= t.OnThreshold ? 1 : score <= t.OffThreshold ? 0 : 2;
            if (state.Candidate == observation) state.Count++; else { state.Candidate = observation; state.Count = 1; }
            string reason = "Debounce";
            if (state.Count >= t.ConfirmationFrames)
            {
                if (observation == state.Confirmed) reason = "Stable";
                else if (state.LastEmission != TimeSpan.MinValue && frame.CapturedAt - state.LastEmission < TimeSpan.FromMilliseconds(t.CooldownMilliseconds)) reason = "Cooldown";
                else
                {
                    var previous = state.Confirmed;
                    state.Confirmed = observation;
                    GameEventType? type = null;
                    if (t.Kind == TemplateKind.Character)
                    {
                        if (observation == 1 && character != t.SubjectId) { character = t.SubjectId; type = GameEventType.CharacterChanged; }
                    }
                    else type = observation switch
                    {
                        1 => GameEventType.BuffIconAppeared,
                        0 when previous != 0 => GameEventType.BuffIconDisappeared,
                        2 when previous == 1 => GameEventType.SignalLost,
                        _ => null
                    };
                    reason = type?.ToString() ?? "Unknown";
                    if (type is { } eventType)
                    {
                        state.LastEmission = frame.CapturedAt;
                        var id = Guid.NewGuid();
                        // Absence/ambiguity is a confirmed classifier decision. Preserve raw similarity in evidence.
                        var confidence = observation == 1 ? score : 1;
                        events.Add(new(id, eventType, t.SubjectId, frame.CapturedAt, confidence, "TemplateMatcher",
                            id.ToString("N"), context.SessionId, context.Preset.Version, false,
                            new(pack.Id, pack.Version, t.Id, t.Version, frame.Sequence, frame.Width, frame.Height,
                                t.Roi.X, t.Roi.Y, t.Roi.Width, t.Roi.Height, score, processedAt)));
                    }
                }
            }
            decisions.Add(new(t.Id, score, reason));
        }
        return new(events, decisions);
    }

    public static double Match(GrayFrame client, DetectionTemplate template)
    {
        var roi = template.Roi.Pixels(client.Width, client.Height);
        long error = 0;
        for (var y = 0; y < template.Height; y++)
        for (var x = 0; x < template.Width; x++)
        {
            var sx = roi.X + Math.Min(roi.Width - 1, (int)((x + .5) * roi.Width / template.Width));
            var sy = roi.Y + Math.Min(roi.Height - 1, (int)((y + .5) * roi.Height / template.Height));
            error += Math.Abs(client.Pixels[sy * client.Width + sx] - template.Pixels[y * template.Width + x]);
        }
        return 1 - (double)error / (255 * template.Width * template.Height);
    }
}
