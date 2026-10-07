using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobPilot.Mobile.Core.Features.Transfer;

/// <summary>The changes "Combinar" makes to one table of the receiving device.</summary>
public sealed class TransferTableChanges(TransferTable table)
{
    public TransferTable Table { get; } = table;

    /// <summary>New rows, with their final keys.</summary>
    public List<TransferRow> Inserts { get; } = [];

    /// <summary>Whole rows to write over the device's row with the same key.</summary>
    public List<TransferRow> Updates { get; } = [];

    /// <summary>Keys of rows deleted on the other device.</summary>
    public List<object> Deletes { get; } = [];

    /// <summary>Rows of the file not combined: deleted here, or depending on something that is not here.</summary>
    public int Skipped { get; set; }

    public bool HasChanges => Inserts.Count > 0 || Updates.Count > 0 || Deletes.Count > 0;
}

/// <summary>Everything an import would change, computed before anything is written so the owner can review it.</summary>
public sealed class TransferPlan
{
    /// <summary>One entry per table, in <see cref="TransferSchema.Tables"/> order (parents first).</summary>
    public required IReadOnlyList<TransferTableChanges> Tables { get; init; }

    public bool HasChanges => Tables.Any(t => t.HasChanges);
}

/// <summary>
/// "Combinar": merges a transfer file into the receiving device's data. Rows are matched by identity (not by local ids), keys and
/// references are translated to the device's keys, the file wins for edits without a date, the later date wins otherwise, and
/// deletions recorded on either device are applied unless the row changed after the deletion. Pure: the apps read and write.
/// </summary>
public static class TransferMerger
{
    private static readonly string[] StageKinds = ["Created", "StageChanged"];
    private static readonly string[] ReminderKinds = ["ReminderSet", "FollowUpSent", "ResponseReceived"];

    public static TransferPlan Merge(TransferData existing, TransferData incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        return new Merging(existing, incoming).Run();
    }

    private sealed class Merging(TransferData existing, TransferData incoming)
    {
        // Incoming key → receiving device key, per table.
        private readonly Dictionary<string, Dictionary<string, object>> _maps = new(StringComparer.Ordinal);
        private readonly Dictionary<long, long> _versions = [];

        // Deletions known on either device, in the receiving device's keys: table → identity → latest deletion date.
        private readonly Dictionary<string, Dictionary<string, object>> _deletions = new(StringComparer.Ordinal);
        private readonly List<TransferRow> _newDeletions = [];
        private readonly HashSet<string> _knownDeletions = new(StringComparer.Ordinal);

        // The receiving device's rows as they will be after the merge: table → key → row.
        private readonly Dictionary<string, Dictionary<string, TransferRow>> _current = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TransferTableChanges> _changes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _touchedApplications = new(StringComparer.Ordinal);

        public TransferPlan Run()
        {
            foreach (var table in TransferSchema.Tables)
            {
                _maps[table.Name] = new(StringComparer.Ordinal);
                _changes[table.Name] = new TransferTableChanges(table);
                _current[table.Name] = existing.Rows(table.Name).ToDictionary(r => TransferValues.KeyOf(r[table.Key]), r => r, StringComparer.Ordinal);
            }

            LoadLocalDeletions();
            foreach (var table in TransferSchema.Tables.Where(t => t.Name != TransferSchema.DeletedRecords))
            {
                MapIncomingDeletions(table);
                ApplyDeletions(table);
                MergeRows(table);
            }

            RecomputeApplications();
            AddNewDeletions();
            return new TransferPlan { Tables = TransferSchema.Tables.Select(t => _changes[t.Name]).ToList() };
        }

        private void LoadLocalDeletions()
        {
            foreach (var row in existing.Rows(TransferSchema.DeletedRecords))
            {
                _knownDeletions.Add(DeletionKey(row));
                if (row.Text("TableName") is { } name && TransferSchema.Tables.FirstOrDefault(t => t.Name == name) is { TracksDeletions: true } table
                    && row.Text("Identity") is { } json && TransferValues.ReadIdentity(json) is { } identity)
                {
                    AddDeletion(table, TransferValues.IdentityKey(table, identity), row["DeletedAtUtc"]);
                }
            }
        }

        /// <summary>Translates the file's deletions of a table to the receiving device's keys (parents are already mapped).</summary>
        private void MapIncomingDeletions(TransferTable table)
        {
            if (!table.TracksDeletions)
            {
                return;
            }

            foreach (var row in incoming.Rows(TransferSchema.DeletedRecords).Where(r => r.Text("TableName") == table.Name))
            {
                if (row.Text("Identity") is not { } json || TransferValues.ReadIdentity(json) is not { } identity)
                {
                    continue;
                }

                var translated = true;
                foreach (var reference in table.Refs.Where(r => table.Identity.Contains(r.Column)))
                {
                    if (identity.TryGetValue(reference.Column, out var value) && value is not null)
                    {
                        if (_maps[reference.Table].TryGetValue(TransferValues.KeyOf(value), out var mapped))
                        {
                            identity[reference.Column] = mapped;
                        }
                        else
                        {
                            // The parent is not on this device: nothing here can match.
                            translated = false;
                        }
                    }
                }

                if (!translated)
                {
                    continue;
                }

                AddDeletion(table, TransferValues.IdentityKey(table, identity), row["DeletedAtUtc"]);
                var local = new TransferRow
                {
                    ["Id"] = null,
                    ["TableName"] = table.Name,
                    ["Identity"] = TransferValues.WriteIdentity(table, identity),
                    ["DeletedAtUtc"] = row["DeletedAtUtc"],
                };
                if (_knownDeletions.Add(DeletionKey(local)))
                {
                    _newDeletions.Add(local);
                }
            }
        }

        private void AddDeletion(TransferTable table, string identity, object? deletedAt)
        {
            if (deletedAt is null)
            {
                return;
            }

            var byIdentity = _deletions.TryGetValue(table.Name, out var found) ? found : _deletions[table.Name] = new(StringComparer.Ordinal);
            if (!byIdentity.TryGetValue(identity, out var current) || TransferValues.Compare(deletedAt, current) > 0)
            {
                byIdentity[identity] = deletedAt;
            }
        }

        /// <summary>A deletion applies to rows not changed after it.</summary>
        private bool IsDeleted(TransferTable table, string identity, IReadOnlyDictionary<string, object?> row) =>
            _deletions.TryGetValue(table.Name, out var byIdentity) && byIdentity.TryGetValue(identity, out var deletedAt)
            && TransferValues.Compare(TransferValues.Changed(table, row), deletedAt) <= 0;

        private void ApplyDeletions(TransferTable table)
        {
            if (!table.TracksDeletions)
            {
                return;
            }

            foreach (var (key, row) in _current[table.Name].ToList())
            {
                if (IsDeleted(table, TransferValues.IdentityKey(table, row), row))
                {
                    _changes[table.Name].Deletes.Add(row[table.Key]!);
                    _current[table.Name].Remove(key);
                }
            }
        }

        private void MergeRows(TransferTable table)
        {
            var changes = _changes[table.Name];
            var current = _current[table.Name];
            var byIdentity = new Dictionary<string, TransferRow>(StringComparer.Ordinal);
            foreach (var row in current.Values)
            {
                byIdentity.TryAdd(TransferValues.IdentityKey(table, row), row);
            }

            var inserted = new HashSet<TransferRow>(ReferenceEqualityComparer.Instance);
            var nextId = current.Values.Select(r => r[table.Key]).OfType<long>().DefaultIfEmpty(0).Max() + 1;
            var usedVersions = table.Name == TransferSchema.MasterCvVersions
                ? current.Values.Select(r => r["Version"]).OfType<long>().ToHashSet()
                : [];
            var latestCheck = table.Policy == TransferPolicy.MailCheck ? current.Values.Select(r => r["StartedAtUtc"]).Max(TransferValuesComparer.Instance) : null;

            IEnumerable<TransferRow> rows = incoming.Rows(table.Name);
            if (table.Policy == TransferPolicy.MailCheck)
            {
                rows = rows.OrderBy(r => r["StartedAtUtc"], TransferValuesComparer.Instance);
            }

            foreach (var source in rows)
            {
                var row = source.Copy();
                var incomingKey = TransferValues.KeyOf(source[table.Key]);
                if (!TranslateReferences(table, row, out var unresolvedValues))
                {
                    changes.Skipped++;
                    continue;
                }

                var identity = TransferValues.IdentityKey(table, row);
                if (table.TracksDeletions && IsDeleted(table, identity, row))
                {
                    changes.Skipped++;
                    continue;
                }

                if (byIdentity.TryGetValue(identity, out var match))
                {
                    _maps[table.Name][incomingKey] = match[table.Key]!;
                    if (table.Name == TransferSchema.MasterCvVersions && source["Version"] is long fileVersion && match["Version"] is long localVersion)
                    {
                        _versions[fileVersion] = localVersion;
                    }

                    row[table.Key] = match[table.Key];
                    ResolveValues(row, unresolvedValues, match);
                    if (table.Name == TransferSchema.Applications)
                    {
                        _touchedApplications.Add(TransferValues.KeyOf(match[table.Key]));
                    }

                    if (Combine(table, match, row) is { } merged && !SameValues(table, merged, match))
                    {
                        if (inserted.Contains(match))
                        {
                            changes.Inserts[changes.Inserts.IndexOf(match)] = merged;
                            inserted.Add(merged);
                        }
                        else
                        {
                            changes.Updates.RemoveAll(u => ReferenceEquals(u, match));
                            changes.Updates.Add(merged);
                        }

                        byIdentity[identity] = merged;
                        current[TransferValues.KeyOf(merged[table.Key])] = merged;
                    }

                    continue;
                }

                if (table.Policy == TransferPolicy.MailCheck && TransferValues.Compare(row["StartedAtUtc"], latestCheck) <= 0)
                {
                    // Older than this device's last check: the Panel shows the latest one, by id.
                    changes.Skipped++;
                    continue;
                }

                row[table.Key] = table.KeyKind switch
                {
                    TransferKeyKind.AutoIncrement => nextId++,
                    TransferKeyKind.Guid when current.ContainsKey(incomingKey) => Guid.NewGuid().ToString("D").ToUpperInvariant(),
                    _ => source[table.Key],
                };
                ResolveValues(row, unresolvedValues, null);
                if (table.Name == TransferSchema.MasterCvVersions && source["Version"] is long version)
                {
                    var assigned = usedVersions.Contains(version) ? usedVersions.Max() + 1 : version;
                    usedVersions.Add(assigned);
                    _versions[version] = assigned;
                    row["Version"] = assigned;
                }

                _maps[table.Name][incomingKey] = row[table.Key]!;
                if (table.Name == TransferSchema.Applications)
                {
                    _touchedApplications.Add(TransferValues.KeyOf(row[table.Key]));
                }

                changes.Inserts.Add(row);
                inserted.Add(row);
                byIdentity[identity] = row;
                current[TransferValues.KeyOf(row[table.Key])] = row;
            }
        }

        /// <summary>Translates foreign keys to this device's keys. False when a required parent is missing.</summary>
        private bool TranslateReferences(TransferTable table, TransferRow row, out List<string> unresolvedValues)
        {
            unresolvedValues = [];
            foreach (var reference in table.Refs)
            {
                if (row[reference.Column] is not { } value)
                {
                    continue;
                }

                if (reference.Table == TransferSchema.CvVersionReference)
                {
                    if (value is long version && _versions.TryGetValue(version, out var mappedVersion))
                    {
                        row[reference.Column] = mappedVersion;
                    }

                    continue;
                }

                if (_maps[reference.Table].TryGetValue(TransferValues.KeyOf(value), out var mapped))
                {
                    row[reference.Column] = mapped;
                    continue;
                }

                switch (reference.Kind)
                {
                    case TransferReferenceKind.Required:
                        return false;
                    case TransferReferenceKind.Optional:
                        row[reference.Column] = null;
                        break;
                    default:
                        unresolvedValues.Add(reference.Column);
                        break;
                }
            }

            return true;
        }

        private static void ResolveValues(TransferRow row, List<string> columns, TransferRow? match)
        {
            foreach (var column in columns)
            {
                row[column] = match?[column] ?? 0L;
            }
        }

        /// <summary>The row to keep for a match, or null to keep the device's row.</summary>
        private TransferRow? Combine(TransferTable table, TransferRow local, TransferRow file) => table.Policy switch
        {
            TransferPolicy.KeepExisting or TransferPolicy.MailCheck => null,
            TransferPolicy.Newer => TransferValues.Compare(file[table.NewerColumn!], local[table.NewerColumn!]) > 0 ? file : null,
            TransferPolicy.FileWins => FileWins(table, local, file),
            TransferPolicy.Posting => Posting(local, file),
            TransferPolicy.Application => Application(local, file),
            TransferPolicy.Practice => Practice(local, file),
            TransferPolicy.MailWaiting => Waiting(local, file),
            _ => throw new InvalidOperationException($"Unknown policy {table.Policy}."),
        };

        private static TransferRow FileWins(TransferTable table, TransferRow local, TransferRow file)
        {
            var merged = file.Copy();
            foreach (var column in table.StickyColumns ?? [])
            {
                if (merged[column] is null && local[column] is not null)
                {
                    merged[column] = local[column];
                }
            }

            return merged;
        }

        private static TransferRow Posting(TransferRow local, TransferRow file)
        {
            var merged = file.Copy();
            if (string.IsNullOrEmpty(file.Text("Description")) && !string.IsNullOrEmpty(local.Text("Description")))
            {
                merged["Description"] = local["Description"];
            }

            merged["FirstSeenAtUtc"] = TransferValues.Compare(local["FirstSeenAtUtc"], file["FirstSeenAtUtc"]) < 0 ? local["FirstSeenAtUtc"] : file["FirstSeenAtUtc"];
            merged["LastSeenAtUtc"] = TransferValues.Compare(local["LastSeenAtUtc"], file["LastSeenAtUtc"]) > 0 ? local["LastSeenAtUtc"] : file["LastSeenAtUtc"];
            merged["Sightings"] = UnionByUrl(local.Text("Sightings"), file.Text("Sightings"));
            return merged;
        }

        private static TransferRow Application(TransferRow local, TransferRow file)
        {
            // Stage and reminder are recomputed from the combined timeline afterwards.
            var merged = (TransferValues.Compare(file["StageChangedAtUtc"], local["StageChangedAtUtc"]) > 0 ? file : local).Copy();
            merged["CreatedAtUtc"] = TransferValues.Compare(local["CreatedAtUtc"], file["CreatedAtUtc"]) < 0 ? local["CreatedAtUtc"] : file["CreatedAtUtc"];
            return merged;
        }

        private static TransferRow? Practice(TransferRow local, TransferRow file)
        {
            var localDone = local["CompletedAtUtc"] is not null;
            var fileDone = file["CompletedAtUtc"] is not null;
            if (localDone != fileDone)
            {
                return fileDone ? file : null;
            }

            return (file.Text("Turns")?.Length ?? 0) >= (local.Text("Turns")?.Length ?? 0) ? file : null;
        }

        private static TransferRow Waiting(TransferRow local, TransferRow file)
        {
            var sameMessage = TransferValues.AreEqual(local["LastMessageId"], file["LastMessageId"]);
            var merged = (TransferValues.Compare(file["LastReceivedUtc"], local["LastReceivedUtc"]) > 0 ? file : local).Copy();
            if (sameMessage)
            {
                // "Listo" on either device hides the card until a newer message.
                merged["DoneAtUtc"] = TransferValues.Compare(local["DoneAtUtc"], file["DoneAtUtc"]) > 0 ? local["DoneAtUtc"] : file["DoneAtUtc"];
            }

            merged["FirstSeenUtc"] = TransferValues.Compare(local["FirstSeenUtc"], file["FirstSeenUtc"]) < 0 ? local["FirstSeenUtc"] : file["FirstSeenUtc"];
            merged["LastCheckId"] = TransferValues.Compare(local["LastCheckId"], file["LastCheckId"]) > 0 ? local["LastCheckId"] : file["LastCheckId"];
            return merged;
        }

        /// <summary>Stage and reminder follow the latest events of the combined timeline, as the domain methods set them.</summary>
        private void RecomputeApplications()
        {
            var table = TransferSchema.Get(TransferSchema.Applications);
            var changes = _changes[table.Name];
            var events = _current[TransferSchema.ApplicationEvents].Values
                .GroupBy(e => TransferValues.KeyOf(e["ApplicationId"]))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var key in _touchedApplications)
            {
                if (!_current[table.Name].TryGetValue(key, out var application) || !events.TryGetValue(key, out var timeline))
                {
                    continue;
                }

                var updated = application.Copy();
                if (Latest(timeline, StageKinds, requireStage: true) is { } stage)
                {
                    updated["Stage"] = stage["ToStage"];
                    updated["StageChangedAtUtc"] = stage["OccurredAtUtc"];
                }

                if (Latest(timeline, ReminderKinds, requireStage: false) is { } reminder)
                {
                    updated["ReminderAtUtc"] = reminder.Text("Kind") == "ReminderSet" ? reminder["ScheduledAtUtc"] : null;
                }

                if (SameValues(table, updated, application))
                {
                    continue;
                }

                var insertIndex = changes.Inserts.FindIndex(r => ReferenceEquals(r, application));
                if (insertIndex >= 0)
                {
                    changes.Inserts[insertIndex] = updated;
                }
                else
                {
                    changes.Updates.RemoveAll(r => ReferenceEquals(r, application));
                    changes.Updates.Add(updated);
                }

                _current[table.Name][key] = updated;
            }
        }

        private static TransferRow? Latest(List<TransferRow> timeline, string[] kinds, bool requireStage) =>
            timeline
                .Where(e => e.Text("Kind") is { } kind && kinds.Contains(kind) && (!requireStage || e["ToStage"] is not null))
                .OrderBy(e => e["OccurredAtUtc"], TransferValuesComparer.Instance)
                .LastOrDefault();

        private void AddNewDeletions()
        {
            var changes = _changes[TransferSchema.DeletedRecords];
            var nextId = existing.Rows(TransferSchema.DeletedRecords).Select(r => r["Id"]).OfType<long>().DefaultIfEmpty(0).Max() + 1;
            foreach (var row in _newDeletions)
            {
                row["Id"] = nextId++;
                changes.Inserts.Add(row);
            }

            changes.Skipped = incoming.Rows(TransferSchema.DeletedRecords).Count - _newDeletions.Count;
        }

        private static string DeletionKey(TransferRow row)
        {
            var table = row.Text("TableName") ?? string.Empty;
            var identity = row.Text("Identity") is { } json && TransferValues.ReadIdentity(json) is { } values
                && TransferSchema.Tables.FirstOrDefault(t => t.Name == table) is { } definition
                    ? TransferValues.IdentityKey(definition, values)
                    : row.Text("Identity");
            return $"{table}\u001e{identity}\u001e{TransferValues.KeyOf(row["DeletedAtUtc"])}";
        }

        private static bool SameValues(TransferTable table, TransferRow left, TransferRow right) =>
            table.Columns.All(c => TransferValues.AreEqual(left.GetValueOrDefault(c), right.GetValueOrDefault(c)));

        /// <summary>Combines two JSON arrays of sightings, one per URL (the device's first).</summary>
        private static string? UnionByUrl(string? local, string? file)
        {
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(file))
            {
                return string.IsNullOrEmpty(file) ? local : file;
            }

            try
            {
                var result = new JsonArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var json in new[] { local, file })
                {
                    if (JsonNode.Parse(json) is not JsonArray items)
                    {
                        return file;
                    }

                    foreach (var item in items)
                    {
                        var url = item is JsonObject obj ? (obj["Url"] ?? obj["url"])?.ToString() : null;
                        if (item is not null && seen.Add(url ?? item.ToJsonString()))
                        {
                            result.Add(item.DeepClone());
                        }
                    }
                }

                return result.ToJsonString();
            }
            catch (JsonException)
            {
                return file;
            }
        }
    }

    private sealed class TransferValuesComparer : IComparer<object?>
    {
        public static TransferValuesComparer Instance { get; } = new();

        public int Compare(object? x, object? y) => TransferValues.Compare(x, y);
    }
}
