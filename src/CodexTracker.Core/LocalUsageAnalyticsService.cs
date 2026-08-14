using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CodexTracker.Core;

public sealed record ModelUsage(string Model, long Tokens, decimal CostUsd, bool Priced);
public sealed record DailyTokenUsage(DateTime Day, long Tokens, decimal UsdCost = 0, decimal BrlCost = 0);
public sealed record TimedTokenUsage(DateTimeOffset At, long Tokens, decimal CostUsd = 0);
public sealed record TimedModelUsage(DateTimeOffset At, string Model, long Tokens, decimal CostUsd = 0, bool Priced = false);
public sealed record UsageWindowEstimate(long Tokens, decimal CostUsd, decimal CostBrl);
public sealed record UsageAnalytics(long TodayTokens, long MonthTokens, decimal MonthUsd, decimal MonthBrl, double CoveragePercent, IReadOnlyList<ModelUsage> Models, decimal TodayUsd = 0, decimal TodayBrl = 0, IReadOnlyList<DailyTokenUsage>? DailySeries = null, IReadOnlyList<TimedTokenUsage>? Timeline = null, decimal UsdBrl = 0, IReadOnlyList<TimedModelUsage>? ModelTimeline = null)
{
    public long TokensInWindow(DateTimeOffset startInclusive, DateTimeOffset endExclusive) =>
        (Timeline ?? []).Where(x => x.At >= startInclusive && x.At < endExclusive).Sum(x => x.Tokens);

    public UsageWindowEstimate EstimateInWindow(DateTimeOffset startInclusive, DateTimeOffset endExclusive)
    {
        var events = (Timeline ?? []).Where(x => x.At >= startInclusive && x.At < endExclusive).ToArray();
        var costUsd = events.Sum(x => x.CostUsd);
        return new(events.Sum(x => x.Tokens), costUsd, costUsd * UsdBrl);
    }

    public IReadOnlyList<ModelUsage> ModelsInWindow(DateTimeOffset startInclusive, DateTimeOffset endExclusive) =>
        (ModelTimeline ?? []).Where(x => x.At >= startInclusive && x.At < endExclusive)
            .GroupBy(x => new { x.Model, x.Priced })
            .Select(group => new ModelUsage(group.Key.Model, group.Sum(x => x.Tokens), group.Sum(x => x.CostUsd), group.Key.Priced))
            .OrderByDescending(x => x.Tokens)
            .ToArray();

    public long? TokensInWeeklyWindow(QuotaWindow? weekly) => weekly?.ResetsAt is { } reset
        ? TokensInWindow(reset.AddDays(-7), reset)
        : null;

    public UsageWindowEstimate? EstimateInWeeklyWindow(QuotaWindow? weekly) => weekly?.ResetsAt is { } reset
        ? EstimateInWindow(reset.AddDays(-7), reset)
        : null;
}

public sealed class LocalUsageAnalyticsService
{
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxParseParallelism;
    private readonly string? _stateDatabasePath;
    private ThreadModelIndex? _threadModelIndex;
    private string? _threadModelIndexPath;

    public LocalUsageAnalyticsService(Func<DateTimeOffset>? clock = null, int? maxParseParallelism = null, string? stateDatabasePath = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        _maxParseParallelism = Math.Max(1, Math.Min(2, maxParseParallelism ?? Environment.ProcessorCount));
        _stateDatabasePath = stateDatabasePath;
    }
    public int FilesParsedLastRead { get; private set; }
    public int FilesRebuiltLastRead { get; private set; }
    public int FilesAppendedLastRead { get; private set; }
    public long BytesReadLastRead { get; private set; }
    public int LogicalStreamsLastRead { get; private set; }
    public int DuplicatePhysicalFilesIgnoredLastRead { get; private set; }
    public int CachedBucketCount => _cache.Values.Sum(x => x.Buckets.Count);
    public int CachedTimelineEntryCount => _cache.Values.Sum(x => x.Timeline.Count);
    private static readonly Dictionary<string, (decimal Input, decimal Cached, decimal Output)> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.6-sol"] = (5m, .5m, 30m), ["gpt-5.6-terra"] = (2.5m, .25m, 15m),
        ["gpt-5.6-luna"] = (1m, .1m, 6m), ["gpt-5.3-codex"] = (1.75m, .175m, 14m)
    };

    public Task<UsageAnalytics> ReadAsync(decimal usdBrl, string? root = null) => Task.Run(() => Read(usdBrl, root));

    public static IReadOnlyList<string> DefaultRoots(string userProfile) =>
    [
        Path.Combine(userProfile, ".codex", "sessions"),
        Path.Combine(userProfile, ".codex", "archived_sessions")
    ];

    public UsageAnalytics Read(decimal usdBrl, string? root = null)
    {
        FilesParsedLastRead = 0;
        FilesRebuiltLastRead = 0;
        FilesAppendedLastRead = 0;
        BytesReadLastRead = 0;
        LogicalStreamsLastRead = 0;
        DuplicatePhysicalFilesIgnoredLastRead = 0;
        var stopwatch = Stopwatch.StartNew();
        var now = _clock();
        // An active seven-day quota window can start no earlier than seven days ago.
        // Keep one extra day for clock/refresh skew while bounding memory independently
        // of the total session-history size.
        var timelineCutoff = now.ToUniversalTime().AddDays(-8);
        var buckets = new Dictionary<BucketKey, Aggregate>();
        var timeline = new Dictionary<TimelineKey, TimelineAggregate>();
        var modelTimeline = new Dictionary<ModelTimelineKey, TimelineAggregate>();
        var roots = root is not null
            ? [root]
            : DefaultRoots(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var fallbackModels = ReadFallbackModels(root);
        var files = roots.Where(Directory.Exists).SelectMany(path => Directory.EnumerateFiles(path, "*.jsonl", SearchOption.AllDirectories))
            .Select(file => Describe(file)).ToArray();
        if (files.Length == 0) return new(0, 0, 0, 0, 0, []);
        var activePaths = files.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _cache.Keys.Where(x => !activePaths.Contains(x)).ToArray()) _cache.Remove(stale);
        var candidateCaches = new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase);
        var parsePlans = new List<ParsePlan>(files.Length);
        foreach (var file in files)
        {
            try
            {
                var signature = FileSignature.Create(file.Path);
                var fallbackModel = fallbackModels.TryGetValue(file.FileId, out var indexedModel) ? indexedModel : "unknown";
                // Active JSONL files may end mid-record. Do not advance the cache offset over an incomplete line;
                // the next read rebuilds this file after the writer commits its terminating newline.
                var hasCommittedTail = HasFinalNewline(file.Path, signature.Length);
                if (!hasCommittedTail)
                {
                    parsePlans.Add(new(file, signature, null, ParseKind.Partial, fallbackModel));
                    _cache.Remove(file.Path);
                    continue;
                }
                if (!_cache.TryGetValue(file.Path, out var cached) || cached.IsFork != file.IsFork || cached.FallbackModel != fallbackModel || signature.Length < cached.Signature.Length || (signature.Length == cached.Signature.Length && signature.LastWriteUtcTicks != cached.Signature.LastWriteUtcTicks) || (signature.Length > cached.Signature.Length && PrefixMarker.Create(file.Path, cached.Signature.Length) != cached.PrefixMarker))
                {
                    parsePlans.Add(new(file, signature, null, ParseKind.Rebuild, fallbackModel));
                }
                else if (signature.Length > cached.Signature.Length)
                {
                    parsePlans.Add(new(file, signature, cached, ParseKind.Append, fallbackModel));
                }
                else candidateCaches[file.Path] = cached;
            }
            catch (Exception ex) { SanitizedLogger.Write("Analytics file error: " + ex.GetType().Name); }
        }
        var parseResults = new ParsePlanResult[parsePlans.Count];
        Parallel.For(0, parsePlans.Count, new ParallelOptions { MaxDegreeOfParallelism = _maxParseParallelism }, index =>
        {
            var plan = parsePlans[index];
            try
            {
                var offset = plan.Kind == ParseKind.Append ? plan.Previous!.Signature.Length : 0;
                var baseline = plan.Kind == ParseKind.Append ? plan.Previous!.LastTotals : null;
                var model = plan.Kind == ParseKind.Append ? plan.Previous!.LastModel : plan.FallbackModel;
                parseResults[index] = new(plan, ParseAggregate(plan.File.Path, offset, plan.Signature.Length, baseline, model, plan.File.IsFork, timelineCutoff), null);
            }
            catch (Exception ex) { parseResults[index] = new(plan, null, ex); }
        });
        foreach (var result in parseResults)
        {
            if (result.Error is not null) { SanitizedLogger.Write("Analytics file error: " + result.Error.GetType().Name); continue; }
            var plan = result.Plan;
            var parsed = result.Parsed!;
            if (parsed.MalformedLineCount > 0)
                SanitizedLogger.Write("Analytics malformed JSONL lines skipped: " + parsed.MalformedLineCount);
            CachedFile cached;
            if (plan.Kind == ParseKind.Append)
            {
                cached = plan.Previous!;
                var previousLength = cached.Signature.Length;
                MergeBuckets(cached.Buckets, parsed.Buckets);
                MergeTimeline(cached.Timeline, parsed.Timeline);
                MergeModelTimeline(cached.ModelTimeline, parsed.ModelTimeline);
                cached = cached with { Signature = plan.Signature, FallbackModel = plan.FallbackModel, LastTotals = parsed.LastTotals ?? cached.LastTotals, LastModel = parsed.LastModel, PrefixMarker = PrefixMarker.Create(plan.File.Path, plan.Signature.Length) };
                FilesAppendedLastRead++;
                BytesReadLastRead += plan.Signature.Length - previousLength;
            }
            else
            {
                cached = new CachedFile(plan.Signature, plan.File.IsFork, plan.FallbackModel, parsed.Buckets, parsed.Timeline, parsed.ModelTimeline, parsed.LastTotals, parsed.LastModel, plan.Kind == ParseKind.Partial ? "" : PrefixMarker.Create(plan.File.Path, plan.Signature.Length));
                FilesRebuiltLastRead++;
                BytesReadLastRead += plan.Signature.Length;
            }
            FilesParsedLastRead++;
            if (plan.Kind != ParseKind.Partial) _cache[plan.File.Path] = cached;
            candidateCaches[plan.File.Path] = cached;
        }
        foreach (var cached in candidateCaches.Values)
        {
            foreach (var staleTimeline in cached.Timeline.Keys.Where(key => key.At < timelineCutoff).ToArray()) cached.Timeline.Remove(staleTimeline);
            foreach (var staleTimeline in cached.ModelTimeline.Keys.Where(key => key.At < timelineCutoff).ToArray()) cached.ModelTimeline.Remove(staleTimeline);
        }
        var candidates = files.Where(file => candidateCaches.ContainsKey(file.Path)).Select(file => new LogicalFileCandidate(file, candidateCaches[file.Path])).ToList();
        // Only collapse physical copies when their bytes prove that one is a checkpoint-prefix
        // of the other. Equal session ids alone are insufficient: a subagent JSONL embeds its
        // parent metadata and independent segments may legitimately share ancestry.
        var logicalCandidates = candidates.GroupBy(x => x.File.FileId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(SelectNonOverlappingCandidates)
            .ToArray();
        foreach (var candidate in logicalCandidates)
        {
            MergeBuckets(buckets, candidate.Cache.Buckets);
            MergeTimeline(timeline, candidate.Cache.Timeline);
            MergeModelTimeline(modelTimeline, candidate.Cache.ModelTimeline);
        }
        LogicalStreamsLastRead = logicalCandidates.Length;
        DuplicatePhysicalFilesIgnoredLastRead = candidates.Count - logicalCandidates.Length;
        var month = buckets.Where(x => x.Key.Day.Year == now.Year && x.Key.Day.Month == now.Month);
        var models = month.GroupBy(x => x.Key.Model).Select(g =>
        {
            var tokens = g.Sum(x => x.Value.Total);
            var key = Prices.Keys.FirstOrDefault(k => string.Equals(g.Key, k, StringComparison.OrdinalIgnoreCase));
            if (key is null) return new ModelUsage(g.Key, tokens, 0, false);
            return new ModelUsage(g.Key, tokens, CalculateCostUsd(g.Select(x => x.Value), Prices[key]), true);
        }).OrderByDescending(x => x.Tokens).ToArray();
        var total = models.Sum(x => x.Tokens);
        var todayBuckets = buckets.Where(x => x.Key.Day == now.LocalDateTime.Date).ToArray();
        var todayUsd = CalculateCostUsd(todayBuckets);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var dailySeries = Enumerable.Range(1, daysInMonth)
            .Select(day =>
            {
                var date = new DateTime(now.Year, now.Month, day);
                var dayBuckets = buckets.Where(x => x.Key.Day == date).ToArray();
                var costUsd = CalculateCostUsd(dayBuckets);
                return new DailyTokenUsage(date, dayBuckets.Sum(x => x.Value.Total), costUsd, costUsd * usdBrl);
            })
            .ToArray();
        var timedSeries = timeline.OrderBy(x => x.Key.At).Select(x => new TimedTokenUsage(x.Key.At, x.Value.Tokens, x.Value.CostUsd)).ToArray();
        var timedModelSeries = modelTimeline.OrderBy(x => x.Key.At).Select(x => new TimedModelUsage(x.Key.At, x.Key.Model, x.Value.Tokens, x.Value.CostUsd, x.Key.Priced)).ToArray();
        SanitizedLogger.Write("Analytics refreshed: models=" + models.Length + ", files=" + files.Length + ", streams=" + LogicalStreamsLastRead + ", duplicateSnapshots=" + DuplicatePhysicalFilesIgnoredLastRead + ", ms=" + stopwatch.ElapsedMilliseconds);
        return new(todayBuckets.Sum(x => x.Value.Total), total, models.Sum(x => x.CostUsd), models.Sum(x => x.CostUsd) * usdBrl, total == 0 ? 0 : 100d * models.Where(x => x.Priced).Sum(x => x.Tokens) / total, models, todayUsd, todayUsd * usdBrl, dailySeries, timedSeries, usdBrl, timedModelSeries);
    }

    private static decimal CalculateCostUsd(IEnumerable<Aggregate> aggregates, (decimal Input, decimal Cached, decimal Output) price) => aggregates.Sum(value => (Math.Max(value.Input - value.Cached, 0) * price.Input + value.Cached * price.Cached + (value.Output + value.Reasoning) * price.Output) / 1_000_000m);

    private IReadOnlyDictionary<string, string> ReadFallbackModels(string? root)
    {
        var databasePath = _stateDatabasePath ?? (root is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "state_5.sqlite") : null);
        if (string.IsNullOrWhiteSpace(databasePath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_threadModelIndex is null || !string.Equals(_threadModelIndexPath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            _threadModelIndex = new ThreadModelIndex(databasePath!);
            _threadModelIndexPath = databasePath;
        }
        return _threadModelIndex.Read();
    }

    private static decimal CalculateCostUsd(IEnumerable<KeyValuePair<BucketKey, Aggregate>> buckets)
    {
        var total = 0m;
        foreach (var bucket in buckets)
        {
            var key = Prices.Keys.FirstOrDefault(priceKey => string.Equals(bucket.Key.Model, priceKey, StringComparison.OrdinalIgnoreCase));
            if (key is not null) total += CalculateCostUsd([bucket.Value], Prices[key]);
        }
        return total;
    }

    private static FileDescriptor Describe(string path)
    {
        string? sessionId = null;
        string? fileId = null;
        DateTimeOffset? startedAt = null;
        var fork = false;
        try
        {
            foreach (var line in File.ReadLines(path).Take(8))
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                JsonElement meta;
                if (root.TryGetProperty("type", out var type) && type.GetString() == "session_meta" && root.TryGetProperty("payload", out var realPayload) && realPayload.ValueKind == JsonValueKind.Object)
                    meta = realPayload;
                else if (root.TryGetProperty("session_meta", out var legacyMeta) && legacyMeta.ValueKind == JsonValueKind.Object)
                    meta = legacyMeta;
                else continue;
                // The first metadata record belongs to this physical rollout. Later records can
                // be inherited parent context embedded in a fork and must not replace its id.
                sessionId = ReadString(meta, "session_id");
                fileId = ReadString(meta, "id");
                startedAt = ReadTimestamp(root) ?? ReadTimestamp(meta);
                fork = !string.IsNullOrWhiteSpace(ReadString(meta, "forked_from_id")) || ReadString(meta, "thread_source") == "subagent";
                break;
            }
        }
        catch { SanitizedLogger.Write("Analytics metadata skipped"); }
        return new(path, sessionId ?? path, fileId ?? path, fork, startedAt);
    }

    private static ParseAggregateResult ParseAggregate(string file, long offset, long snapshotLength, Totals? baseline, string model, bool isFork, DateTimeOffset timelineCutoff)
    {
        var buckets = new Dictionary<BucketKey, Aggregate>();
        var timeline = new Dictionary<TimelineKey, TimelineAggregate>();
        var modelTimeline = new Dictionary<ModelTimelineKey, TimelineAggregate>();
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var bounded = new BoundedReadStream(stream, Math.Max(0, snapshotLength - offset));
        using var reader = new StreamReader(bounded);
        string? line;
        var malformedLineCount = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
                var top = ReadString(root, "type"); var legacy = ReadString(payload, "type");
                if ((top == "turn_context" || legacy == "turn_context") && ReadString(payload, "model") is { } currentModel) model = currentModel;
                else if (top == "event_msg" && legacy == "thread_settings_applied" &&
                         payload.TryGetProperty("thread_settings", out var settings) && settings.ValueKind == JsonValueKind.Object &&
                         ReadString(settings, "model") is { } settingsModel) model = settingsModel;
                var type = top == "event_msg" ? ReadString(payload, "type") : legacy;
                if (type != "token_count" || !payload.TryGetProperty("info", out var info) || !info.TryGetProperty("total_token_usage", out var total)) continue;
                var current = Totals.From(total); if (current is null) continue;
                // total_token_usage is cumulative per rollout. Its first snapshot is work processed
                // by that rollout even when context was inherited by a fork. A component decrease
                // starts a new accumulator segment, whose current snapshot must also be counted.
                var delta = baseline is not { } before
                    ? current.Value
                    : current.Value.IsMonotonicAfter(before) ? current.Value - before : current.Value;
                baseline = current;
                var at = ReadTimestamp(root) ?? File.GetLastWriteTimeUtc(file);
                if (delta.Total > 0)
                {
                    var key = new BucketKey(at.LocalDateTime.Date, model);
                    buckets[key] = Net48Compatibility.GetValueOrDefault(buckets, key) + new Aggregate(delta.Input, delta.Cached, delta.Output, delta.Reasoning, delta.Total);
                    if (at >= timelineCutoff)
                    {
                        var timelineKey = new TimelineKey(at);
                        var priceKey = Prices.Keys.FirstOrDefault(key => string.Equals(model, key, StringComparison.OrdinalIgnoreCase));
                        var costUsd = priceKey is null ? 0 : CalculateCostUsd([new Aggregate(delta.Input, delta.Cached, delta.Output, delta.Reasoning, delta.Total)], Prices[priceKey]);
                        timeline[timelineKey] = Net48Compatibility.GetValueOrDefault(timeline, timelineKey) + new TimelineAggregate(delta.Total, costUsd);
                        var modelTimelineKey = new ModelTimelineKey(at, model, priceKey is not null);
                        modelTimeline[modelTimelineKey] = Net48Compatibility.GetValueOrDefault(modelTimeline, modelTimelineKey) + new TimelineAggregate(delta.Total, costUsd);
                    }
                }
            }
            catch (JsonException) { malformedLineCount++; }
        }
        return new ParseAggregateResult(buckets, timeline, modelTimeline, baseline, model, malformedLineCount);
    }

    private static string? ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? ReadTimestamp(JsonElement element) => ReadString(element, "timestamp") is { } value && DateTimeOffset.TryParse(value, out var timestamp) ? timestamp.ToUniversalTime() : null;
    private sealed record FileDescriptor(string Path, string SessionId, string FileId, bool IsFork, DateTimeOffset? StartedAt);
    private sealed record LogicalFileCandidate(FileDescriptor File, CachedFile Cache);
    private enum ParseKind { Partial, Rebuild, Append }
    private sealed record ParsePlan(FileDescriptor File, FileSignature Signature, CachedFile? Previous, ParseKind Kind, string FallbackModel);
    private sealed record ParsePlanResult(ParsePlan Plan, ParseAggregateResult? Parsed, Exception? Error);
    private sealed record CachedFile(FileSignature Signature, bool IsFork, string FallbackModel, Dictionary<BucketKey, Aggregate> Buckets, Dictionary<TimelineKey, TimelineAggregate> Timeline, Dictionary<ModelTimelineKey, TimelineAggregate> ModelTimeline, Totals? LastTotals, string LastModel, string PrefixMarker);
    private sealed record ParseAggregateResult(Dictionary<BucketKey, Aggregate> Buckets, Dictionary<TimelineKey, TimelineAggregate> Timeline, Dictionary<ModelTimelineKey, TimelineAggregate> ModelTimeline, Totals? LastTotals, string LastModel, int MalformedLineCount);
    private readonly record struct FileSignature(long Length, long LastWriteUtcTicks)
    {
        public static FileSignature Create(string path)
        {
            var info = new FileInfo(path);
            return new FileSignature(info.Length, info.LastWriteTimeUtc.Ticks);
        }
    }
    private static class PrefixMarker
    {
        public static string Create(string path, long length)
        {
            const int chunk = 2048;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Feed(stream, hash, 0, (int)Math.Min(chunk, length));
            if (length > chunk) Feed(stream, hash, Math.Max(0, length - chunk), (int)Math.Min(chunk, length));
            return Net48Compatibility.ToHexString(hash.GetHashAndReset());
        }
        private static void Feed(FileStream stream, IncrementalHash hash, long start, int count)
        {
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[count]; var read = stream.Read(buffer, 0, count);
            if (read > 0) hash.AppendData(buffer, 0, read);
        }
    }
    private static bool HasFinalNewline(string path, long length)
    {
        if (length == 0) return true;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(length - 1, SeekOrigin.Begin);
        var last = stream.ReadByte();
        return last is '\n' or '\r';
    }

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;
        private long _remaining;
        public BoundedReadStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _remaining = length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private readonly record struct BucketKey(DateTime Day, string Model);
    private readonly record struct TimelineKey(DateTimeOffset At);
    private readonly record struct ModelTimelineKey(DateTimeOffset At, string Model, bool Priced);
    private readonly record struct TimelineAggregate(long Tokens, decimal CostUsd)
    {
        public static TimelineAggregate operator +(TimelineAggregate left, TimelineAggregate right) => new(left.Tokens + right.Tokens, left.CostUsd + right.CostUsd);
    }
    private readonly record struct Aggregate(long Input, long Cached, long Output, long Reasoning, long Total)
    {
        public static Aggregate operator +(Aggregate left, Aggregate right) => new(left.Input + right.Input, left.Cached + right.Cached, left.Output + right.Output, left.Reasoning + right.Reasoning, left.Total + right.Total);
    }

    private static void MergeBuckets(Dictionary<BucketKey, Aggregate> destination, IReadOnlyDictionary<BucketKey, Aggregate> source)
    {
        foreach (var pair in source) destination[pair.Key] = Net48Compatibility.GetValueOrDefault(destination, pair.Key) + pair.Value;
    }

    private static void MergeTimeline(Dictionary<TimelineKey, TimelineAggregate> destination, IReadOnlyDictionary<TimelineKey, TimelineAggregate> source)
    {
        foreach (var pair in source) destination[pair.Key] = Net48Compatibility.GetValueOrDefault(destination, pair.Key) + pair.Value;
    }

    private static void MergeModelTimeline(Dictionary<ModelTimelineKey, TimelineAggregate> destination, IReadOnlyDictionary<ModelTimelineKey, TimelineAggregate> source)
    {
        foreach (var pair in source) destination[pair.Key] = Net48Compatibility.GetValueOrDefault(destination, pair.Key) + pair.Value;
    }

    private static IEnumerable<LogicalFileCandidate> SelectNonOverlappingCandidates(IGrouping<string, LogicalFileCandidate> group)
    {
        var files = group.OrderBy(x => x.File.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            var candidate = files[index];
            var coveredByAnother = files.Where((other, otherIndex) => otherIndex != index).Any(other =>
                other.Cache.Signature.Length > candidate.Cache.Signature.Length && IsPhysicalPrefix(candidate.File.Path, other.File.Path)
                || other.Cache.Signature.Length == candidate.Cache.Signature.Length && string.Compare(other.File.Path, candidate.File.Path, StringComparison.OrdinalIgnoreCase) < 0 && IsPhysicalPrefix(candidate.File.Path, other.File.Path));
            if (!coveredByAnother) yield return candidate;
        }
    }

    private static bool IsPhysicalPrefix(string prefixPath, string fullPath)
    {
        using var prefix = new FileStream(prefixPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var full = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (prefix.Length > full.Length) return false;
        var prefixBuffer = new byte[64 * 1024];
        var fullBuffer = new byte[64 * 1024];
        int read;
        while ((read = prefix.Read(prefixBuffer, 0, prefixBuffer.Length)) > 0)
        {
            if (full.Read(fullBuffer, 0, read) != read || !prefixBuffer.AsSpan(0, read).SequenceEqual(fullBuffer.AsSpan(0, read))) return false;
        }
        return true;
    }
    private readonly record struct Totals(long Input, long Cached, long Output, long Reasoning, long Total)
    {
        public static Totals Zero => new(0, 0, 0, 0, 0);
        public static Totals? From(JsonElement e)
        {
            long Get(string n) => e.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
            if (e.ValueKind != JsonValueKind.Object) return null;
            var input = Get("input_tokens");
            var output = Get("output_tokens");
            var reasoning = Get("reasoning_output_tokens");
            return new(input, Get("cached_input_tokens"), output, reasoning, input + output + reasoning);
        }
        public bool IsMonotonicAfter(Totals other) => Input >= other.Input && Cached >= other.Cached && Output >= other.Output && Reasoning >= other.Reasoning;
        public static Totals operator -(Totals a, Totals b) => new(a.Input - b.Input, a.Cached - b.Cached, a.Output - b.Output, a.Reasoning - b.Reasoning, a.Total - b.Total);
    }
}
